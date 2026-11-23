# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `goldenpath-api` template packaged as `GoldenPath.Templates`
- Generated service: vertical-slice minimal APIs with a sample Orders slice, built-in request validation, RFC 9457 problem details with stable error codes, URL-segment API versioning and per-version OpenAPI documents
- Serilog console logging (JSON or text) bridged into OpenTelemetry; traces, metrics and logs with OTLP and/or Azure Monitor exporters (`--observability`)
- `/health/live` and `/health/ready` probes
- `--auth jwt|none`: JWT bearer authentication configured from `Authentication:Schemes:Bearer` (works with `dotnet user-jwts`), scope policies, a secure-by-default fallback policy and the bearer scheme in the OpenAPI document
- Per-client rate limiting with `429` problem details and `Retry-After`
- Resilient typed `HttpClient` for a downstream customer directory (standard pipeline: timeouts, retries with jitter, circuit breaker), configured from `appsettings.json`, with a local stub at the HTTP boundary
- Graceful shutdown: readiness fails first, a configurable drain delay, then a bounded shutdown timeout
- Options validated at startup for every settings section; bad configuration stops the service with a message naming the setting
- `--database postgres|none`: EF Core 10 on PostgreSQL with snake_case naming, migrations (applied on startup when enabled), order lines as a `jsonb` complex collection, `xmin` optimistic concurrency mapped to `409`, keyset paging, Npgsql tracing and a readiness check
- `--messaging kafka|none`: `IMessagePublisher` over an outbox-ready `MessageEnvelope`, idempotent Kafka producer, at-least-once consumer with bounded retries and a dead-letter topic, W3C trace context in headers, a readiness check and an in-memory transport for local runs
- Architecture tests (ArchUnitNET) that enforce slice independence, dependency direction, a framework-free domain and "only X may use library Y" rules, each with a positive control
- OpenAPI contract snapshot test with an approved file per auth variant (`UPDATE_SNAPSHOTS=1` to accept a change)
- Testcontainers integration tests (`Category=Integration`) for PostgreSQL and Kafka, and a shared `*.Testing` kit for API and integration tests
- Multi-stage Dockerfile on the chiseled, non-root ASP.NET runtime image; runs with a read-only root file system
- `docker-compose.yml` with the service, its chosen infrastructure and the Aspire dashboard as OTLP endpoint
- Helm chart: Deployment (startup, liveness and readiness probes, resource requests and limits, read-only root file system, dropped capabilities, seccomp), Service, HPA, PDB, ConfigMap, ServiceAccount without a token, and a `helm test` readiness check
- Generated GitHub Actions CI (build, tests, coverage summary, image, compose validation, Helm lint, kubeconform) and Dependabot configuration
- `POST /dev/tokens` for local JWTs, available only in the Development environment
- Unit and API tests in the generated service; template tests and a CI matrix in this repository
- CI here: every combination builds and tests; a pairwise set also runs integration tests, builds the image, runs the compose stack end to end and validates the chart; the default output is deployed to a kind cluster with `helm install --wait`
- Release workflow that attaches the template package to the GitHub release

[Unreleased]: https://github.com/AutumnTrace/dotnet-golden-path/commits/main
