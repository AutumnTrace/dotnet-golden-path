using System.Diagnostics;
using System.Globalization;
using System.Text;
using Company.Service.Api.Infrastructure.Observability;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace Company.Service.Api.Infrastructure.Messaging;

/// <summary>
/// Idempotent producer (<c>acks=all</c>, <c>enable.idempotence=true</c>): broker-side retries cannot duplicate or
/// reorder messages. One producer per process; it is thread-safe and batches internally.
/// </summary>
internal sealed partial class KafkaMessagePublisher : IMessagePublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private int _disposed;

    public KafkaMessagePublisher(IOptions<MessagingOptions> options, ILogger<KafkaMessagePublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            ClientId = Telemetry.DefaultServiceName,
            Acks = Acks.All,
            EnableIdempotence = true,
            LingerMs = 5,
        })
            .SetErrorHandler((_, error) => LogKafkaError(logger, error.Code, error.Reason))
            .Build();
    }

    public async Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var activity = MessagingTelemetry.StartPublish(message);
        try
        {
            var result = await _producer.ProduceAsync(message.Topic, ToKafka(message, activity), cancellationToken);
            activity?.SetTag("messaging.destination.partition.id", result.Partition.Value.ToString(CultureInfo.InvariantCulture));
        }
        catch (ProduceException<string, string> exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Error.Reason);
            throw;
        }
    }

    /// <summary>Parks a message that could not be processed, with the reason, so it can be inspected and replayed.</summary>
    public Task PublishDeadLetterAsync(ConsumeResult<string, string> result, Exception? error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        var headers = new Headers();
        foreach (var header in result.Message.Headers ?? [])
        {
            headers.Add(header.Key, header.GetValueBytes());
        }

        headers.Add("dlq-source", Encoding.UTF8.GetBytes($"{result.Topic}/{result.Partition.Value}/{result.Offset.Value}"));
        headers.Add("dlq-error", Encoding.UTF8.GetBytes(error?.GetType().Name ?? "unknown"));

        return _producer.ProduceAsync(
            MessagingTopology.DeadLetterTopic(result.Topic),
            new Message<string, string> { Key = result.Message.Key, Value = result.Message.Value, Headers = headers },
            cancellationToken);
    }

    public void Dispose()
    {
        // Registered as itself and as IMessagePublisher, so the container may dispose it twice.
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // Deliver whatever is still batched before the process exits.
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }

    internal static Message<string, string> ToKafka(MessageEnvelope message, Activity? activity) => new()
    {
        Key = message.Key,
        Value = message.Payload,
        Timestamp = new Timestamp(message.OccurredAt),
        Headers = new Headers
        {
            { KafkaHeaders.MessageId, Encoding.UTF8.GetBytes(message.MessageId.ToString()) },
            { KafkaHeaders.MessageType, Encoding.UTF8.GetBytes(message.Type) },
            { KafkaHeaders.OccurredAt, Encoding.UTF8.GetBytes(message.OccurredAt.ToString("O", CultureInfo.InvariantCulture)) },
            { KafkaHeaders.TraceParent, Encoding.UTF8.GetBytes(activity?.Id ?? message.TraceParent ?? string.Empty) },
        },
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kafka producer error {Code}: {Reason}")]
    private static partial void LogKafkaError(ILogger logger, ErrorCode code, string reason);
}

internal static class KafkaHeaders
{
    public const string MessageId = "message-id";
    public const string MessageType = "message-type";
    public const string OccurredAt = "occurred-at";
    public const string TraceParent = "traceparent";

    public static MessageEnvelope ToEnvelope(ConsumeResult<string, string> result)
    {
        var headers = result.Message.Headers;
        string? Read(string key) => headers is not null && headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

        return new MessageEnvelope(
            Guid.TryParse(Read(MessageId), out var id) ? id : Guid.Empty,
            result.Topic,
            result.Message.Key ?? string.Empty,
            Read(MessageType) ?? string.Empty,
            DateTimeOffset.TryParse(Read(OccurredAt), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? at
                : result.Message.Timestamp.UtcDateTime,
            result.Message.Value ?? string.Empty,
            Read(TraceParent) is { Length: > 0 } traceParent ? traceParent : null);
    }
}
