using System.Diagnostics;
using Company.Service.Api.Infrastructure.Observability;

namespace Company.Service.Api.Infrastructure.Messaging;

/// <summary>
/// Producer and consumer spans following the OpenTelemetry messaging conventions. The producer's trace context
/// travels in the <c>traceparent</c> header, so a consumer span joins the trace that caused the message.
/// </summary>
internal static class MessagingTelemetry
{
    public static Activity? StartPublish(MessageEnvelope envelope)
    {
        var parent = Activity.Current?.Context ?? Parse(envelope.TraceParent);
        var activity = Telemetry.ActivitySource.StartActivity($"send {envelope.Topic}", ActivityKind.Producer, parent);
        return Tag(activity, envelope, "send");
    }

    public static Activity? StartProcess(MessageEnvelope envelope)
    {
        var activity = Telemetry.ActivitySource.StartActivity($"process {envelope.Topic}", ActivityKind.Consumer, Parse(envelope.TraceParent));
        return Tag(activity, envelope, "process");
    }

    private static ActivityContext Parse(string? traceParent) =>
        ActivityContext.TryParse(traceParent, traceState: null, out var context) ? context : default;

    private static Activity? Tag(Activity? activity, MessageEnvelope envelope, string operation) =>
        activity?
            .SetTag("messaging.system", "kafka")
            .SetTag("messaging.operation.type", operation)
            .SetTag("messaging.destination.name", envelope.Topic)
            .SetTag("messaging.message.id", envelope.MessageId.ToString())
            .SetTag("messaging.kafka.message.key", envelope.Key)
            .SetTag("messaging.message.type", envelope.Type);
}
