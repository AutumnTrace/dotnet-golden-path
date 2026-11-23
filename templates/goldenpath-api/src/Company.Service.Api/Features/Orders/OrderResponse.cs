using Company.Service.Api.Features.Orders.Domain;

namespace Company.Service.Api.Features.Orders;

public sealed record OrderResponse(
    Guid Id,
    string CustomerId,
    string Currency,
    OrderStatus Status,
    long TotalMinor,
    IReadOnlyList<OrderLineResponse> Lines,
    DateTimeOffset PlacedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason)
{
    internal static OrderResponse From(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Currency,
        order.Status,
        order.TotalMinor,
        [.. order.Lines.Select(line => new OrderLineResponse(line.Sku, line.Quantity, line.UnitPriceMinor, line.TotalMinor))],
        order.PlacedAt,
        order.PaidAt,
        order.CancelledAt,
        order.CancellationReason);
}

public sealed record OrderLineResponse(string Sku, int Quantity, long UnitPriceMinor, long TotalMinor);
