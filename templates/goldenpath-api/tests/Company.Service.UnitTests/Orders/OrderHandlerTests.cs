using System.Diagnostics.Metrics;
using Company.Service.Api.Features.Orders;
using Company.Service.Api.Features.Orders.Domain;
using Company.Service.Api.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Company.Service.UnitTests.Orders;

public sealed class OrderHandlerTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 14, 9, 30, 0, TimeSpan.Zero));
    private readonly InMemoryOrderStore _store = new();
    private readonly OrdersMetrics _metrics;

    public OrderHandlerTests() => _metrics = new OrdersMetrics(_services.GetRequiredService<IMeterFactory>());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PlaceOrder_stores_the_order_counts_it_and_logs_it()
    {
        using var placed = Collect("orders.placed");
        var logger = new FakeLogger<PlaceOrderHandler>();
        var handler = new PlaceOrderHandler(_store, _clock, _metrics, logger);

        var result = await handler.HandleAsync(ValidRequest(), Ct);

        Assert.True(result.IsSuccess);
        Assert.Same(result.Value, await _store.FindAsync(result.Value.Id, Ct));
        Assert.Equal(_clock.GetUtcNow(), result.Value.PlacedAt);

        var measurement = Assert.Single(placed.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal("EUR", measurement.Tags["currency"]);

        var log = logger.Collector.LatestRecord;
        Assert.Equal(1001, log.Id.Id);
        Assert.Equal(LogLevel.Information, log.Level);
    }

    [Fact]
    public async Task PlaceOrder_returns_the_rule_violation_and_stores_nothing()
    {
        using var placed = Collect("orders.placed");
        var handler = new PlaceOrderHandler(_store, _clock, _metrics, new FakeLogger<PlaceOrderHandler>());
        var request = ValidRequest() with { Lines = [new("SKU-1", 1, 100), new("sku-1", 1, 100)] };

        var result = await handler.HandleAsync(request, Ct);

        Assert.Equal("orders.duplicate_sku", result.Error?.Code);
        Assert.Empty(await _store.ListAsync(new OrderQuery(null, null, null, 10), Ct));
        Assert.Empty(placed.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task CancelOrder_returns_not_found_for_an_unknown_id()
    {
        var handler = new CancelOrderHandler(_store, _clock, _metrics, new FakeLogger<CancelOrderHandler>());

        var result = await handler.HandleAsync(Guid.CreateVersion7(), new CancelOrderRequest("duplicate order"), Ct);

        Assert.Equal("orders.not_found", result.Error?.Code);
    }

    [Fact]
    public async Task CancelOrder_cancels_once_and_counts_once()
    {
        using var cancelled = Collect("orders.cancelled");
        var order = (await new PlaceOrderHandler(_store, _clock, _metrics, new FakeLogger<PlaceOrderHandler>())
            .HandleAsync(ValidRequest(), Ct)).Value;
        var handler = new CancelOrderHandler(_store, _clock, _metrics, new FakeLogger<CancelOrderHandler>());

        _clock.Advance(TimeSpan.FromMinutes(3));
        var first = await handler.HandleAsync(order.Id, new CancelOrderRequest("duplicate order"), Ct);
        var second = await handler.HandleAsync(order.Id, new CancelOrderRequest("retried request"), Ct);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(_clock.GetUtcNow(), order.CancelledAt);
        Assert.Single(cancelled.GetMeasurementSnapshot());
    }

    public void Dispose() => _services.Dispose();

    private static PlaceOrderRequest ValidRequest() =>
        new("cust-42", "EUR", [new("SKU-1", 2, 1_250), new("SKU-2", 1, 499)]);

    private MetricCollector<long> Collect(string instrument) =>
        new(_services.GetRequiredService<IMeterFactory>(), Telemetry.MeterName, instrument);
}
