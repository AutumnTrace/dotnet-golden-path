#if (UseAppInsights)
using Azure.Monitor.OpenTelemetry.Exporter;
#endif
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Company.Service.Api.Infrastructure.Observability;

/// <summary>
/// One telemetry pipeline: code logs through <see cref="Microsoft.Extensions.Logging.ILogger"/>, Serilog formats
/// and enriches those events for the console, and OpenTelemetry carries logs, traces and metrics to whichever
/// exporters are configured. Exporters are switched on by configuration, never by code changes.
/// </summary>
public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var section = builder.Configuration.GetSection(ObservabilityOptions.SectionName);
        builder.Services.AddOptions<ObservabilityOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var settings = section.Get<ObservabilityOptions>() ?? new ObservabilityOptions();
        var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] is { Length: > 0 } configured
            ? configured
            : Telemetry.DefaultServiceName;
        var environment = builder.Environment.EnvironmentName;

        // Logs: ILogger -> Serilog (console) -> OpenTelemetry logger provider (exporters).
        builder.Logging.ClearProviders();
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddSerilog(
            (services, logger) =>
            {
                logger
                    .ReadFrom.Configuration(builder.Configuration)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("service.name", serviceName)
                    .Enrich.WithProperty("service.version", Telemetry.ServiceVersion)
                    .Enrich.WithProperty("deployment.environment", environment);

                if (settings.LogFormat == LogFormat.Text)
                {
                    logger.WriteTo.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                        formatProvider: System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    logger.WriteTo.Console(new RenderedCompactJsonFormatter());
                }
            },
            writeToProviders: true);

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: Telemetry.ServiceVersion, serviceInstanceId: Environment.MachineName)
                .AddAttributes([new("deployment.environment.name", environment)]))
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(settings.TraceSampleRatio)))
                .AddSource(Telemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !IsProbe(context.Request.Path))
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(Telemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

#if (UseOtlp)
        // Standard OTEL_EXPORTER_OTLP_* variables configure endpoint, protocol and headers.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }
#endif
#if (UseAppInsights)
        // Application Insights is just another exporter; instrumentation stays vendor neutral.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            otel.UseAzureMonitorExporter(options =>
                options.ConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]);
        }
#endif

        return builder;
    }

    /// <summary>Logs one structured event per request, enriched with what you need to find it again.</summary>
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms";
            options.GetLevel = (context, _, exception) =>
                exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? LogEventLevel.Error
                    : IsProbe(context.Request.Path) ? LogEventLevel.Verbose : LogEventLevel.Information;
            options.EnrichDiagnosticContext = (diagnostics, context) =>
            {
                diagnostics.Set("EndpointName", context.GetEndpoint()?.DisplayName);
                diagnostics.Set("UserAgent", context.Request.Headers.UserAgent.ToString());
            };
        });

    private static bool IsProbe(PathString path) => path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
}
