using Company.Service.Api.Features.Orders.Domain;
using Company.Service.Api.Infrastructure.Errors;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Company.Service.Api.Features.Orders;

internal static class GetOrderEndpoint
{
    public static RouteHandlerBuilder MapGetOrder(this IEndpointRouteBuilder orders) =>
        orders.MapGet("/{id:guid}", HandleAsync)
            .WithName("GetOrder")
            .WithSummary("Get an order by id")
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<Results<Ok<OrderResponse>, ProblemHttpResult>> HandleAsync(
        Guid id,
        IOrderStore store,
        CancellationToken cancellationToken)
    {
        var order = await store.FindAsync(id, cancellationToken);
        return order is null
            ? OrderErrors.NotFound(id).ToProblem()
            : TypedResults.Ok(OrderResponse.From(order));
    }
}
