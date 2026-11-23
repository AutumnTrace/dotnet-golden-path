# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `goldenpath-api` template packaged as `GoldenPath.Templates`
- Generated service: vertical-slice minimal APIs with a sample Orders slice, built-in request validation, RFC 9457 problem details with stable error codes, URL-segment API versioning and per-version OpenAPI documents
- Serilog console logging (JSON or text) bridged into OpenTelemetry; traces, metrics and logs with OTLP and/or Azure Monitor exporters (`--observability`)
- `/health/live` and `/health/ready` probes
- Unit and API tests in the generated service; template tests and a CI matrix in this repository

[Unreleased]: https://github.com/AutumnTrace/dotnet-golden-path/commits/main
