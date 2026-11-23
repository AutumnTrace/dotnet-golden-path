using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Company.Service.Api.Features.Orders.Domain;
using Company.Service.Api.Infrastructure.Errors;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Company.Service.Api.Features.Orders;

/// <summary>Shape checks only; business rules live in <see cref="Order.Place"/>.</summary>
public sealed record PlaceOrderRequest(
    [Required, StringLength(64), RegularExpression(OrderPatterns.Identifier)] string CustomerId,
    [Required, RegularExpression(OrderPatterns.Currency, ErrorMessage = "Currency must be an ISO 4217 code such as EUR.")] string Currency,
    [Required, MinLength(1), MaxLength(Order.MaxLines)] IReadOnlyList<PlaceOrderLineRequest> Lines);

public sealed record PlaceOrderLineRequest(
    [Required, StringLength(32), RegularExpression(OrderPatterns.Identifier)] string Sku,
    [Range(1, 10_000)] int Quantity,
    [Range(0, 1_000_000_000_000)] long UnitPriceMinor);

internal sealed class PlaceOrderHandler(
    IOrderStore store,
    TimeProvider clock,
    OrdersMetrics metrics,
    ILogger<PlaceOrderHandler> logger)
{
    public async Task<Result<Order>> HandleAsync(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lines = request.Lines.Select(line => new OrderLine(line.Sku, line.Quantity, line.UnitPriceMinor)).ToList();
        var placed = Order.Place(request.CustomerId, request.Currency, lines, clock.GetUtcNow());
        if (!placed.IsSuccess)
        {
            OrdersLog.OrderRejected(logger, placed.Error.Code);
            return placed.Error;
        }

        var order = placed.Value;
        await store.AddAsync(order, cancellationToken);

        Activity.Current?.SetTag("order.id", order.Id);
        metrics.Placed(order.Currency);
        OrdersLog.OrderPlaced(logger, order.Id, order.Lines.Count, order.TotalMinor, order.Currency);
        return order;
    }
}

internal static class PlaceOrderEndpoint
{
    public static RouteHandlerBuilder MapPlaceOrder(this IEndpointRouteBuilder orders) =>
        orders.MapPost("", HandleAsync)
            .WithName("PlaceOrder")
            .WithSummary("Place an order")
            .WithDescription("Validates the request, applies the order rules and stores the order. Amounts are integer minor units.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<Results<Created<OrderResponse>, ProblemHttpResult>> HandleAsync(
        PlaceOrderRequest request,
        PlaceOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created($"/v1/orders/{result.Value.Id}", OrderResponse.From(result.Value))
            : result.Error.ToProblem();
    }
}
