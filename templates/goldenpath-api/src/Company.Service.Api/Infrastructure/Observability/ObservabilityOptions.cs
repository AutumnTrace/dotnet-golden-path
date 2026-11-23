using System.ComponentModel.DataAnnotations;

namespace Company.Service.Api.Infrastructure.Observability;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>Console log format: <c>Json</c> for machines (default), <c>Text</c> for humans.</summary>
    public LogFormat LogFormat { get; set; } = LogFormat.Json;

    /// <summary>Head sampling ratio for new traces (0.0 to 1.0). Parent decisions are always respected.</summary>
    [Range(0.0, 1.0)]
    public double TraceSampleRatio { get; set; } = 1.0;
}

public enum LogFormat
{
    Json,
    Text,
}
