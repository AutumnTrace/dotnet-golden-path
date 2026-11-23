using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Company.Service.Api.Integrations.CustomerDirectory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Company.Service.UnitTests.Integrations;

/// <summary>
/// Runs the real registration (typed client + standard resilience pipeline) against a scripted handler at the
/// HTTP boundary, so retries, timeouts and the circuit breaker are tested as configured, not mocked away.
/// </summary>
public class CustomerDirectoryClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("active", CustomerStatus.Active)]
    [InlineData("blocked", CustomerStatus.Blocked)]
    [InlineData("suspended", CustomerStatus.Blocked)]
    public async Task Maps_the_directory_status(string status, CustomerStatus expected)
    {
        var handler = new ScriptedHandler(ScriptedHandler.Ok(new { id = "cust-1", status }));
        using var services = BuildServices(handler);

        var result = await Directory(services).GetStatusAsync("cust-1", Ct);

        Assert.Equal(expected, result);
        Assert.Equal("/customers/cust-1", handler.LastPath);
    }

    [Fact]
    public async Task A_404_means_not_found_and_is_not_retried()
    {
        var handler = new ScriptedHandler(ScriptedHandler.Status(HttpStatusCode.NotFound));
        using var services = BuildServices(handler);

        var result = await Directory(services).GetStatusAsync("cust-1", Ct);

        Assert.Equal(CustomerStatus.NotFound, result);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Transient_failures_are_retried_until_one_succeeds()
    {
        var handler = new ScriptedHandler(
            ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable),
            ScriptedHandler.Status(HttpStatusCode.InternalServerError),
            ScriptedHandler.Ok(new { id = "cust-1", status = "active" }));
        using var services = BuildServices(handler);

        var result = await Directory(services).GetStatusAsync("cust-1", Ct);

        Assert.Equal(CustomerStatus.Active, result);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Persistent_failures_become_unavailable_after_the_retry_budget()
    {
        var handler = new ScriptedHandler(ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable));
        using var services = BuildServices(handler);

        var result = await Directory(services).GetStatusAsync("cust-1", Ct);

        Assert.Equal(CustomerStatus.Unavailable, result);
        Assert.Equal(1 + 2, handler.Calls);
    }

    [Fact]
    public async Task Unsafe_methods_are_never_retried()
    {
        var handler = new ScriptedHandler(ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable));
        using var services = BuildServices(handler);
        using var http = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ICustomerDirectory));

        using var response = await http.PostAsync(new Uri("customers", UriKind.Relative), content: null, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_hanging_dependency_is_cut_off_by_the_attempt_timeout()
    {
        var handler = new ScriptedHandler(ScriptedHandler.Hang);
        using var services = BuildServices(handler);
        var stopwatch = Stopwatch.StartNew();

        var result = await Directory(services).GetStatusAsync("cust-1", Ct);

        Assert.Equal(CustomerStatus.Unavailable, result);
        Assert.Equal(3, handler.Calls);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Took {stopwatch.Elapsed}; attempt timeouts did not fire.");
    }

    [Fact]
    public async Task An_open_circuit_fails_fast_without_calling_the_dependency()
    {
        var handler = new ScriptedHandler(ScriptedHandler.Status(HttpStatusCode.InternalServerError));
        using var services = BuildServices(handler, settings =>
        {
            settings["CustomerDirectory:Resilience:CircuitBreaker:MinimumThroughput"] = "2";
            settings["CustomerDirectory:Resilience:Retry:MaxRetryAttempts"] = "1";
        });
        var directory = Directory(services);

        var first = await directory.GetStatusAsync("cust-1", Ct);
        var callsWhenOpened = handler.Calls;
        var second = await directory.GetStatusAsync("cust-2", Ct);

        Assert.Equal(CustomerStatus.Unavailable, first);
        Assert.Equal(CustomerStatus.Unavailable, second);
        Assert.Equal(callsWhenOpened, handler.Calls);
    }

    [Theory]
    [InlineData("cust-1", CustomerStatus.Active)]
    [InlineData("blocked-7", CustomerStatus.Blocked)]
    [InlineData("unknown-3", CustomerStatus.NotFound)]
    public async Task The_local_stub_answers_through_the_real_pipeline(string customerId, CustomerStatus expected)
    {
        using var services = BuildServices(handler: null, settings => settings["CustomerDirectory:UseStub"] = "true");

        Assert.Equal(expected, await Directory(services).GetStatusAsync(customerId, Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    public void An_invalid_base_address_fails_validation(string baseAddress)
    {
        using var services = BuildServices(handler: null, settings => settings["CustomerDirectory:BaseAddress"] = baseAddress);

        var exception = Assert.Throws<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<CustomerDirectoryOptions>>().Value);
        Assert.Contains("BaseAddress", exception.Message, StringComparison.Ordinal);
    }

    private static ICustomerDirectory Directory(IServiceProvider services) => services.GetRequiredService<ICustomerDirectory>();

    /// <summary>Production registration with test-sized timings: 2 retries, no jitter, 200 ms attempt timeout.</summary>
    private static ServiceProvider BuildServices(ScriptedHandler? handler, Action<Dictionary<string, string?>>? adjust = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["CustomerDirectory:BaseAddress"] = "http://customer-directory.test/",
            ["CustomerDirectory:Resilience:Retry:MaxRetryAttempts"] = "2",
            ["CustomerDirectory:Resilience:Retry:BackoffType"] = "Constant",
            ["CustomerDirectory:Resilience:Retry:UseJitter"] = "false",
            ["CustomerDirectory:Resilience:Retry:Delay"] = "00:00:00.001",
            ["CustomerDirectory:Resilience:AttemptTimeout:Timeout"] = "00:00:00.200",
            ["CustomerDirectory:Resilience:TotalRequestTimeout:Timeout"] = "00:00:05",
            ["CustomerDirectory:Resilience:CircuitBreaker:FailureRatio"] = "0.5",
            ["CustomerDirectory:Resilience:CircuitBreaker:MinimumThroughput"] = "100",
            ["CustomerDirectory:Resilience:CircuitBreaker:SamplingDuration"] = "00:00:30",
            ["CustomerDirectory:Resilience:CircuitBreaker:BreakDuration"] = "00:00:30",
        };
        adjust?.Invoke(settings);

        var services = new ServiceCollection();
        services.AddLogging();
        if (handler is not null)
        {
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => handler));
        }

        services.AddCustomerDirectory(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider(validateScopes: true);
    }
}

/// <summary>Plays back one response per call; the last one repeats.</summary>
internal sealed class ScriptedHandler(params Func<CancellationToken, Task<HttpResponseMessage>>[] script) : HttpMessageHandler
{
    private int _calls;

    public static Func<CancellationToken, Task<HttpResponseMessage>> Hang { get; } = async cancellationToken =>
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new UnreachableException();
    };

    public int Calls => Volatile.Read(ref _calls);

    public string? LastPath { get; private set; }

    public static Func<CancellationToken, Task<HttpResponseMessage>> Status(HttpStatusCode status) =>
        _ => Task.FromResult(new HttpResponseMessage(status));

    public static Func<CancellationToken, Task<HttpResponseMessage>> Ok(object body) =>
        _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastPath = request.RequestUri?.AbsolutePath;
        var call = Interlocked.Increment(ref _calls) - 1;
        return script[Math.Min(call, script.Length - 1)](cancellationToken);
    }
}
