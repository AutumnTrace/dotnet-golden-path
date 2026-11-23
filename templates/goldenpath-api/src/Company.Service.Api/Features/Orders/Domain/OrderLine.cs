namespace Company.Service.Api.Features.Orders.Domain;

/// <summary>One product line. Money is always integer minor units (cents) in the order's currency.</summary>
public sealed record OrderLine(string Sku, int Quantity, long UnitPriceMinor)
{
    /// <exception cref="OverflowException">The line total does not fit in 64 bits.</exception>
    public long TotalMinor => checked(Quantity * UnitPriceMinor);
}
