using Company.Service.Api.Features.Orders.Domain;

namespace Company.Service.UnitTests.Orders;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Place_sums_line_totals_in_minor_units()
    {
        var result = Order.Place("cust-1", "EUR", [new("SKU-1", 3, 1_250), new("SKU-2", 1, 499)], Now);

        Assert.True(result.IsSuccess);
        var order = result.Value;
        Assert.Equal(4_249, order.TotalMinor);
        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(Now, order.PlacedAt);
        Assert.Equal(2, order.Lines.Count);
    }

    [Fact]
    public void Place_assigns_time_ordered_ids()
    {
        var earlier = Order.Place("cust-1", "EUR", [new("SKU-1", 1, 100)], Now).Value;
        var later = Order.Place("cust-1", "EUR", [new("SKU-1", 1, 100)], Now.AddMilliseconds(5)).Value;

        Assert.Equal(7, earlier.Id.Version);
        Assert.True(later.Id.CompareTo(earlier.Id) > 0);
    }

    [Fact]
    public void Timestamps_are_kept_to_the_microsecond_like_the_database()
    {
        var precise = new DateTimeOffset(2026, 3, 14, 9, 30, 0, TimeSpan.Zero).AddTicks(1_234_567);

        var order = Order.Place("cust-1", "EUR", [new("SKU-1", 1, 100)], precise).Value;
        order.Cancel("test", precise.AddTicks(9));

        Assert.Equal(precise.AddTicks(-7), order.PlacedAt);
        Assert.Equal(precise.AddTicks(9 - 6), order.CancelledAt);
    }

    [Fact]
    public void Place_rejects_an_order_without_lines()
    {
        var result = Order.Place("cust-1", "EUR", [], Now);

        Assert.Equal("orders.no_lines", result.Error?.Code);
    }

    [Fact]
    public void Place_rejects_more_lines_than_the_limit()
    {
        var lines = Enumerable.Range(0, Order.MaxLines + 1).Select(i => new OrderLine($"SKU-{i}", 1, 100)).ToList();

        var result = Order.Place("cust-1", "EUR", lines, Now);

        Assert.Equal("orders.too_many_lines", result.Error?.Code);
    }

    [Fact]
    public void Place_rejects_the_same_sku_twice_ignoring_case()
    {
        var result = Order.Place("cust-1", "EUR", [new("sku-1", 1, 100), new("SKU-1", 2, 100)], Now);

        Assert.Equal("orders.duplicate_sku", result.Error?.Code);
    }

    [Theory]
    [InlineData("eur")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    [InlineData("")]
    public void Place_rejects_anything_but_an_iso_currency_code(string currency)
    {
        var result = Order.Place("cust-1", currency, [new("SKU-1", 1, 100)], Now);

        Assert.Equal("orders.invalid_currency", result.Error?.Code);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(1, -1)]
    public void Place_rejects_non_positive_quantities_and_negative_prices(int quantity, long unitPriceMinor)
    {
        var result = Order.Place("cust-1", "EUR", [new("SKU-1", quantity, unitPriceMinor)], Now);

        Assert.Equal("orders.invalid_line", result.Error?.Code);
    }

    [Fact]
    public void Place_rejects_a_total_that_would_overflow_instead_of_wrapping_around()
    {
        var half = (long.MaxValue / 2) + 1;

        var result = Order.Place("cust-1", "EUR", [new("SKU-1", 1, half), new("SKU-2", 1, half)], Now);

        Assert.Equal("orders.total_too_large", result.Error?.Code);
    }

    [Fact]
    public void Cancel_records_when_and_why()
    {
        var order = Order.Place("cust-1", "EUR", [new("SKU-1", 1, 100)], Now).Value;

        var result = order.Cancel("customer changed their mind", Now.AddMinutes(2));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(Now.AddMinutes(2), order.CancelledAt);
        Assert.Equal("customer changed their mind", order.CancellationReason);
    }

    [Fact]
    public void Cancel_twice_is_a_no_op_that_keeps_the_first_reason()
    {
        var order = Order.Place("cust-1", "EUR", [new("SKU-1", 1, 100)], Now).Value;
        order.Cancel("first", Now.AddMinutes(1));

        var again = order.Cancel("second", Now.AddMinutes(9));

        Assert.True(again.IsSuccess);
        Assert.Equal("first", order.CancellationReason);
        Assert.Equal(Now.AddMinutes(1), order.CancelledAt);
    }
}
