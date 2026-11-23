using System.Collections.Concurrent;
using Company.Service.Api.Features.Orders.Domain;

namespace Company.Service.Api.Features.Orders;

/// <summary>Process-local store for local runs and fast tests. Data is gone when the process stops.</summary>
internal sealed class InMemoryOrderStore : IOrderStore
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (!_orders.TryAdd(order.Id, order))
        {
            throw new InvalidOperationException($"Order {order.Id} already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_orders.GetValueOrDefault(id));

    public Task<IReadOnlyList<Order>> ListAsync(OrderQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<Order> page = [.. _orders.Values
            .Where(order => query.CustomerId is null || order.CustomerId == query.CustomerId)
            .Where(order => query.Status is null || order.Status == query.Status)
            .Where(order => query.After is null || order.Id.CompareTo(query.After.Value) < 0)
            .OrderByDescending(order => order.Id)
            .Take(query.Limit)];

        return Task.FromResult(page);
    }

    public Task UpdateAsync(Order order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }
}
