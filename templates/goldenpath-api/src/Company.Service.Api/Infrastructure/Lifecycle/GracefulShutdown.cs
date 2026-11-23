using System.ComponentModel.DataAnnotations;
using Company.Service.Api.Infrastructure.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Company.Service.Api.Infrastructure.Lifecycle;

public sealed class ShutdownOptions : IValidatableObject
{
    public const string SectionName = "Shutdown";

    /// <summary>
    /// How long to keep serving after SIGTERM while readiness reports Unhealthy, so load balancers and
    /// Kubernetes endpoints stop sending traffic before the server stops accepting it.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:01:00")]
    public TimeSpan DrainDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Total budget for shutdown (drain plus finishing in-flight work). Keep it below the pod's grace period.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(25);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Timeout <= DrainDelay)
        {
            yield return new ValidationResult(
                "Shutdown:Timeout must be longer than Shutdown:DrainDelay, or in-flight requests get no time to finish.",
                [nameof(Timeout), nameof(DrainDelay)]);
        }
    }
}

public static class LifecycleExtensions
{
    public static IServiceCollection AddGracefulShutdown(this IServiceCollection services)
    {
        services.AddOptions<ShutdownOptions>()
            .BindConfiguration(ShutdownOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<HostOptions>()
            .Configure<IOptions<ShutdownOptions>>((host, shutdown) => host.ShutdownTimeout = shutdown.Value.Timeout);

        services.AddHostedService<GracefulShutdown>();
        services.AddHealthChecks().AddCheck<DrainingHealthCheck>("draining", tags: [HealthExtensions.ReadyTag]);
        return services;
    }
}

/// <summary>Holds the process open for <see cref="ShutdownOptions.DrainDelay"/> once shutdown starts.</summary>
internal sealed partial class GracefulShutdown(
    IOptions<ShutdownOptions> options,
    TimeProvider clock,
    ILogger<GracefulShutdown> logger) : IHostedLifecycleService
{
    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        var delay = options.Value.DrainDelay;
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        LogDraining(logger, delay.TotalSeconds);
        try
        {
            await Task.Delay(delay, clock, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The host's shutdown timeout won; stop draining and let the server shut down.
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Shutdown requested: readiness is now failing, draining traffic for {DrainSeconds} s")]
    private static partial void LogDraining(ILogger logger, double drainSeconds);
}

/// <summary>Readiness fails as soon as shutdown starts, while liveness keeps passing.</summary>
internal sealed class DrainingHealthCheck(IHostApplicationLifetime lifetime) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(lifetime.ApplicationStopping.IsCancellationRequested
            ? HealthCheckResult.Unhealthy("Shutting down")
            : HealthCheckResult.Healthy());
}
