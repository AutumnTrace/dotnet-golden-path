using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Company.Service.Api.Infrastructure.Messaging;

/// <summary>Readiness: can we reach at least one broker? One admin client for the process, not one per probe.</summary>
internal sealed class KafkaHealthCheck(IOptions<MessagingOptions> options) : IHealthCheck, IDisposable
{
    private readonly Lazy<IAdminClient> _admin = new(() =>
        new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.Value.BootstrapServers }).Build());

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var cluster = await _admin.Value.DescribeClusterAsync(new DescribeClusterOptions { RequestTimeout = TimeSpan.FromSeconds(2) });
            return cluster.Nodes.Count > 0
                ? HealthCheckResult.Healthy($"{cluster.Nodes.Count} broker(s)")
                : HealthCheckResult.Unhealthy("No brokers in the cluster metadata");
        }
        catch (KafkaException exception)
        {
            return HealthCheckResult.Unhealthy("Kafka is unreachable", exception);
        }
    }

    public void Dispose()
    {
        if (_admin.IsValueCreated)
        {
            _admin.Value.Dispose();
        }
    }
}

/// <summary>Creates this service's topics (and their dead-letter topics) when <c>CreateTopicsOnStartup</c> is on.</summary>
internal sealed partial class KafkaTopicInitializer(
    IOptions<MessagingOptions> options,
    IOptions<MessagingTopology> topology,
    ILogger<KafkaTopicInitializer> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.CreateTopicsOnStartup)
        {
            return;
        }

        var topics = topology.Value.ProducedTopics
            .Concat(topology.Value.ConsumedTopics)
            .Concat(topology.Value.ConsumedTopics.Select(MessagingTopology.DeadLetterTopic))
            .Distinct(StringComparer.Ordinal)
            .Select(name => new TopicSpecification { Name = name, NumPartitions = 3, ReplicationFactor = 1 })
            .ToList();
        var names = topics.Select(topic => topic.Name).ToArray();

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.Value.BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync(topics);
        }
        catch (CreateTopicsException exception) when (exception.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
            // Already there: nothing to do.
        }

        LogTopicsReady(logger, names);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Kafka topics ready: {Topics}")]
    private static partial void LogTopicsReady(ILogger logger, IEnumerable<string> topics);
}
