# dotnet-golden-path

**A `dotnet new` template for production .NET 10 services: OpenTelemetry, Serilog, health probes and tests, ready on day one.**

[![CI](https://github.com/AutumnTrace/dotnet-golden-path/actions/workflows/ci.yml/badge.svg)](https://github.com/AutumnTrace/dotnet-golden-path/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> [!NOTE]
> Work in progress. Persistence, messaging, auth, containers and the Helm chart land in the next releases.

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
| `--observability` | `otlp`, `appinsights`, `both` | `otlp` | Telemetry exporters. Each one switches on only when its endpoint (`OTEL_EXPORTER_OTLP_ENDPOINT`) or connection string (`APPLICATIONINSIGHTS_CONNECTION_STRING`) is set. |

## Verify the template locally

```bash
eng/verify-combinations.sh
```

Packs the template, installs it into a throwaway hive and builds and unit-tests every option combination.

## License

[MIT](LICENSE) © Mina Rotari
