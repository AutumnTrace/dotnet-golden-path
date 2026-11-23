using System.Net.Http.Json;
using System.Text.Json;
using Company.Service.Api.Features.Orders;
using Company.Service.Api.Features.Orders.Domain;
using Company.Service.Api.Features.Orders.Persistence;
using Company.Service.Api.Infrastructure.Persistence;
using Company.Service.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Company.Service.IntegrationTests.Postgres;

[Trait("Category", "Integration")]
public class PostgresTests(IntegrationFixture infrastructure)
{
    private readonly HttpClient _client = infrastructure.Service.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Startup_applies_every_migration()
    {
        await using var scope = infrastructure.Service.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;

        Assert.Empty(await database.GetPendingMigrationsAsync(Ct));
        Assert.Contains(await database.GetAppliedMigrationsAsync(Ct), migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_order_round_trips_through_postgres_including_its_lines()
    {
        var placed = await _client.PlaceOrderAsync(ApiClient.NewOrder(), Ct);

        await using var scope = infrastructure.Service.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Set<Order>().AsNoTracking().SingleAsync(order => order.Id == placed.Id, Ct);

        Assert.Equal(placed.TotalMinor, stored.TotalMinor);
        Assert.Equal(["SKU-1", "SKU-2"], stored.Lines.Select(line => line.Sku));
        Assert.Equal(placed.PlacedAt, stored.PlacedAt);
    }

    [Fact]
    public async Task Keyset_paging_walks_every_order_once_newest_first()
    {
        var customer = $"pager-{Guid.NewGuid():N}";
        var placed = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            placed.Add((await _client.PlaceOrderAsync(ApiClient.NewOrder(customer), Ct)).Id);
        }

        var seen = new List<Guid>();
        Guid? cursor = null;
        do
        {
            var page = await _client.GetFromJsonAsync<OrderPage>(
                $"/v1/orders?customerId={customer}&limit=2{(cursor is null ? "" : $"&after={cursor}")}", ApiClient.Json, Ct);
            seen.AddRange(page!.Items.Select(order => order.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(Enumerable.Reverse(placed), seen);
    }

    [Fact]
    public async Task A_concurrent_update_is_detected_instead_of_silently_overwritten()
    {
        var placed = await _client.PlaceOrderAsync(ApiClient.NewOrder(), Ct);
        await using var first = infrastructure.Service.Services.CreateAsyncScope();
        await using var second = infrastructure.Service.Services.CreateAsyncScope();
        var mine = new EfOrderStore(first.ServiceProvider.GetRequiredService<AppDbContext>());
        var theirs = new EfOrderStore(second.ServiceProvider.GetRequiredService<AppDbContext>());

        var myCopy = (await mine.FindAsync(placed.Id, Ct))!;
        var theirCopy = (await theirs.FindAsync(placed.Id, Ct))!;
        theirCopy.Cancel("cancelled elsewhere", DateTimeOffset.UtcNow);
        await theirs.UpdateAsync(theirCopy, Ct);
        myCopy.RecordPayment(myCopy.TotalMinor, myCopy.Currency, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => mine.UpdateAsync(myCopy, Ct));
        Assert.Equal(OrderStatus.Cancelled, (await _client.GetOrderAsync(placed.Id, Ct)).Status);
    }

    [Fact]
    public async Task Readiness_reports_the_database()
    {
        var report = await _client.GetFromJsonAsync<JsonElement>("/health/ready", Ct);

        var database = report.GetProperty("checks").EnumerateArray().Single(check => check.GetProperty("name").GetString() == "database");
        Assert.Equal("Healthy", database.GetProperty("status").GetString());
    }
}
