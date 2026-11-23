using Company.Service.Api.Infrastructure.Messaging;

namespace Company.Service.UnitTests.Fakes;

internal sealed class RecordingPublisher : IMessagePublisher
{
    public List<MessageEnvelope> Published { get; } = [];

    public Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }
}
