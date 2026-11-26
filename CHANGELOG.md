# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed

- The generated `.github/dependabot.yml` ignores semver-major updates of `dotnet-ef`, so the EF Core tool in `.config/dotnet-tools.json` stays on the same major as the EF Core packages. A template test now checks, for every combination, that each framework-aligned dependency of the generated service has such a rule, using this repository's Dependabot config as the list (#9)

## [0.1.0] - 2026-11-23

First release: the `goldenpath-api` template, packaged as `GoldenPath.Templates` and attached to the GitHub release.

### Added

**Template**

- `dotnet new goldenpath-api` with four options: `--database postgres|none`, `--messaging kafka|none`,
  `--observability otlp|appinsights|both`, `--auth jwt|none`. All 24 combinations build with warnings as errors and
  pass their tests.
- Service name flows into projects, namespaces and a kebab-case slug used for the chart, image, compose project
  and telemetry service name. Each instantiation gets its own user-secrets id and development signing key.

**Generated service**

- Vertical-slice minimal APIs with a sample Orders slice: place, get, list (keyset paging) and cancel; money as
  minor units with an ISO 4217 currency; UUIDv7 ids; timestamps at microsecond precision.
- Built-in .NET 10 request validation, RFC 9457 problem details everywhere with a stable `errorCode` and the
  `traceId`, URL-segment API versioning and a per-version OpenAPI document.
- Serilog console logging (JSON or text) bridged into OpenTelemetry; traces, metrics and logs exported over OTLP
  and/or to Azure Monitor, each switched on by its standard setting.
- `/health/live` and `/health/ready`; readiness checks only the service's own dependencies and fails while draining.
- Resilient typed `HttpClient` for a downstream customer directory (timeouts, retries with jitter, circuit breaker,
  no retries for unsafe methods), configured from `appsettings.json`, with a stub at the HTTP boundary for local runs.
- Per-client rate limiting with `429` and `Retry-After`; graceful shutdown with a drain delay and a bounded timeout;
  every options section validated at startup.
- `--auth jwt`: JWT bearer from `Authentication:Schemes:Bearer`, scope policies, a deny-by-default fallback policy,
  the bearer scheme in the OpenAPI document, and `POST /dev/tokens` in Development only.
- `--database postgres`: EF Core 10 with snake_case naming, an initial migration, order lines as a `jsonb` complex
  collection, `xmin` optimistic concurrency mapped to `409`, migrate-on-startup switch, Npgsql telemetry and a
  readiness check. An in-memory store keeps `dotnet run` Docker-free.
- `--messaging kafka`: `IMessagePublisher` over an outbox-ready envelope, idempotent producer, at-least-once
  consumer with retries and a dead-letter topic, W3C trace context in headers, topic bootstrap, readiness check and
  an in-memory transport for local runs.
- Tests: unit, API (`WebApplicationFactory`), architecture (ArchUnitNET), an approved OpenAPI contract snapshot and
  Testcontainers integration tests tagged `Category=Integration`, plus a shared `*.Testing` kit.
- Multi-stage `Dockerfile` on the chiseled, non-root ASP.NET image with a read-only root file system;
  `docker-compose.yml` with the chosen infrastructure and the Aspire dashboard.
- Helm chart: Deployment with probes, requests and limits and a restricted security context, Service, HPA, PDB,
  ConfigMap, a token-less ServiceAccount and a `helm test` readiness check.
- Generated GitHub Actions CI (build, tests, coverage summary, image, compose and chart validation), Dependabot
  configuration and a README filled in for the service.

**This repository**

- Template tests that generate all 24 combinations and check the output, plus a test that the package carries
  exactly the template files.
- CI: every combination built and unit-tested; a pairwise-covering set also runs integration tests, builds the
  image, runs the compose stack end to end and validates the chart; the default output is deployed to a kind
  cluster with `helm install --wait` and `helm test`.
- Release workflow that tests, packs, smoke-tests and attaches `GoldenPath.Templates.<version>.nupkg` to the
  GitHub release.
- Engineering handbook (onboarding, service anatomy, observability, errors, resilience, testing, code review,
  ADRs) and five architecture decision records.
- `eng/verify-combinations.sh` to build and test every combination locally without touching the machine's
  `dotnet new` configuration.

[Unreleased]: https://github.com/AutumnTrace/dotnet-golden-path/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/AutumnTrace/dotnet-golden-path/releases/tag/v0.1.0
