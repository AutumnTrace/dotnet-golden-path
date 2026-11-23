using System.Diagnostics;
using System.Reflection;

namespace Company.Service.Api.Infrastructure.Observability;

/// <summary>Names shared by every custom span and instrument the service emits.</summary>
public static class Telemetry
{
    /// <summary>Default <c>service.name</c>; override with the standard <c>OTEL_SERVICE_NAME</c> variable.</summary>
    public const string DefaultServiceName = "company-service";

    public const string ActivitySourceName = "Company.Service.Api";

    public const string MeterName = "Company.Service.Api";

    public static readonly string ServiceVersion =
        typeof(Telemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>Source for custom spans (messaging, background work). HTTP and outbound calls are instrumented for you.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, ServiceVersion);
}
