using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Company.Service.Api.Infrastructure.Messaging;

/// <summary>
/// In-process transport with the same contract as Kafka, for <c>dotnet run</c> without Docker and for API tests.
/// Messages to topics this service consumes are dispatched to its own handlers.
/// </summary>
internal sealed class InMemoryMessageBus : IMessagePublisher
{
    private readonly Channel<MessageEnvelope> _channel = Channel.CreateUnbounded<MessageEnvelope>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentQueue<MessageEnvelope> _published = new();

    /// <summary>Everything published so far, oldest first. Useful in tests and when debugging locally.</summary>
    public IReadOnlyCollection<MessageEnvelope> Published => _published;

    public ChannelReader<MessageEnvelope> Reader => _channel.Reader;

    public Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var activity = MessagingTelemetry.StartPublish(message);
        var sent = message with { TraceParent = activity?.Id ?? message.TraceParent };
        _published.Enqueue(sent);
        return _channel.Writer.WriteAsync(sent, cancellationToken).AsTask();
    }
}

internal sealed partial class InMemoryConsumerService(
    InMemoryMessageBus bus,
    MessageDispatcher dispatcher,
    IOptions<MessagingTopology> topology,
    ILogger<InMemoryConsumerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in bus.Reader.ReadAllAsync(stoppingToken))
            {
                if (!topology.Value.ConsumedTopics.Contains(message.Topic))
                {
                    continue;
                }

                var (outcome, _) = await dispatcher.DispatchAsync(message, stoppingToken);
                if (outcome == DispatchOutcome.Failed)
                {
                    LogDropped(logger, message.Type, message.MessageId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Dropped {MessageType} {MessageId} after all delivery attempts failed (in-memory bus has no dead-letter topic)")]
    private static partial void LogDropped(ILogger logger, string messageType, Guid messageId);
}
