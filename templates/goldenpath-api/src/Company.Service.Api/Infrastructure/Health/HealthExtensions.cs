using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Company.Service.Api.Infrastructure.Health;

public static class HealthExtensions
{
    /// <summary>Tag for checks that gate traffic. Only this service's own dependencies belong here, never other services.</summary>
    public const string ReadyTag = "ready";

    public static IHealthChecksBuilder AddHealthDefaults(this IServiceCollection services) =>
        services.AddHealthChecks();

    /// <summary>
    /// <c>/health/live</c> runs no checks: if the process can answer, it is alive and must not be restarted.
    /// <c>/health/ready</c> runs the <see cref="ReadyTag"/> checks: failing it only takes the pod out of rotation.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var probes = app.MapGroup("/health").ExcludeFromDescription().AllowAnonymous();

        probes.MapHealthChecks("/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthResponseWriter.WriteAsync,
        });

        probes.MapHealthChecks("/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = HealthResponseWriter.WriteAsync,
        });

        return app;
    }
}
