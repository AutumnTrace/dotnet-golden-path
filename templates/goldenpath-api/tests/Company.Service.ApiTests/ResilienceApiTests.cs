using System.Net;
using System.Net.Http.Json;
using Company.Service.Testing;
using Microsoft.AspNetCore.Hosting;

namespace Company.Service.ApiTests;

/// <summary>A factory with a tiny rate limit, so the limiter can be observed with a handful of requests.</summary>
public sealed class TightRateLimitFactory : ServiceFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:PermitLimit", "3");
        builder.UseSetting("RateLimiting:Window", "00:01:00");
    }
}

public class RateLimitingApiTests(TightRateLimitFactory factory) : IClassFixture<TightRateLimitFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Callers_over_the_limit_get_429_with_retry_after_and_probes_keep_working()
    {
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var response = await client.GetAsync("/v1/orders", Ct);
            statuses.Add(response.StatusCode);
        }

        using var limited = await client.GetAsync("/v1/orders", Ct);
        using var probe = await client.GetAsync("/health/ready", Ct);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("rate_limited", (await limited.ReadProblemAsync(Ct)).GetProperty("errorCode").GetString());
        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
    }
}

public class CustomerChecksApiTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("blocked-17", "orders.customer_blocked")]
    [InlineData("unknown-99", "orders.customer_not_found")]
    public async Task Orders_from_customers_the_directory_rejects_are_422s(string customerId, string errorCode)
    {
        using var response = await _client.PostAsJsonAsync("/v1/orders", ApiClient.NewOrder(customerId), ApiClient.Json, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(errorCode, (await response.ReadProblemAsync(Ct)).GetProperty("errorCode").GetString());
    }
}
