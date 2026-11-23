using System.Diagnostics.CodeAnalysis;

namespace Company.Service.Api.Infrastructure.Errors;

/// <summary>The outcome of an operation that can fail in an expected way. Exceptions stay for the unexpected.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
    }

    private Result(Error error) => Error = error;

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    public Error? Error { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Result has no value; it failed with '{Error.Code}'.");

#pragma warning disable CA1000 // Factory methods on the generic type read naturally at call sites.
    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));
#pragma warning restore CA1000

#pragma warning disable CA2225 // Implicit conversions are the point: `return order;` or `return OrderErrors.NotFound(id);`.
    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
#pragma warning restore CA2225
}
