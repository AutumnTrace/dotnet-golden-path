using System.Diagnostics;
#if (UsePostgres)
using Company.Service.Api.Infrastructure.Persistence;
#endif
using Microsoft.AspNetCore.Http.HttpResults;

namespace Company.Service.Api.Infrastructure.Errors;

public static class ProblemDetailsExtensions
{
    /// <summary>Extension member carrying the stable, machine-readable error code.</summary>
    public const string ErrorCodeKey = "errorCode";

    public const string TraceIdKey = "traceId";

    /// <summary>
    /// RFC 9457 problem details for every error response: unhandled exceptions, status code pages,
    /// validation failures and <see cref="Error"/> results all share one shape.
    /// </summary>
    public static IServiceCollection AddProblemDetailsDefaults(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var http = context.HttpContext;

            problem.Instance ??= $"{http.Request.Method} {http.Request.Path}";
            // The 32-hex trace id, which is what tracing backends search by (not the full traceparent).
            problem.Extensions[TraceIdKey] = Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier;

            if (context.Exception is BadHttpRequestException badRequest)
            {
                // The client sent something unreadable (malformed JSON, a value that does not bind): say what.
                problem.Detail = badRequest.Message;
                problem.Extensions.TryAdd(ErrorCodeKey, "request.malformed");
            }
#if (UsePostgres)
            else if (context.Exception is ConcurrencyConflictException)
            {
                problem.Title = "Conflict with a concurrent update";
                problem.Detail = "The resource changed while this request was processed. Read it again and retry.";
                problem.Extensions.TryAdd(ErrorCodeKey, "concurrency_conflict");
            }
#endif
            else if (problem.Status == StatusCodes.Status500InternalServerError)
            {
                // Never leak internals: exception messages and stack traces stay in logs and traces.
                problem.Detail = null;
                problem.Extensions.TryAdd(ErrorCodeKey, "internal_error");
            }
        });

        services.AddExceptionHandler(options =>
        {
            // Binding failures are the client's fault in every environment, not a 500.
            options.StatusCodeSelector = exception => exception switch
            {
                BadHttpRequestException badRequest => badRequest.StatusCode,
#if (UsePostgres)
                ConcurrencyConflictException => StatusCodes.Status409Conflict,
#endif
                _ => StatusCodes.Status500InternalServerError,
            };

            // Expected client-side conditions are not server errors: keep them out of the error logs.
#if (UsePostgres)
            options.SuppressDiagnosticsCallback = context => context.Exception is BadHttpRequestException or ConcurrencyConflictException;
#else
            options.SuppressDiagnosticsCallback = context => context.Exception is BadHttpRequestException;
#endif
        });

        return services;
    }

    public static ProblemHttpResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var (status, title) = error.Kind switch
        {
            ErrorKind.NotFound => (StatusCodes.Status404NotFound, "Resource not found"),
            ErrorKind.Conflict => (StatusCodes.Status409Conflict, "Conflict with the current state"),
            ErrorKind.Unprocessable => (StatusCodes.Status422UnprocessableEntity, "Request cannot be processed"),
            ErrorKind.Unavailable => (StatusCodes.Status503ServiceUnavailable, "Dependency unavailable"),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Kind, "Unknown error kind."),
        };

        return TypedResults.Problem(
            statusCode: status,
            title: title,
            detail: error.Message,
            extensions: new Dictionary<string, object?> { [ErrorCodeKey] = error.Code });
    }
}
