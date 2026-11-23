# Company.Service

Generated from the [`goldenpath-api`](https://github.com/AutumnTrace/dotnet-golden-path) template. The conventions
behind this code (logging, errors, resilience, testing, review) are in the
[engineering handbook](https://github.com/AutumnTrace/dotnet-golden-path/tree/main/docs/handbook).

## Run it

### Without Docker

```bash
dotnet run --project src/Company.Service.Api
```

The API listens on <http://localhost:5080>. In Development everything runs in-process:

- the customer directory is a stub at the HTTP boundary, so the resilience pipeline still runs;
<!--#if (UsePostgres) -->
- orders live in an in-memory store (set `Database:Provider=Postgres` and `ConnectionStrings:Database` to use PostgreSQL);
<!--#endif -->
<!--#if (UseKafka) -->
- messages go through an in-memory bus that runs the same handlers as Kafka;
<!--#endif -->
- logs are human-readable text.

<!--#if (UseJwt) -->
Every endpoint except the probes and the OpenAPI document needs a bearer token. In Development, ask for one:

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/dev/tokens -H "Content-Type: application/json" -d '{}' \
  | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')
```

`/dev/tokens` exists only in the Development environment. `dotnet user-jwts create --scope orders:read --scope orders:write`
works too.

```bash
curl -X POST http://localhost:5080/v1/orders -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"customerId":"cust-42","currency":"EUR","lines":[{"sku":"SKU-1","quantity":2,"unitPriceMinor":1250}]}'
```
<!--#else -->
```bash
curl -X POST http://localhost:5080/v1/orders -H "Content-Type: application/json" \
  -d '{"customerId":"cust-42","currency":"EUR","lines":[{"sku":"SKU-1","quantity":2,"unitPriceMinor":1250}]}'
```
<!--#endif -->

Customer ids starting with `blocked-` or `unknown-` make the stub refuse the order, so you can see the error paths.

### With Docker

```bash
docker compose up --build
```

<!--#if (UseOtlp) -->
The API is on <http://localhost:8080> and the Aspire dashboard (traces, metrics, structured logs) on
<http://localhost:18888>.
<!--#else -->
The API is on <http://localhost:8080>. Set `APPLICATIONINSIGHTS_CONNECTION_STRING` before starting to export
telemetry to Application Insights.
<!--#endif -->
<!--#if (UsePostgres) -->
PostgreSQL runs on port 5432 and the schema is migrated on startup.
<!--#endif -->
<!--#if (UseKafka) -->
Kafka listens on `localhost:9094` for tools on your machine.
<!--#endif -->

### On Kubernetes

```bash
docker build -t <registry>/company-service:<version> .
helm upgrade --install company-service deploy/helm/company-service \
  --set image.repository=<registry>/company-service --set image.tag=<version> \
<!--#if (UseJwt) -->
  --set config.Authentication__Schemes__Bearer__Authority=https://<your-identity-provider> \
<!--#endif -->
  --set existingSecret=company-service-secrets
```

Secrets (connection strings, Application Insights) go into the Kubernetes Secret named by `existingSecret`, never
into values files. See `deploy/helm/company-service/values.yaml` for every setting.

## Endpoints

| Method | Path | What it does |
|---|---|---|
| `POST` | `/v1/orders` | Place an order (`201` with `Location`; `400` shape errors, `422` business rules, `503` directory down) |
| `GET` | `/v1/orders/{id}` | Read one order |
| `GET` | `/v1/orders?customerId=&status=&limit=&after=` | List newest first, keyset paged with `nextCursor` |
| `POST` | `/v1/orders/{id}/cancel` | Cancel a placed order (idempotent; `409` if already paid) |
<!--#if (UseJwt) -->
| `POST` | `/dev/tokens` | Development only: mint a local JWT |
<!--#endif -->
| `GET` | `/health/live`, `/health/ready` | Liveness (no checks) and readiness (own dependencies only) |
| `GET` | `/openapi/v1.json` | OpenAPI 3.1 document |

Errors are RFC 9457 problem details with a stable `errorCode` and the `traceId`.

## Configuration

| Key | Default | Notes |
|---|---|---|
| `Observability:LogFormat` | `Json` | `Text` in Development |
| `Observability:TraceSampleRatio` | `1.0` | Head sampling for new traces |
<!--#if (UseOtlp) -->
| `OTEL_EXPORTER_OTLP_ENDPOINT` | unset | Turns on the OTLP exporter |
<!--#endif -->
<!--#if (UseAppInsights) -->
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | unset | Turns on the Azure Monitor exporter |
<!--#endif -->
<!--#if (UseJwt) -->
| `Authentication:Schemes:Bearer:Authority` | unset | Your identity provider; required outside Development |
| `Authentication:Schemes:Bearer:ValidAudiences` | `["company-service"]` | Accepted `aud` values |
<!--#endif -->
<!--#if (UsePostgres) -->
| `Database:Provider` | `Postgres` | `InMemory` in Development |
| `ConnectionStrings:Database` | unset | Required with `Postgres` |
| `Database:MigrateOnStartup` | `false` | Apply EF Core migrations before serving |
<!--#endif -->
<!--#if (UseKafka) -->
| `Messaging:Provider` | `Kafka` | `InMemory` in Development |
| `Messaging:BootstrapServers` | unset | Required with `Kafka` |
| `Messaging:ConsumerGroup` | `company-service` | |
| `Messaging:MaxDeliveryAttempts` | `3` | Then the message goes to `<topic>.dlq` |
<!--#endif -->
| `CustomerDirectory:BaseAddress` | `http://customer-directory/` | `UseStub=true` in Development |
| `CustomerDirectory:Resilience:*` | see `appsettings.json` | Timeouts, retries, circuit breaker |
| `RateLimiting:PermitLimit` / `Window` | `100` / `00:00:01` | Per client |
| `Shutdown:DrainDelay` / `Timeout` | `5 s` / `25 s` | Keep the pod grace period above the sum |

Every section is validated at startup; a bad value stops the service with a message that names the key.
Environment variables use `__` for `:`, for example `Database__Provider`.

## Tests

```bash
dotnet test --filter "Category!=Integration"   # unit, API and architecture tests: no Docker needed
<!--#if (UseIntegrationTests) -->
dotnet test --filter "Category=Integration"    # Testcontainers: real PostgreSQL / Kafka
<!--#endif -->
```

The OpenAPI document is snapshot-tested. If you change the API on purpose, run the tests with
`UPDATE_SNAPSHOTS=1` and commit `tests/Company.Service.ApiTests/Snapshots/openapi-v1.approved.json`.

<!--#if (UsePostgres) -->
## Database migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Company.Service.Api -o Infrastructure/Persistence/Migrations
```

<!--#endif -->
## Layout

```
src/Company.Service.Api/
  Features/Orders/        the sample slice: domain, use cases, endpoints
  Infrastructure/         errors, observability, health, rate limiting, lifecycle
  Integrations/           clients for other services
tests/                    unit, API, architecture and integration tests, shared test kit
deploy/helm/              Helm chart
.github/workflows/ci.yml  build, test, image, chart validation
```
