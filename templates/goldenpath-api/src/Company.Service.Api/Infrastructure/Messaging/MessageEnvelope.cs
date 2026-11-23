using System.Diagnostics;
using System.Text.Json;

namespace Company.Service.Api.Infrastructure.Messaging;

/// <summary>A message contract. <see cref="MessageType"/> is the stable, versioned name on the wire.</summary>
public interface IMessage
{
    static abstract string MessageType { get; }
}

/// <summary>
/// Everything needed to deliver a message, as plain data. This is deliberately the shape of an outbox row:
/// a service that needs atomic "save + publish" can store envelopes in its own transaction and let a relay
/// hand them to the transport later, without touching any handler or publisher call site.
/// </summary>
/// <param name="MessageId">Unique per message; consumers use it to drop duplicates (delivery is at-least-once).</param>
/// <param name="Key">Partition key: messages with the same key keep their order.</param>
/// <param name="TraceParent">W3C trace context of the operation that produced the message.</param>
public sealed record MessageEnvelope(
    Guid MessageId,
    string Topic,
    string Key,
    string Type,
    DateTimeOffset OccurredAt,
    string Payload,
    string? TraceParent = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static MessageEnvelope Create<TMessage>(string topic, string key, TMessage message, DateTimeOffset occurredAt)
        where TMessage : IMessage =>
        new(
            Guid.CreateVersion7(occurredAt),
            topic,
            key,
            TMessage.MessageType,
            occurredAt,
            JsonSerializer.Serialize(message, Json),
            Activity.Current?.Id);

    /// <exception cref="JsonException">The payload is not a valid <typeparamref name="TMessage"/>.</exception>
    public TMessage Read<TMessage>()
        where TMessage : IMessage =>
        JsonSerializer.Deserialize<TMessage>(Payload, Json)
        ?? throw new JsonException($"Message {MessageId} of type {Type} has an empty payload.");
}

public interface IMessagePublisher
{
    Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken);
}

public interface IMessageHandler<in TMessage>
    where TMessage : IMessage
{
    /// <summary>Must be idempotent: the same message can arrive more than once.</summary>
    Task HandleAsync(TMessage message, MessageEnvelope envelope, CancellationToken cancellationToken);
}

public static class MessagePublisherExtensions
{
    public static Task PublishAsync<TMessage>(
        this IMessagePublisher publisher,
        string topic,
        string key,
        TMessage message,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
        where TMessage : IMessage
    {
        ArgumentNullException.ThrowIfNull(publisher);
        return publisher.PublishAsync(MessageEnvelope.Create(topic, key, message, occurredAt), cancellationToken);
    }
}
