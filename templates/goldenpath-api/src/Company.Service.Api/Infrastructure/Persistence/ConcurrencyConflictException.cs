namespace Company.Service.Api.Infrastructure.Persistence;

/// <summary>
/// Someone else changed the row between our read and our write (optimistic concurrency).
/// The API maps it to 409 so the client re-reads and retries.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
