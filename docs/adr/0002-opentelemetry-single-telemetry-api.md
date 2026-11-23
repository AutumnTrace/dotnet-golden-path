# 0002. OpenTelemetry as the single telemetry API, exporters chosen by configuration

- **Status:** Accepted
- **Date:** 2026-11-23

## Context

Teams run services against different backends: some ship to an OpenTelemetry Collector (and from there to
Grafana, Jaeger or a vendor), some to Azure Application Insights, a few to both during a migration. Previously each
backend came with its own SDK in the code (`TelemetryClient`, vendor log sinks), so switching backend meant
changing code in every service and losing correlation between logs and traces along the way.

We also standardised on Serilog for log formatting years ago and want to keep its enrichment and console output.

## Decision

- **Code talks to vendor-neutral APIs only**: `ILogger` (with `LoggerMessage` source generation on hot paths),
  `ActivitySource` for custom spans, `Meter`/`IMeterFactory` for metrics. No vendor SDK types in feature code.
- **OpenTelemetry carries all three signals.** ASP.NET Core, `HttpClient`, runtime, Npgsql and Polly
  instrumentation are on by default; health probes are not traced.
- **Serilog formats and enriches** console output (JSON in production, text locally) and forwards every event to
  the OpenTelemetry logger provider (`writeToProviders: true`), so logs carry the active trace and span ids.
- **Exporters are a template option and a runtime switch.** `--observability otlp|appinsights|both` decides which
  exporter packages are referenced; each exporter turns on only when its standard setting is present
  (`OTEL_EXPORTER_OTLP_ENDPOINT`, `APPLICATIONINSIGHTS_CONNECTION_STRING`). With neither set, the service runs
  with console logs only, which is what you want in tests and on a laptop.
- Messaging propagates W3C trace context in Kafka headers, so a consumer span joins the trace that caused it.

## Consequences

- Moving from Application Insights to an OTLP backend (or running both) is a configuration change.
- One set of semantic conventions (`http.route`, `messaging.destination.name`, ...) across services makes
  cross-service dashboards possible.
- The Azure Monitor exporter is used rather than the "distro" package, so Application Insights receives exactly
  what OpenTelemetry instruments; nothing extra is auto-collected. Teams that relied on classic auto-collected
  dependencies need to check their dashboards once.
- Tests can assert on telemetry with the in-memory exporter and `MetricCollector<T>`; the template does that for
  the request span and the business counter.

## Alternatives considered

- **Application Insights SDK directly.** Locks code to one vendor and makes local runs noisy. Rejected.
- **Serilog sinks per backend (Seq, Elastic, OTLP sink).** Logs would be fine, but traces and metrics would still
  need OpenTelemetry, giving us two pipelines to configure. Rejected in favour of one pipeline.
- **OpenTelemetry for logs too, without Serilog.** Viable; kept Serilog because its console formatting,
  enrichment and level overrides are what operators already know.
