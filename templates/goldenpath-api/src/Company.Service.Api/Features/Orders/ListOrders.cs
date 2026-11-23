using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using Company.Service.Api.Features.Orders.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Company.Service.Api.Features.Orders;

/// <param name="Items">The page, newest first.</param>
/// <param name="NextCursor">Pass as <c>after</c> to get the next page; null on the last page.</param>
public sealed record OrderPage(IReadOnlyList<OrderResponse> Items, Guid? NextCursor);

internal static class ListOrdersEndpoint
{
    private const int DefaultLimit = 20;

    public static RouteHandlerBuilder MapListOrders(this IEndpointRouteBuilder orders) =>
        orders.MapGet("", HandleAsync)
            .WithName("ListOrders")
            .WithSummary("List orders, newest first")
            .WithDescription("Keyset pagination: pass the previous page's nextCursor as `after`.")
            .ProducesValidationProblem();

    private static async Task<Ok<OrderPage>> HandleAsync(
        IOrderStore store,
        [Description("Only orders for this customer.")][StringLength(64)] string? customerId,
        [Description("Only orders in this status.")] OrderStatus? status,
        [Description("Cursor from the previous page.")] Guid? after,
        [Description("Page size, 1 to 100 (default 20).")][Range(1, 100)] int? limit,
        CancellationToken cancellationToken)
    {
        var pageSize = limit ?? DefaultLimit;

        // Ask for one extra row to learn whether another page exists without a COUNT query.
        var rows = await store.ListAsync(new OrderQuery(customerId, status, after, pageSize + 1), cancellationToken);

        var items = rows.Take(pageSize).Select(OrderResponse.From).ToList();
        var nextCursor = rows.Count > pageSize ? items[^1].Id : (Guid?)null;
        return TypedResults.Ok(new OrderPage(items, nextCursor));
    }
}
