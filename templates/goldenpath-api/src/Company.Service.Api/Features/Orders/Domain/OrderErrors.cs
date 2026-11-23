using System.Globalization;
using Company.Service.Api.Infrastructure.Errors;

namespace Company.Service.Api.Features.Orders.Domain;

/// <summary>Every expected failure of the Orders slice, with its stable error code.</summary>
public static class OrderErrors
{
    public static readonly Error NoLines =
        Error.Unprocessable("orders.no_lines", "An order needs at least one line.");

    public static readonly Error TooManyLines =
        Error.Unprocessable("orders.too_many_lines", $"An order can have at most {Order.MaxLines} lines.");

    public static readonly Error TotalTooLarge =
        Error.Unprocessable("orders.total_too_large", "The order total is too large to process.");

    public static Error NotFound(Guid id) =>
        Error.NotFound("orders.not_found", $"Order {id} does not exist.");

    public static Error InvalidCurrency(string currency) =>
        Error.Unprocessable("orders.invalid_currency", $"'{currency}' is not an ISO 4217 currency code.");

    public static Error DuplicateSku(string sku) =>
        Error.Unprocessable("orders.duplicate_sku", $"SKU '{sku}' appears on more than one line; combine the quantities instead.");

    public static Error InvalidLine(string sku) =>
        Error.Unprocessable("orders.invalid_line", $"Line '{sku}' needs a positive quantity and a non-negative price.");

    public static Error CustomerNotFound(string customerId) =>
        Error.Unprocessable("orders.customer_not_found", $"Customer '{customerId}' does not exist.");

    public static Error CustomerBlocked(string customerId) =>
        Error.Unprocessable("orders.customer_blocked", $"Customer '{customerId}' is blocked from placing orders.");

    public static readonly Error CustomerDirectoryUnavailable =
        Error.Unavailable("orders.customer_directory_unavailable", "Could not verify the customer right now. Retry shortly.");

    public static Error InvalidState(Guid id, OrderStatus status, string action) =>
        Error.Conflict(
            "orders.invalid_state",
            string.Create(CultureInfo.InvariantCulture, $"Order {id} is {status} and cannot be {action}."));
}
