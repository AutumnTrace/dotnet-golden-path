using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;

namespace Company.Service.Api.Infrastructure.Persistence;

/// <summary>
/// Applies migrations in <see cref="IHostedLifecycleService.StartingAsync"/>, before Kestrel accepts traffic,
/// when <see cref="DatabaseOptions.MigrateOnStartup"/> is on. EF Core takes a database lock, so replicas that
/// start together do not race.
/// </summary>
internal sealed partial class DatabaseMigrator(
    IServiceScopeFactory scopes,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseMigrator> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MigrateOnStartup)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;

        var pending = (await database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        LogMigrating(logger, pending.Count);
        await database.MigrateAsync(cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {PendingCount} pending database migration(s)")]
    private static partial void LogMigrating(ILogger logger, int pendingCount);
}

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without starting the service or reaching a database.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        PersistenceExtensions.Configure(options, "Host=localhost;Database=design_time");
        return new AppDbContext(options.Options);
    }
}
