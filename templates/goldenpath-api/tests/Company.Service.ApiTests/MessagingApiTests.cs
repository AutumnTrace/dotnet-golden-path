using System.Diagnostics;
using Company.Service.Api.Features.Orders;
using Company.Service.Api.Features.Orders.Domain;
using Company.Service.Api.Infrastructure.Messaging;
using Company.Service.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Company.Service.ApiTests;

/// <summary>The messaging flow on the in-memory transport: same handlers and dispatcher as Kafka, no broker.</summary>
public class MessagingApiTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private InMemoryMessageBus Bus => factory.Services.GetRequiredService<InMemoryMessageBus>();

    [Fact]
    public async Task Placing_an_order_publishes_OrderPlaced_in_the_same_trace()
    {
        var order = await _client.PlaceOrderAsync(ApiClient.NewOrder(), Ct);

        var envelope = Assert.Single(Bus.Published, e => e.Key == order.Id.ToString());
        Assert.Equal(OrderTopics.OrderEvents, envelope.Topic);
        Assert.Equal(OrderPlaced.MessageType, envelope.Type);

        var server = await factory.Spans.WaitForAsync(
            span => span.Kind == ActivityKind.Server && Equals(span.GetTagItem("order.id"), order.Id), Ct);
        Assert.True(ActivityContext.TryParse(envelope.TraceParent, null, out var producer));
        Assert.Equal(server.TraceId, producer.TraceId);
    }

    [Fact]
    public async Task A_payment_captured_event_marks_the_order_paid()
    {
        var order = await _client.PlaceOrderAsync(ApiClient.NewOrder(), Ct);
        var capturedAt = DateTimeOffset.UtcNow;

        await Bus.PublishAsync(
            OrderTopics.PaymentEvents,
            order.Id.ToString(),
            new PaymentCaptured(Guid.CreateVersion7(), order.Id, order.TotalMinor, order.Currency, capturedAt),
            capturedAt,
            Ct);

        var paid = await _client.WaitForOrderAsync(order.Id, o => o.Status == OrderStatus.Paid, Ct);
        Assert.Equal(OrderStatus.Paid, paid.Status);
        Assert.NotNull(paid.PaidAt);
    }

    [Fact]
    public async Task A_payment_for_the_wrong_amount_leaves_the_order_unpaid()
    {
        var order = await _client.PlaceOrderAsync(ApiClient.NewOrder(), Ct);
        var capturedAt = DateTimeOffset.UtcNow;

        await Bus.PublishAsync(
            OrderTopics.PaymentEvents,
            order.Id.ToString(),
            new PaymentCaptured(Guid.CreateVersion7(), order.Id, order.TotalMinor - 1, order.Currency, capturedAt),
            capturedAt,
            Ct);

        var after = await _client.WaitForOrderAsync(order.Id, o => o.Status != OrderStatus.Placed, Ct, TimeSpan.FromSeconds(1));
        Assert.Equal(OrderStatus.Placed, after.Status);
    }
}
