using Company.Service.Api.Infrastructure.Errors;

namespace Company.Service.Api.Features.Orders.Domain;

/// <summary>
/// The Orders aggregate. State only changes through its methods, which enforce the invariants
/// (at least one line, unique SKUs, no overflowing totals, legal status transitions).
/// </summary>
public sealed class Order
{
    public const int MaxLines = 50;

    private readonly List<OrderLine> _lines = [];

    private Order(Guid id, string customerId, string currency, DateTimeOffset placedAt)
    {
        Id = id;
        CustomerId = customerId;
        Currency = currency;
        PlacedAt = placedAt;
        Status = OrderStatus.Placed;
    }

    public Guid Id { get; private set; }

    public string CustomerId { get; private set; }

    /// <summary>ISO 4217 code; all amounts on the order are minor units of this currency.</summary>
    public string Currency { get; private set; }

    public OrderStatus Status { get; private set; }

    public long TotalMinor { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public static Result<Order> Place(string customerId, string currency, IReadOnlyCollection<OrderLine> lines, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(lines);

        if (!IsIsoCurrencyCode(currency))
        {
            return OrderErrors.InvalidCurrency(currency);
        }

        if (lines.Count == 0)
        {
            return OrderErrors.NoLines;
        }

        if (lines.Count > MaxLines)
        {
            return OrderErrors.TooManyLines;
        }

        var duplicate = lines
            .GroupBy(line => line.Sku, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return OrderErrors.DuplicateSku(duplicate.Key);
        }

        long total = 0;
        foreach (var line in lines)
        {
            if (line.Quantity <= 0 || line.UnitPriceMinor < 0)
            {
                return OrderErrors.InvalidLine(line.Sku);
            }

            try
            {
                total = checked(total + line.TotalMinor);
            }
            catch (OverflowException)
            {
                return OrderErrors.TotalTooLarge;
            }
        }

        // UUIDv7: unique like any GUID, but time-ordered, so keyset paging and B-tree indexes stay cheap.
        var order = new Order(Guid.CreateVersion7(now), customerId, currency, now) { TotalMinor = total };
        order._lines.AddRange(lines);
        return order;
    }

    /// <summary>Cancels a placed order. Cancelling twice is a no-op, so clients can retry safely.</summary>
    public Result<Order> Cancel(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        switch (Status)
        {
            case OrderStatus.Cancelled:
                return this;
            case OrderStatus.Placed:
                Status = OrderStatus.Cancelled;
                CancelledAt = now;
                CancellationReason = reason;
                return this;
            default:
                return OrderErrors.InvalidState(Id, Status, "cancelled");
        }
    }

    private static bool IsIsoCurrencyCode(string currency) =>
        currency.Length == 3 && currency.All(char.IsAsciiLetterUpper);
}
