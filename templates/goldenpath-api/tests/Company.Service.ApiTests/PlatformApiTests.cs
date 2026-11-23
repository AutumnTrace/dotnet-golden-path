using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Company.Service.Api.Infrastructure.Observability;
using Company.Service.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Company.Service.ApiTests;

/// <summary>The cross-cutting contract every service on the golden path keeps: probes, telemetry, API documents.</summary>
public class PlatformApiTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Probes_answer_healthy_with_a_json_report(string url)
    {
        using var response = await _client.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Healthy", report.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Requests_produce_a_server_span_tagged_with_the_route_and_order_id()
    {
        var order = await _client.PlaceOrderAsync(ApiClient.NewOrder(), Ct);

        var span = await factory.Spans.WaitForAsync(
            activity => activity.Kind == ActivityKind.Server && Equals(activity.GetTagItem("order.id"), order.Id),
            Ct);

        // The route template, not the raw path: low cardinality, so dashboards can group by it.
        Assert.Equal("/v{version:apiVersion}/orders", (span.GetTagItem("http.route") as string)?.TrimEnd('/'));
        Assert.Equal(201, span.GetTagItem("http.response.status_code"));
    }

    [Fact]
    public async Task Health_probes_are_not_traced()
    {
        using var response = await _client.GetAsync("/health/live", Ct);
        await _client.PlaceOrderAsync(ApiClient.NewOrder(), Ct);

        await factory.Spans.WaitForAsync(activity => activity.Kind == ActivityKind.Server, Ct);
        Assert.DoesNotContain(factory.Spans.Snapshot(), activity =>
            activity.GetTagItem("url.path") is string path && path.StartsWith("/health", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Placing_an_order_increments_the_business_metric()
    {
        using var placed = new MetricCollector<long>(
            factory.Services.GetRequiredService<IMeterFactory>(), Telemetry.MeterName, "orders.placed");

        await _client.PlaceOrderAsync(ApiClient.NewOrder(), Ct);

        Assert.Contains(placed.GetMeasurementSnapshot(), m => m.Value == 1 && Equals(m.Tags["currency"], "EUR"));
    }

    [Fact]
    public async Task The_openapi_document_describes_the_versioned_endpoints()
    {
        var document = await _client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);

        var paths = document.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/v1/orders", out _));
        Assert.True(paths.TryGetProperty("/v1/orders/{id}", out _));
        Assert.True(paths.TryGetProperty("/v1/orders/{id}/cancel", out _));
        Assert.False(paths.TryGetProperty("/health/live", out _));
    }
}
