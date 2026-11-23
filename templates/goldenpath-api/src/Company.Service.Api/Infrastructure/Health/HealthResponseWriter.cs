using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Company.Service.Api.Infrastructure.Health;

internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// A small JSON body for humans and dashboards. Exception messages are left out on purpose:
    /// they can contain hosts, user names or connection strings.
    /// </summary>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var body = new HealthResponse(
            report.Status.ToString(),
            Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            [.. report.Entries.Select(entry => new HealthCheckResponse(
                entry.Key,
                entry.Value.Status.ToString(),
                Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                entry.Value.Description))]);

        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(context.Response.Body, body, JsonOptions, context.RequestAborted);
    }

    private sealed record HealthResponse(string Status, double DurationMs, IReadOnlyList<HealthCheckResponse> Checks);

    private sealed record HealthCheckResponse(string Name, string Status, double DurationMs, string? Description);
}
