using Company.Service.Api.Features.Orders.Domain;
using Company.Service.Api.Infrastructure.Messaging;

namespace Company.Service.Api.Features.Orders;

/// <summary>
/// A use case triggered by a message instead of an HTTP request: same slice, same store, same rules.
/// Idempotent by construction: a redelivered capture finds the order already paid and changes nothing.
/// </summary>
internal sealed class PaymentCapturedHandler(
    IOrderStore store,
    OrdersMetrics metrics,
    ILogger<PaymentCapturedHandler> logger) : IMessageHandler<PaymentCaptured>
{
    public async Task HandleAsync(PaymentCaptured message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var order = await store.FindAsync(message.OrderId, cancellationToken);
        if (order is null)
        {
            OrdersLog.PaymentForUnknownOrder(logger, message.PaymentId, message.OrderId);
            return;
        }

        if (order.Status == OrderStatus.Paid)
        {
            return;
        }

        var result = order.RecordPayment(message.AmountMinor, message.Currency, message.CapturedAt);
        if (!result.IsSuccess)
        {
            // A business problem, not a transient fault: retrying will not help, so do not throw.
            OrdersLog.PaymentNotApplied(logger, message.PaymentId, order.Id, result.Error.Code);
            return;
        }

        await store.UpdateAsync(order, cancellationToken);
        metrics.Paid();
        OrdersLog.OrderPaid(logger, order.Id, message.PaymentId);
    }
}
