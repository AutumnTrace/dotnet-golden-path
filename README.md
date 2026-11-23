# dotnet-golden-path

**A `dotnet new` template for production .NET 10 services: OpenTelemetry, Serilog, resilience, health probes and tests, ready on day one.**

[![CI](https://github.com/AutumnTrace/dotnet-golden-path/actions/workflows/ci.yml/badge.svg)](https://github.com/AutumnTrace/dotnet-golden-path/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> [!NOTE]
> Work in progress. Documentation and the engineering handbook land next.

## Quick start

```bash
git clone https://github.com/AutumnTrace/dotnet-golden-path.git
dotnet new install ./dotnet-golden-path/templates/goldenpath-api
dotnet new goldenpath-api -n Orders
cd Orders
dotnet run --project src/Orders.Api
```

## Options

| Option | Choices | Default | What it changes |
|---|---|---|---|
| `--database` | `postgres`, `none` | `postgres` | PostgreSQL with EF Core 10, migrations, optimistic concurrency (`xmin`) and a readiness check. Local runs use an in-memory store, so no Docker is needed. |
| `--messaging` | `kafka`, `none` | `none` | Kafka producer and at-least-once consumer behind an outbox-friendly `IMessagePublisher`, with retries, a dead-letter topic and trace propagation. Local runs use an in-memory bus. |
| `--observability` | `otlp`, `appinsights`, `both` | `otlp` | Telemetry exporters. Each one switches on only when its endpoint (`OTEL_EXPORTER_OTLP_ENDPOINT`) or connection string (`APPLICATIONINSIGHTS_CONNECTION_STRING`) is set. |
| `--auth` | `jwt`, `none` | `jwt` | JWT bearer auth with scope policies and a secure-by-default fallback policy, or no auth for services behind an authenticating gateway. |

## Verify the template locally

```bash
eng/verify-combinations.sh
```

Packs the template, installs it into a throwaway hive and builds and unit-tests every option combination (24). Integration tests need Docker and run in CI.

## License

[MIT](LICENSE) © Mina Rotari
