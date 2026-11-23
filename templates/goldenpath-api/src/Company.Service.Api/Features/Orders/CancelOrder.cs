using System.ComponentModel.DataAnnotations;
using Company.Service.Api.Features.Orders.Domain;
using Company.Service.Api.Infrastructure.Errors;
#if (UseKafka)
using Company.Service.Api.Infrastructure.Messaging;
#endif
using Microsoft.AspNetCore.Http.HttpResults;

namespace Company.Service.Api.Features.Orders;

public sealed record CancelOrderRequest([Required, StringLength(200, MinimumLength = 3)] string Reason);

internal sealed class CancelOrderHandler(
    IOrderStore store,
#if (UseKafka)
    IMessagePublisher publisher,
#endif
    TimeProvider clock,
    OrdersMetrics metrics,
    ILogger<CancelOrderHandler> logger)
{
    public async Task<Result<Order>> HandleAsync(Guid id, CancelOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var order = await store.FindAsync(id, cancellationToken);
        if (order is null)
        {
            return OrderErrors.NotFound(id);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        var cancelled = order.Cancel(request.Reason, clock.GetUtcNow());
        if (!cancelled.IsSuccess)
        {
            return cancelled.Error;
        }

        await store.UpdateAsync(order, cancellationToken);
#if (UseKafka)
        await publisher.PublishAsync(
            OrderTopics.OrderEvents,
            order.Id.ToString(),
            new OrderCancelled(order.Id, request.Reason, order.CancelledAt!.Value),
            order.CancelledAt.Value,
            cancellationToken);
#endif
        metrics.Cancelled();
        OrdersLog.OrderCancelled(logger, order.Id);
        return order;
    }
}

internal static class CancelOrderEndpoint
{
    public static RouteHandlerBuilder MapCancelOrder(this IEndpointRouteBuilder orders) =>
        orders.MapPost("/{id:guid}/cancel", HandleAsync)
            .WithName("CancelOrder")
            .WithSummary("Cancel a placed order")
            .WithDescription("Idempotent: cancelling an already cancelled order returns it unchanged.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        CancelOrderRequest request,
        CancelOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, request, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(OrderResponse.From(result.Value))
            : result.Error.ToProblem();
    }
}
