#if (UseJwt)
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
#endif
using Company.Service.Testing;
using Microsoft.AspNetCore.Hosting;
#if (UseJwt)
using Microsoft.AspNetCore.Mvc.Testing;
#endif
using Microsoft.Extensions.Options;

namespace Company.Service.ApiTests;

/// <summary>Bad configuration must stop the service at startup, not surface on the first unlucky request.</summary>
public class StartupValidationTests
{
#if (UseJwt)
    private static readonly string[] ReadOnlyScopes = ["orders:read"];

#endif
    [Theory]
    [InlineData("CustomerDirectory:BaseAddress", "", "BaseAddress")]
    [InlineData("RateLimiting:PermitLimit", "0", "PermitLimit")]
    [InlineData("Shutdown:DrainDelay", "00:00:30", "Shutdown:DrainDelay")]
    [InlineData("Observability:TraceSampleRatio", "1.5", "TraceSampleRatio")]
    public void Invalid_settings_fail_fast_with_a_message_that_names_the_setting(string key, string value, string expected)
    {
        using var factory = new MisconfiguredFactory(key, value);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validation = Assert.IsType<OptionsValidationException>(Unwrap(exception));
        Assert.Contains(expected, validation.Message, StringComparison.Ordinal);
    }

#if (UseJwt)
    [Fact]
    public void Production_refuses_to_start_without_a_way_to_validate_tokens()
    {
        using var factory = new NoSigningKeyFactory("Production");

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validation = Assert.IsType<OptionsValidationException>(Unwrap(exception));
        Assert.Contains("Authentication:Schemes:Bearer", validation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_starts_without_keys_so_a_fresh_clone_runs()
    {
        using var factory = new NoSigningKeyFactory("Development", ("Authentication:DevelopmentTokens:Enabled", "false"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task In_Development_dev_tokens_are_issued_and_accepted_by_the_api()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new NoSigningKeyFactory("Development");
        using var client = factory.CreateClient();

        using var issued = await client.PostAsJsonAsync("/dev/tokens", new { scopes = ReadOnlyScopes }, ct);
        var token = (await issued.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var read = await client.GetAsync("/v1/orders", ct);
        using var write = await client.PostAsJsonAsync("/v1/orders", ApiClient.NewOrder(), ApiClient.Json, ct);

        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public void Dev_tokens_switched_on_outside_Development_stop_the_service()
    {
        using var factory = new NoSigningKeyFactory(
            "Production",
            ("Authentication:Schemes:Bearer:Authority", "https://identity.example.test/"),
            ("Authentication:DevelopmentTokens:Enabled", "true"),
            ("Authentication:DevelopmentTokens:SigningKey", new string('k', 40)));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validation = Assert.IsType<OptionsValidationException>(Unwrap(exception));
        Assert.Contains("only allowed in the Development environment", validation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outside_Development_the_dev_token_endpoint_does_not_exist()
    {
        using var factory = new ServiceFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/dev/tokens", new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

#endif
    private static Exception Unwrap(Exception exception) =>
        exception is AggregateException { InnerExceptions.Count: 1 } aggregate ? Unwrap(aggregate.InnerExceptions[0]) : exception;

    private sealed class MisconfiguredFactory(string key, string value) : ServiceFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting(key, value);
        }
    }
#if (UseJwt)

    /// <summary>The real app with no test signing key, as it would start on a fresh machine.</summary>
    private sealed class NoSigningKeyFactory(string environment, params (string Key, string Value)[] settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
            builder.UseSetting("CustomerDirectory:UseStub", "true");
            builder.UseSetting("Shutdown:DrainDelay", "00:00:00");
#if (UsePostgres)
            builder.UseSetting("Database:Provider", "InMemory");
#endif
#if (UseKafka)
            builder.UseSetting("Messaging:Provider", "InMemory");
#endif
        }
    }
#endif
}
