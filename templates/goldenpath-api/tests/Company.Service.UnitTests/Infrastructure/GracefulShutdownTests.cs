using System.ComponentModel.DataAnnotations;
using Company.Service.Api.Infrastructure.Lifecycle;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Company.Service.UnitTests.Infrastructure;

public class GracefulShutdownTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stopping_holds_the_process_for_the_drain_delay()
    {
        var clock = new FakeTimeProvider();
        var shutdown = Create(TimeSpan.FromSeconds(5), clock);

        var stopping = shutdown.StoppingAsync(Ct);
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(stopping.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    [Fact]
    public async Task A_zero_drain_delay_stops_immediately()
    {
        var shutdown = Create(TimeSpan.Zero, new FakeTimeProvider());

        await shutdown.StoppingAsync(Ct).WaitAsync(TimeSpan.FromSeconds(1), Ct);
    }

    [Fact]
    public async Task The_host_shutdown_timeout_cuts_the_drain_short()
    {
        using var hostTimeout = new CancellationTokenSource();
        var shutdown = Create(TimeSpan.FromSeconds(30), new FakeTimeProvider());

        var stopping = shutdown.StoppingAsync(hostTimeout.Token);
        await hostTimeout.CancelAsync();

        await stopping.WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    [Fact]
    public async Task Readiness_fails_once_shutdown_starts()
    {
        var lifetime = new FakeLifetime();
        var check = new DrainingHealthCheck(lifetime);

        var before = await check.CheckHealthAsync(new HealthCheckContext(), Ct);
        lifetime.StopApplication();
        var after = await check.CheckHealthAsync(new HealthCheckContext(), Ct);

        Assert.Equal(HealthStatus.Healthy, before.Status);
        Assert.Equal(HealthStatus.Unhealthy, after.Status);
    }

    [Theory]
    [InlineData("00:00:05", "00:00:25", true)]
    [InlineData("00:00:10", "00:00:10", false)]
    [InlineData("00:00:20", "00:00:10", false)]
    [InlineData("00:05:00", "00:05:00", false)]
    public void Options_require_time_left_after_draining(string drainDelay, string timeout, bool valid)
    {
        var options = new ShutdownOptions { DrainDelay = TimeSpan.Parse(drainDelay, System.Globalization.CultureInfo.InvariantCulture), Timeout = TimeSpan.Parse(timeout, System.Globalization.CultureInfo.InvariantCulture) };

        var isValid = Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);

        Assert.Equal(valid, isValid);
    }

    private static GracefulShutdown Create(TimeSpan drainDelay, TimeProvider clock) =>
        new(Options.Create(new ShutdownOptions { DrainDelay = drainDelay }), clock, NullLogger<GracefulShutdown>.Instance);

    private sealed class FakeLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();

        public void Dispose() => _stopping.Dispose();
    }
}
