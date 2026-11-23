namespace Company.Service.Api.Features.Orders;

internal static class OrderPatterns
{
    /// <summary>Opaque identifiers: letters, digits, dot, dash and underscore; starts with a letter or digit.</summary>
    public const string Identifier = "^[A-Za-z0-9][A-Za-z0-9._-]*$";

    public const string Currency = "^[A-Z]{3}$";
}
