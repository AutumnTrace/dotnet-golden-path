using Company.Service.Api.Features.Orders.Domain;

namespace Company.Service.Api.Features.Orders;

/// <summary>Persistence port for the Orders slice. Handlers depend on this, never on a database client.</summary>
public interface IOrderStore
{
    Task AddAsync(Order order, CancellationToken cancellationToken);

    Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Newest first, keyset-paged on the time-ordered id.</summary>
    Task<IReadOnlyList<Order>> ListAsync(OrderQuery query, CancellationToken cancellationToken);

    Task UpdateAsync(Order order, CancellationToken cancellationToken);
}

/// <param name="After">Return orders older than this id (the previous page's cursor).</param>
public sealed record OrderQuery(string? CustomerId, OrderStatus? Status, Guid? After, int Limit);
