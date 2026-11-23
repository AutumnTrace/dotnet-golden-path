using System.ComponentModel.DataAnnotations;
using Company.Service.Api.Infrastructure.Health;
using Microsoft.Extensions.Options;

namespace Company.Service.Api.Infrastructure.Messaging;

public enum MessagingProvider
{
    /// <summary>In-process channel: local runs without Docker and fast API tests.</summary>
    InMemory,
    Kafka,
}

public sealed class MessagingOptions : IValidatableObject
{
    public const string SectionName = "Messaging";

    public MessagingProvider Provider { get; set; } = MessagingProvider.Kafka;

    public string? BootstrapServers { get; set; }

    [Required]
    public string ConsumerGroup { get; set; } = "company-service";

    /// <summary>Delivery attempts per message before it goes to the dead-letter topic.</summary>
    [Range(1, 10)]
    public int MaxDeliveryAttempts { get; set; } = 3;

    /// <summary>Create the topics this service uses at startup (local and CI only; production topics are provisioned).</summary>
    public bool CreateTopicsOnStartup { get; set; }

    public static MessagingProvider ProviderFrom(IConfiguration configuration) =>
        configuration.GetValue($"{SectionName}:{nameof(Provider)}", MessagingProvider.Kafka);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provider == MessagingProvider.Kafka && string.IsNullOrWhiteSpace(BootstrapServers))
        {
            yield return new ValidationResult(
                "Messaging:BootstrapServers is required when Messaging:Provider is Kafka.",
                [nameof(BootstrapServers)]);
        }
    }
}

/// <summary>Which topics the service produces to and consumes from, and who handles each message type.</summary>
public sealed class MessagingTopology
{
    internal Dictionary<string, Func<IServiceProvider, MessageEnvelope, CancellationToken, Task>> Handlers { get; } = new(StringComparer.Ordinal);

    internal SortedSet<string> ConsumedTopics { get; } = new(StringComparer.Ordinal);

    internal SortedSet<string> ProducedTopics { get; } = new(StringComparer.Ordinal);

    public static string DeadLetterTopic(string topic) => $"{topic}.dlq";
}

public static class MessagingExtensions
{
    /// <summary>The transport (Kafka or in-memory), the consumer loop, dispatching, telemetry and the readiness check.</summary>
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MessagingOptions>()
            .Bind(configuration.GetSection(MessagingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<MessagingTopology>();
        services.AddSingleton<MessageDispatcher>();

        if (MessagingOptions.ProviderFrom(configuration) == MessagingProvider.Kafka)
        {
            services.AddSingleton<KafkaMessagePublisher>();
            services.AddSingleton<IMessagePublisher>(provider => provider.GetRequiredService<KafkaMessagePublisher>());
            services.AddHostedService<KafkaTopicInitializer>();
            services.AddHostedService<KafkaConsumerService>();
            services.AddSingleton<KafkaHealthCheck>();
            services.AddHealthChecks().AddCheck<KafkaHealthCheck>("kafka", tags: [HealthExtensions.ReadyTag]);
        }
        else
        {
            services.AddSingleton<InMemoryMessageBus>();
            services.AddSingleton<IMessagePublisher>(provider => provider.GetRequiredService<InMemoryMessageBus>());
            services.AddHostedService<InMemoryConsumerService>();
        }

        return services;
    }

    public static IServiceCollection AddProducedTopic(this IServiceCollection services, string topic) =>
        services.Configure<MessagingTopology>(topology => topology.ProducedTopics.Add(topic));

    public static IServiceCollection AddMessageHandler<TMessage, THandler>(this IServiceCollection services, string topic)
        where TMessage : IMessage
        where THandler : class, IMessageHandler<TMessage>
    {
        services.AddScoped<THandler>();
        return services.Configure<MessagingTopology>(topology =>
        {
            topology.ConsumedTopics.Add(topic);
            topology.Handlers[TMessage.MessageType] = (provider, envelope, cancellationToken) =>
                provider.GetRequiredService<THandler>().HandleAsync(envelope.Read<TMessage>(), envelope, cancellationToken);
        });
    }

    internal static MessagingTopology Topology(this IServiceProvider services) =>
        services.GetRequiredService<IOptions<MessagingTopology>>().Value;
}
