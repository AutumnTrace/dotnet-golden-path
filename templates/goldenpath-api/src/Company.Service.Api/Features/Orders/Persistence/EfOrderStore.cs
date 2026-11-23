using Company.Service.Api.Features.Orders.Domain;
using Company.Service.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Company.Service.Api.Features.Orders.Persistence;

internal sealed class EfOrderStore(AppDbContext db) : IOrderStore
{
    private IQueryable<Order> Orders => db.Set<Order>();

    public async Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        db.Add(order);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Orders.FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Order>> ListAsync(OrderQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var orders = Orders.AsNoTracking();
        if (query.CustomerId is not null)
        {
            orders = orders.Where(order => order.CustomerId == query.CustomerId);
        }

        if (query.Status is { } status)
        {
            orders = orders.Where(order => order.Status == status);
        }

        if (query.After is { } after)
        {
            // Keyset paging: WHERE id < @after ORDER BY id DESC LIMIT n. Stable and index-friendly, unlike OFFSET.
            orders = orders.Where(order => order.Id.CompareTo(after) < 0);
        }

        return await orders.OrderByDescending(order => order.Id).Take(query.Limit).ToListAsync(cancellationToken);
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken)
    {
        if (db.Entry(order).State == EntityState.Detached)
        {
            db.Update(order);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException($"Order {order.Id} was changed by another request.", exception);
        }
    }
}
