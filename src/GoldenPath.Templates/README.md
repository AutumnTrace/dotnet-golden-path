# GoldenPath.Templates

`dotnet new` templates for production .NET 10 services.

```bash
dotnet new install GoldenPath.Templates.0.1.0.nupkg
dotnet new goldenpath-api -n Orders
```

The generated service comes with vertical-slice minimal APIs, ProblemDetails, API versioning,
Serilog, OpenTelemetry (OTLP and/or Azure Monitor), liveness and readiness probes, unit and API tests.
Run `dotnet new goldenpath-api --help` for the options.

Documentation, the engineering handbook and the changelog live at
https://github.com/AutumnTrace/dotnet-golden-path.
