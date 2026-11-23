using System.Diagnostics.Metrics;
using System.Text.Json;
using Company.Service.Api.Features.Orders;
using Company.Service.Api.Features.Orders.Domain;
using Company.Service.Api.Infrastructure.Messaging;
using Company.Service.Api.Infrastructure.Observability;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Company.Service.UnitTests.Messaging;

public sealed class MessagingTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);
    private readonly ServiceProvider _services;

    public MessagingTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:Provider"] = "InMemory",
                ["Messaging:MaxDeliveryAttempts"] = "3",
            })
            .Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddMetrics()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IOrderStore, InMemoryOrderStore>()
            .AddSingleton<OrdersMetrics>()
            .AddSingleton<FlakyHandler.Counter>()
            .AddMessaging(configuration)
            .AddMessageHandler<PaymentCaptured, PaymentCapturedHandler>(OrderTopics.PaymentEvents)
            .AddMessageHandler<FlakyMessage, FlakyHandler>("tests.flaky");
        _services = services.BuildServiceProvider(validateScopes: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MessageDispatcher Dispatcher => _services.GetRequiredService<MessageDispatcher>();

    [Fact]
    public void An_envelope_round_trips_its_message_and_is_time_ordered()
    {
        var message = new OrderPlaced(Guid.CreateVersion7(), "cust-1", "EUR", 2_500, Now);

        var envelope = MessageEnvelope.Create(OrderTopics.OrderEvents, message.OrderId.ToString(), message, Now);

        Assert.Equal(OrderPlaced.MessageType, envelope.Type);
        Assert.Equal(7, envelope.MessageId.Version);
        Assert.Equal(message, envelope.Read<OrderPlaced>());
        Assert.Contains("\"totalMinor\":2500", envelope.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void Kafka_headers_carry_id_type_time_and_trace_context_both_ways()
    {
        var envelope = MessageEnvelope.Create("orders", "key-1", new OrderCancelled(Guid.CreateVersion7(), "test", Now), Now)
            with { TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01" };

        var kafka = KafkaMessagePublisher.ToKafka(envelope, activity: null);
        var back = KafkaHeaders.ToEnvelope(new ConsumeResult<string, string> { Topic = "orders", Message = kafka });

        Assert.Equal(envelope, back);
    }

    [Fact]
    public async Task A_payment_event_marks_the_order_paid_and_redelivery_is_harmless()
    {
        var store = _services.GetRequiredService<IOrderStore>();
        var order = Order.Place("cust-1", "EUR", [new("SKU-1", 2, 1_250)], Now).Value;
        await store.AddAsync(order, Ct);
        using var paid = new MetricCollector<long>(_services.GetRequiredService<IMeterFactory>(), Telemetry.MeterName, "orders.paid");
        var envelope = MessageEnvelope.Create(
            OrderTopics.PaymentEvents,
            order.Id.ToString(),
            new PaymentCaptured(Guid.CreateVersion7(), order.Id, 2_500, "EUR", Now.AddMinutes(1)),
            Now.AddMinutes(1));

        var first = await Dispatcher.DispatchAsync(envelope, Ct);
        var redelivered = await Dispatcher.DispatchAsync(envelope, Ct);

        Assert.Equal(DispatchOutcome.Handled, first.Outcome);
        Assert.Equal(DispatchOutcome.Handled, redelivered.Outcome);
        Assert.Equal(OrderStatus.Paid, (await store.FindAsync(order.Id, Ct))!.Status);
        Assert.Single(paid.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task Messages_nobody_handles_are_skipped_not_failed()
    {
        var envelope = new MessageEnvelope(Guid.CreateVersion7(), "orders", "k", "someone-else.event.v1", Now, "{}");

        var (outcome, _) = await Dispatcher.DispatchAsync(envelope, Ct);

        Assert.Equal(DispatchOutcome.Skipped, outcome);
    }

    [Fact]
    public async Task Transient_handler_failures_are_retried()
    {
        var counter = _services.GetRequiredService<FlakyHandler.Counter>();
        counter.FailuresLeft = 2;

        var (outcome, _) = await Dispatcher.DispatchAsync(Flaky(), Ct);

        Assert.Equal(DispatchOutcome.Handled, outcome);
        Assert.Equal(3, counter.Calls);
    }

    [Fact]
    public async Task A_message_that_keeps_failing_is_reported_for_dead_lettering()
    {
        var counter = _services.GetRequiredService<FlakyHandler.Counter>();
        counter.FailuresLeft = int.MaxValue;

        var (outcome, error) = await Dispatcher.DispatchAsync(Flaky(), Ct);

        Assert.Equal(DispatchOutcome.Failed, outcome);
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(3, counter.Calls);
    }

    [Fact]
    public async Task A_payload_that_cannot_be_read_fails_instead_of_being_silently_dropped()
    {
        var envelope = new MessageEnvelope(Guid.CreateVersion7(), OrderTopics.PaymentEvents, "k", PaymentCaptured.MessageType, Now, "not json");

        var (outcome, error) = await Dispatcher.DispatchAsync(envelope, Ct);

        Assert.Equal(DispatchOutcome.Failed, outcome);
        Assert.IsType<JsonException>(error, exactMatch: false);
    }

    public void Dispose() => _services.Dispose();

    private static MessageEnvelope Flaky() => MessageEnvelope.Create("tests.flaky", "k", new FlakyMessage(1), Now);

    internal sealed record FlakyMessage(int Value) : IMessage
    {
        public static string MessageType => "tests.flaky.v1";
    }

    internal sealed class FlakyHandler(FlakyHandler.Counter counter) : IMessageHandler<FlakyMessage>
    {
        public Task HandleAsync(FlakyMessage message, MessageEnvelope envelope, CancellationToken cancellationToken)
        {
            counter.Calls++;
            return counter.FailuresLeft-- > 0
                ? throw new InvalidOperationException("Downstream hiccup")
                : Task.CompletedTask;
        }

        internal sealed class Counter
        {
            public int Calls { get; set; }

            public int FailuresLeft { get; set; }
        }
    }
}
