namespace Company.Service.Api.Infrastructure.Errors;

/// <summary>How an expected failure should be classified. Each kind maps to exactly one HTTP status.</summary>
public enum ErrorKind
{
    NotFound,
    Conflict,
    Unprocessable,
    Unavailable,
}

/// <summary>
/// An expected, named failure. <see cref="Code"/> is the stable contract clients switch on
/// (for example <c>orders.not_found</c>); <see cref="Message"/> is for humans and may change.
/// </summary>
public sealed record Error(string Code, string Message, ErrorKind Kind)
{
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Unprocessable(string code, string message) => new(code, message, ErrorKind.Unprocessable);

    public static Error Unavailable(string code, string message) => new(code, message, ErrorKind.Unavailable);
}
