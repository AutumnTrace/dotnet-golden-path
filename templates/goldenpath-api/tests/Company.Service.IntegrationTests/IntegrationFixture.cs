using Company.Service.IntegrationTests;
using Company.Service.Testing;
using Microsoft.AspNetCore.Hosting;
#if (UseKafka)
using Testcontainers.Kafka;
#endif
#if (UsePostgres)
using Testcontainers.PostgreSql;
#endif

[assembly: AssemblyFixture(typeof(IntegrationFixture))]

namespace Company.Service.IntegrationTests;

/// <summary>
/// Real infrastructure in throwaway containers, started once for the whole test run, and one instance of
/// the service wired to it. Image versions match docker-compose.yml.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
{
#if (UsePostgres)
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:18-alpine").Build();

#endif
#if (UseKafka)
    public KafkaContainer Kafka { get; } = new KafkaBuilder("apache/kafka:4.4.0").Build();

#endif
    public IntegrationFactory Service { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(
#if (UsePostgres && UseKafka)
            Postgres.StartAsync(),
            Kafka.StartAsync());
#elif (UsePostgres)
            Postgres.StartAsync());
#else
            Kafka.StartAsync());
#endif

        Service = new IntegrationFactory(this);
        Service.StartServer();
    }

    public async ValueTask DisposeAsync()
    {
        if (Service is not null)
        {
            await Service.DisposeAsync();
        }
#if (UseKafka)

        await Kafka.DisposeAsync();
#endif
#if (UsePostgres)

        await Postgres.DisposeAsync();
#endif
    }
}

/// <summary>The real service, pointed at the containers instead of the in-memory providers.</summary>
public sealed class IntegrationFactory(IntegrationFixture infrastructure) : ServiceFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
#if (UsePostgres)
        builder.UseSetting("Database:Provider", "Postgres");
        builder.UseSetting("ConnectionStrings:Database", infrastructure.Postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
#endif
#if (UseKafka)
        builder.UseSetting("Messaging:Provider", "Kafka");
        builder.UseSetting("Messaging:BootstrapServers", infrastructure.Kafka.GetBootstrapAddress());
        builder.UseSetting("Messaging:ConsumerGroup", $"integration-{Guid.NewGuid():N}");
        builder.UseSetting("Messaging:CreateTopicsOnStartup", "true");
#endif
    }
}
