using Company.Service.Api.Features.Orders;
using Company.Service.Api.Features.Orders.Domain;

namespace Company.Service.UnitTests.Orders;

public class InMemoryOrderStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 14, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_returns_newest_first_and_pages_with_the_cursor()
    {
        var store = new InMemoryOrderStore();
        var orders = new List<Order>();
        for (var i = 0; i < 5; i++)
        {
            var order = Order.Place("cust-1", "EUR", [new("SKU-1", 1, 100)], Start.AddSeconds(i)).Value;
            await store.AddAsync(order, Ct);
            orders.Add(order);
        }

        var first = await store.ListAsync(new OrderQuery(null, null, null, 2), Ct);
        var second = await store.ListAsync(new OrderQuery(null, null, first[^1].Id, 2), Ct);

        Assert.Equal([orders[4].Id, orders[3].Id], first.Select(o => o.Id));
        Assert.Equal([orders[2].Id, orders[1].Id], second.Select(o => o.Id));
    }

    [Fact]
    public async Task List_filters_by_customer_and_status()
    {
        var store = new InMemoryOrderStore();
        var mine = Order.Place("cust-1", "EUR", [new("SKU-1", 1, 100)], Start).Value;
        var cancelled = Order.Place("cust-1", "EUR", [new("SKU-1", 1, 100)], Start.AddSeconds(1)).Value;
        cancelled.Cancel("test", Start.AddSeconds(2));
        var theirs = Order.Place("cust-2", "EUR", [new("SKU-1", 1, 100)], Start.AddSeconds(3)).Value;
        foreach (var order in new[] { mine, cancelled, theirs })
        {
            await store.AddAsync(order, Ct);
        }

        var result = await store.ListAsync(new OrderQuery("cust-1", OrderStatus.Placed, null, 10), Ct);

        Assert.Equal(mine.Id, Assert.Single(result).Id);
    }

    [Fact]
    public async Task Adding_the_same_order_twice_fails_loudly()
    {
        var store = new InMemoryOrderStore();
        var order = Order.Place("cust-1", "EUR", [new("SKU-1", 1, 100)], Start).Value;
        await store.AddAsync(order, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(order, Ct));
    }
}
