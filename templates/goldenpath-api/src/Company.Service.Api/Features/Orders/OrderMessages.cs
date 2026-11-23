using Company.Service.Api.Infrastructure.Messaging;

namespace Company.Service.Api.Features.Orders;

/// <summary>Topics this slice talks on. Producers own their topic; the version suffix changes only on breaking changes.</summary>
public static class OrderTopics
{
    public const string OrderEvents = "company-service.order-events.v1";

    /// <summary>Owned by the payments service; this slice only consumes it.</summary>
    public const string PaymentEvents = "payments.payment-events.v1";
}

/// <summary>Published when an order is accepted. Keyed by order id, so events for one order stay in order.</summary>
public sealed record OrderPlaced(Guid OrderId, string CustomerId, string Currency, long TotalMinor, DateTimeOffset PlacedAt) : IMessage
{
    public static string MessageType => "orders.order-placed.v1";
}

public sealed record OrderCancelled(Guid OrderId, string Reason, DateTimeOffset CancelledAt) : IMessage
{
    public static string MessageType => "orders.order-cancelled.v1";
}

/// <summary>Consumed from the payments service. Amounts are minor units of <see cref="Currency"/>.</summary>
public sealed record PaymentCaptured(Guid PaymentId, Guid OrderId, long AmountMinor, string Currency, DateTimeOffset CapturedAt) : IMessage
{
    public static string MessageType => "payments.payment-captured.v1";
}
