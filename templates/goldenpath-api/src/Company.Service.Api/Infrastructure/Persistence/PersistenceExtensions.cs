using Company.Service.Api.Infrastructure.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Company.Service.Api.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Configure(options => options.ConnectionString ??= configuration.GetConnectionString(DatabaseOptions.ConnectionStringName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (DatabaseOptions.ProviderFrom(configuration) != DatabaseProvider.Postgres)
        {
            return services;
        }

        services.AddDbContextPool<AppDbContext>((provider, options) => Configure(
            options,
            provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString!));

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: [HealthExtensions.ReadyTag]);
        services.AddHostedService<DatabaseMigrator>();
        return services;
    }

    /// <summary>Shared by the running service and the design-time factory, so migrations see the same model.</summary>
    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3))
            .UseSnakeCaseNamingConvention();
}
