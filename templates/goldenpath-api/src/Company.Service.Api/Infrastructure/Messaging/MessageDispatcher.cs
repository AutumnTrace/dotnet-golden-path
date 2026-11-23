using System.Diagnostics;
using System.Diagnostics.Metrics;
using Company.Service.Api.Infrastructure.Observability;
using Microsoft.Extensions.Options;

namespace Company.Service.Api.Infrastructure.Messaging;

public enum DispatchOutcome
{
    Handled,

    /// <summary>No handler for the message type: acknowledged and skipped (other services share the topic).</summary>
    Skipped,

    /// <summary>Every attempt failed; the transport dead-letters the message.</summary>
    Failed,
}

/// <summary>
/// Transport-independent delivery: one DI scope and one consumer span per message, bounded retries with
/// backoff, and a metric per outcome. Kafka and the in-memory bus both go through here.
/// </summary>
internal sealed partial class MessageDispatcher
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<MessagingTopology> _topology;
    private readonly IOptions<MessagingOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<MessageDispatcher> _logger;
    private readonly Counter<long> _processed;

    public MessageDispatcher(
        IServiceScopeFactory scopes,
        IOptions<MessagingTopology> topology,
        IOptions<MessagingOptions> options,
        IMeterFactory meterFactory,
        TimeProvider clock,
        ILogger<MessageDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _scopes = scopes;
        _topology = topology;
        _options = options;
        _clock = clock;
        _logger = logger;
        _processed = meterFactory.Create(Telemetry.MeterName)
            .CreateCounter<long>("messaging.messages.processed", unit: "{message}", description: "Messages processed, by type and outcome.");
    }

    public async Task<(DispatchOutcome Outcome, Exception? Error)> DispatchAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        using var activity = MessagingTelemetry.StartProcess(envelope);
        if (!_topology.Value.Handlers.TryGetValue(envelope.Type, out var handle))
        {
            LogSkipped(_logger, envelope.Type, envelope.MessageId);
            return Record(envelope, DispatchOutcome.Skipped, error: null);
        }

        Exception? lastError = null;
        var attempts = _options.Value.MaxDeliveryAttempts;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await handle(scope.ServiceProvider, envelope, cancellationToken);
                return Record(envelope, DispatchOutcome.Handled, error: null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                lastError = exception;
                LogAttemptFailed(_logger, exception, envelope.Type, envelope.MessageId, attempt, attempts);
                if (attempt < attempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1)), _clock, cancellationToken);
                }
            }
        }

        activity?.SetStatus(ActivityStatusCode.Error, lastError?.Message);
        return Record(envelope, DispatchOutcome.Failed, lastError);
    }

    private (DispatchOutcome, Exception?) Record(MessageEnvelope envelope, DispatchOutcome outcome, Exception? error)
    {
        _processed.Add(1, new("messaging.message.type", envelope.Type), new("outcome", outcome.ToString()));
        return (outcome, error);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "No handler for {MessageType}; skipping message {MessageId}")]
    private static partial void LogSkipped(ILogger logger, string messageType, Guid messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handling {MessageType} {MessageId} failed (attempt {Attempt} of {MaxAttempts})")]
    private static partial void LogAttemptFailed(ILogger logger, Exception exception, string messageType, Guid messageId, int attempt, int maxAttempts);
}
