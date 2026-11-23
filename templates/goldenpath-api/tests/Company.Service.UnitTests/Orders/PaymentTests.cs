using Company.Service.Api.Features.Orders.Domain;

namespace Company.Service.UnitTests.Orders;

public class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_matching_payment_marks_the_order_paid()
    {
        var order = NewOrder();

        var result = order.RecordPayment(2_500, "EUR", Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(Now.AddMinutes(1), order.PaidAt);
    }

    [Fact]
    public void The_same_payment_delivered_twice_changes_nothing_the_second_time()
    {
        var order = NewOrder();
        order.RecordPayment(2_500, "EUR", Now.AddMinutes(1));

        var again = order.RecordPayment(2_500, "EUR", Now.AddMinutes(5));

        Assert.True(again.IsSuccess);
        Assert.Equal(Now.AddMinutes(1), order.PaidAt);
    }

    [Theory]
    [InlineData(2_499, "EUR")]
    [InlineData(2_500, "USD")]
    public void A_payment_that_does_not_match_the_total_is_refused(long amountMinor, string currency)
    {
        var order = NewOrder();

        var result = order.RecordPayment(amountMinor, currency, Now);

        Assert.Equal("orders.payment_mismatch", result.Error?.Code);
        Assert.Equal(OrderStatus.Placed, order.Status);
    }

    [Fact]
    public void A_cancelled_order_cannot_be_paid()
    {
        var order = NewOrder();
        order.Cancel("customer request", Now);

        var result = order.RecordPayment(2_500, "EUR", Now.AddMinutes(1));

        Assert.Equal("orders.invalid_state", result.Error?.Code);
    }

    [Fact]
    public void A_paid_order_cannot_be_cancelled()
    {
        var order = NewOrder();
        order.RecordPayment(2_500, "EUR", Now);

        var result = order.Cancel("too late", Now.AddMinutes(1));

        Assert.Equal("orders.invalid_state", result.Error?.Code);
        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    private static Order NewOrder() => Order.Place("cust-1", "EUR", [new("SKU-1", 2, 1_250)], Now).Value;
}
