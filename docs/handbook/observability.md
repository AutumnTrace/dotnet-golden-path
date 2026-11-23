# Logging, tracing and metrics

Telemetry is how we debug production; nobody attaches a debugger to a pod. The setup is described in
[ADR 0002](../adr/0002-opentelemetry-single-telemetry-api.md). This page is about using it well.

## Logs

- **Log through `ILogger<T>`**, never `Console` and never a vendor SDK. Serilog formats the output and forwards it
  to OpenTelemetry for you.
- **Use message templates, not interpolation.** `"Order {OrderId} placed"`, not `$"Order {id} placed"`. Templates
  become queryable fields; interpolated strings become noise.
- **Name properties in PascalCase and keep them stable.** Dashboards and alerts depend on `OrderId`, not on the
  sentence around it.
- **Use `[LoggerMessage]` source generation** for anything on a request path. It avoids boxing and string work when
  the level is off, and gives each message a stable `EventId` you can alert on. Number event ids per slice
  (Orders uses 1001-1099).
- **Levels mean something:**

  | Level | Use for | Example |
  |---|---|---|
  | `Error` | Something failed that a human may need to act on | A message was dead-lettered |
  | `Warning` | Degraded but handled; worth a look if it repeats | A dependency was unavailable, a payment did not match its order |
  | `Information` | Business events and state changes | Order placed, order cancelled |
  | `Debug` | Details for local debugging; off in production | A message type had no handler |

- **One request, one summary line.** Serilog request logging writes `HTTP POST /v1/orders responded 201 in 12.3 ms`
  with the endpoint name. Do not add your own "request started/finished" logs. Health probes log at `Verbose`.
- **Never log secrets or personal data**: tokens, passwords, connection strings, card numbers (PAN), CVV, full
  names, emails, IP addresses. Log opaque ids (`OrderId`, `CustomerId`) instead. Exception messages from
  infrastructure can contain connection strings, which is why health responses omit them.

## Traces

- HTTP server and client, EF Core/Npgsql and Kafka produce spans automatically. Add a custom span only for work
  that is not already a span: a background job, a batch step, a message handler you call yourself.
- **Tag the current span with business ids**: `Activity.Current?.SetTag("order.id", order.Id)`. That is how you
  find "the trace for order X".
- **Name spans by operation, not by instance**: `process payments.payment-events.v1`, never a name with an id in it.
- Messages carry W3C `traceparent` in their headers. A consumer span joins the producer's trace, so one trace shows
  the HTTP request and every message it caused.
- Health probes are excluded from tracing; sampling is head-based (`Observability:TraceSampleRatio`, default 1.0)
  and respects the parent's decision.

## Metrics

- **Create instruments through `IMeterFactory`** with the service meter name (`Telemetry.MeterName`). Tests can then
  observe them with `MetricCollector<T>`.
- **Name like OpenTelemetry does**: lowercase, dot-separated, a unit in braces for counts: `orders.placed`
  (`{order}`), `messaging.messages.processed` (`{message}`).
- **Keep cardinality low.** Tags are for small, bounded sets: currency, outcome, message type. Never an id, a user,
  a URL with ids in it, or free text: each distinct value is a new time series that someone pays for.
- Built in: ASP.NET Core request duration and active requests, `HttpClient` duration, runtime (GC, thread pool,
  exceptions), Polly resilience events, Npgsql connection pool.

## Exporters and environments

| Setting | Effect |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Turns on the OTLP exporter for traces, metrics and logs |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Turns on the Azure Monitor exporter |
| `OTEL_SERVICE_NAME` | Overrides the service name (default: the service slug) |
| `Observability:LogFormat` | `Json` (default) or `Text` (Development) |
| `Serilog:MinimumLevel` | Default and per-namespace levels; the only place levels are configured |

Locally, `docker compose up` starts the Aspire dashboard as the OTLP endpoint on <http://localhost:18888>.
