using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Company.Service.Api.Features.Orders;
using Company.Service.Api.Features.Orders.Domain;
using Company.Service.Api.Infrastructure.Messaging;
using Company.Service.Testing;
using Confluent.Kafka;

namespace Company.Service.IntegrationTests.Kafka;

[Trait("Category", "Integration")]
public class KafkaTests(IntegrationFixture infrastructure)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);
    private readonly HttpClient _client = infrastructure.Service.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Placing_an_order_publishes_OrderPlaced_with_id_type_and_trace_headers()
    {
        var order = await _client.PlaceOrderAsync(ApiClient.NewOrder(), Ct);

        var message = await ConsumeAsync(OrderTopics.OrderEvents, m => m.Key == order.Id.ToString());

        Assert.Equal(OrderPlaced.MessageType, Header(message, "message-type"));
        Assert.True(Guid.TryParse(Header(message, "message-id"), out _));
        Assert.StartsWith("00-", Header(message, "traceparent"), StringComparison.Ordinal);
        var body = JsonSerializer.Deserialize<OrderPlaced>(message.Value, ApiClient.Json);
        Assert.Equal(order.TotalMinor, body!.TotalMinor);
    }

    [Fact]
    public async Task A_payment_captured_event_from_kafka_marks_the_order_paid()
    {
        var order = await _client.PlaceOrderAsync(ApiClient.NewOrder(), Ct);
        var capturedAt = DateTimeOffset.UtcNow;
        var envelope = MessageEnvelope.Create(
            OrderTopics.PaymentEvents,
            order.Id.ToString(),
            new PaymentCaptured(Guid.CreateVersion7(), order.Id, order.TotalMinor, order.Currency, capturedAt),
            capturedAt);

        await ProduceAsync(OrderTopics.PaymentEvents, KafkaMessagePublisher.ToKafka(envelope, activity: null));

        var paid = await _client.WaitForOrderAsync(order.Id, o => o.Status == OrderStatus.Paid, Ct, Patience);
        Assert.Equal(OrderStatus.Paid, paid.Status);
    }

    [Fact]
    public async Task A_message_that_cannot_be_processed_is_parked_on_the_dead_letter_topic()
    {
        var key = $"poison-{Guid.NewGuid():N}";
        var poison = new Message<string, string>
        {
            Key = key,
            Value = "this is not json",
            Headers = new Headers
            {
                { "message-id", Encoding.UTF8.GetBytes(Guid.CreateVersion7().ToString()) },
                { "message-type", Encoding.UTF8.GetBytes(PaymentCaptured.MessageType) },
            },
        };

        await ProduceAsync(OrderTopics.PaymentEvents, poison);

        var parked = await ConsumeAsync(MessagingTopology.DeadLetterTopic(OrderTopics.PaymentEvents), m => m.Key == key);
        Assert.Equal("this is not json", parked.Value);
        Assert.False(string.IsNullOrEmpty(Header(parked, "dlq-error")));
    }

    [Fact]
    public async Task Readiness_reports_kafka()
    {
        var report = await _client.GetFromJsonAsync<JsonElement>("/health/ready", Ct);

        var kafka = report.GetProperty("checks").EnumerateArray().Single(check => check.GetProperty("name").GetString() == "kafka");
        Assert.Equal("Healthy", kafka.GetProperty("status").GetString());
    }

    private static string? Header(Message<string, string> message, string key) =>
        message.Headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    private async Task ProduceAsync(string topic, Message<string, string> message)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = infrastructure.Kafka.GetBootstrapAddress(),
        }).Build();
        await producer.ProduceAsync(topic, message, Ct);
    }

    private async Task<Message<string, string>> ConsumeAsync(string topic, Func<Message<string, string>, bool> match)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = infrastructure.Kafka.GetBootstrapAddress(),
            GroupId = $"test-reader-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(topic);

        // Consume() blocks; run it off the test thread and give up after Patience.
        return await Task.Run(
            () =>
            {
                var deadline = DateTimeOffset.UtcNow + Patience;
                while (DateTimeOffset.UtcNow < deadline)
                {
                    var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                    if (result?.Message is { } message && match(message))
                    {
                        return message;
                    }
                }

                throw new TimeoutException($"No matching message on {topic} within {Patience.TotalSeconds} s.");
            },
            Ct);
    }
}
