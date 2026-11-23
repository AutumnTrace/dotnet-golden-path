using System.Diagnostics.Metrics;
using Company.Service.Api.Infrastructure.Observability;

namespace Company.Service.Api.Features.Orders;

/// <summary>Business metrics for the slice. Created through <see cref="IMeterFactory"/> so tests can observe them.</summary>
internal sealed class OrdersMetrics
{
    private readonly Counter<long> _placed;
    private readonly Counter<long> _cancelled;

    public OrdersMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(Telemetry.MeterName);
        _placed = meter.CreateCounter<long>("orders.placed", unit: "{order}", description: "Orders accepted.");
        _cancelled = meter.CreateCounter<long>("orders.cancelled", unit: "{order}", description: "Orders cancelled.");
    }

    public void Placed(string currency) => _placed.Add(1, new KeyValuePair<string, object?>("currency", currency));

    public void Cancelled() => _cancelled.Add(1);
}

/// <summary>Source-generated, allocation-free log messages with stable event ids.</summary>
internal static partial class OrdersLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Order {OrderId} placed with {LineCount} lines, total {TotalMinor} {Currency}")]
    public static partial void OrderPlaced(ILogger logger, Guid orderId, int lineCount, long totalMinor, string currency);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Order {OrderId} cancelled")]
    public static partial void OrderCancelled(ILogger logger, Guid orderId);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Order rejected: {ErrorCode}")]
    public static partial void OrderRejected(ILogger logger, string errorCode);
}
