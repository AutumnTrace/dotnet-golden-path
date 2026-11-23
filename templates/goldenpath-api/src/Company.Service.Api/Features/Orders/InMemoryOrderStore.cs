using System.Collections.Concurrent;
using Company.Service.Api.Features.Orders.Domain;

namespace Company.Service.Api.Features.Orders;

/// <summary>
/// Process-local store for local runs and fast tests. Data is gone when the process stops. Like a database,
/// it hands out copies: changes become visible only through <see cref="UpdateAsync"/>, all at once.
/// </summary>
internal sealed class InMemoryOrderStore : IOrderStore
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (!_orders.TryAdd(order.Id, order.Snapshot()))
        {
            throw new InvalidOperationException($"Order {order.Id} already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_orders.GetValueOrDefault(id)?.Snapshot());

    public Task<IReadOnlyList<Order>> ListAsync(OrderQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Order> page = [.. _orders.Values
            .Where(order => query.CustomerId is null || order.CustomerId == query.CustomerId)
            .Where(order => query.Status is null || order.Status == query.Status)
            .Where(order => query.After is null || order.Id.CompareTo(query.After.Value) < 0)
            .OrderByDescending(order => order.Id)
            .Take(query.Limit)
            .Select(order => order.Snapshot())];

        return Task.FromResult(page);
    }

    public Task UpdateAsync(Order order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        _orders[order.Id] = order.Snapshot();
        return Task.CompletedTask;
    }
}
