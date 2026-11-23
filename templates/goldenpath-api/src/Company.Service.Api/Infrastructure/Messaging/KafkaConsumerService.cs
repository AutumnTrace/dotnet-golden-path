using Company.Service.Api.Infrastructure.Observability;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Company.Service.Api.Infrastructure.Messaging;

/// <summary>
/// At-least-once consumer. An offset is stored only after its message was handled (or dead-lettered), and stored
/// offsets are committed in the background and on shutdown. A crash therefore re-delivers, never loses, messages.
/// </summary>
internal sealed partial class KafkaConsumerService(
    IOptions<MessagingOptions> options,
    IOptions<MessagingTopology> topology,
    MessageDispatcher dispatcher,
    KafkaMessagePublisher deadLetters,
    ILogger<KafkaConsumerService> logger) : BackgroundService
{
    // Confluent's Consume() blocks, so the loop gets its own thread instead of starving the request pool.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(() => ConsumeAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var topics = topology.Value.ConsumedTopics;
        if (topics.Count == 0)
        {
            return;
        }

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            GroupId = options.Value.ConsumerGroup,
            ClientId = Telemetry.DefaultServiceName,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            EnableAutoOffsetStore = false,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky,
        })
            .SetErrorHandler((_, error) => LogKafkaError(logger, error.Code, error.Reason))
            .Build();

        consumer.Subscribe(topics);
        LogSubscribed(logger, topics, options.Value.ConsumerGroup);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException exception) when (!exception.Error.IsFatal)
                {
                    LogKafkaError(logger, exception.Error.Code, exception.Error.Reason);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                if (result?.Message is null)
                {
                    continue;
                }

                var envelope = KafkaHeaders.ToEnvelope(result);
                var (outcome, error) = await dispatcher.DispatchAsync(envelope, stoppingToken);
                if (outcome == DispatchOutcome.Failed)
                {
                    await deadLetters.PublishDeadLetterAsync(result, error, stoppingToken);
                    LogDeadLettered(logger, envelope.Type, envelope.MessageId, MessagingTopology.DeadLetterTopic(result.Topic));
                }

                consumer.StoreOffset(result);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            // Commits stored offsets and leaves the group, so partitions rebalance immediately.
            consumer.Close();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming {Topics} as group {ConsumerGroup}")]
    private static partial void LogSubscribed(ILogger logger, IEnumerable<string> topics, string consumerGroup);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dead-lettered {MessageType} {MessageId} to {DeadLetterTopic}")]
    private static partial void LogDeadLettered(ILogger logger, string messageType, Guid messageId, string deadLetterTopic);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kafka consumer error {Code}: {Reason}")]
    private static partial void LogKafkaError(ILogger logger, ErrorCode code, string reason);
}
