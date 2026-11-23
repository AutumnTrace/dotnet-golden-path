# 0005. Few template options, each one a real architectural choice, all of them tested

- **Status:** Accepted
- **Date:** 2026-11-23

## Context

A service template is tempting to grow into a configurator with a switch for every library. Each switch doubles
the number of possible outputs, and the combinations nobody builds are the ones that rot: a missing `using`, a
dangling comma in `appsettings.json`, a test that only compiles when another option is on. A template that
generates broken code once loses the trust it exists to earn.

## Decision

- **Only four options**, each a decision a team genuinely makes per service and that changes the architecture:
  `--database postgres|none`, `--messaging kafka|none`, `--observability otlp|appinsights|both`,
  `--auth jwt|none`. Everything else (logging, problem details, versioning, health, rate limiting, resilience,
  shutdown, container, chart, CI) is the same for every service.
- **Defaults are the common production case**: PostgreSQL, no messaging, OTLP, JWT.
- **Every option also runs without its infrastructure.** PostgreSQL and Kafka have in-memory implementations
  behind the same interfaces, selected by configuration, so `dotnet run` and the API tests need no Docker. The
  real implementations are covered by Testcontainers integration tests.
- **Conditional content stays readable**: whole files are excluded in `template.json` where possible; inline
  `#if` blocks are kept short and never nested more than one level in C#.
- **Every combination is tested on every change**:
  - template tests instantiate all 24 combinations and check the output (no leftover markers or placeholder
    names, valid JSON, the solution lists exactly the generated projects, option-specific code present or absent);
  - CI builds all 24 with warnings as errors and runs their unit, API and architecture tests;
  - a pairwise-covering set of 6 combinations plus "everything on" also runs integration tests, builds the image,
    runs the compose stack end to end and validates the Helm chart;
  - the default output is deployed to a kind cluster.

## Consequences

- Adding an option is expensive on purpose: it touches `template.json`, the options model in the template tests,
  the CI matrix, the README options table and usually an ADR. The template tests fail until all of them agree.
- Teams that need something outside the options (a second database, gRPC) add it to their generated service.
  The handbook explains how, and if many teams do the same, it becomes a candidate option.
- The generated code carries a little indirection for local runs (an in-memory store behind `IOrderStore`, an
  in-memory bus behind `IMessagePublisher`). We consider that worth a fresh clone that runs with one command.

## Alternatives considered

- **Many fine-grained switches** (`--serilog`, `--versioning`, `--rate-limiting`, ...). Rejected: the cross-cutting
  pieces are the golden path; making them optional invites services without them.
- **Several separate templates** (one per stack). Less conditional content, but conventions drift between
  templates and fixes land in one and not the others.
- **Testing only the default combination.** Cheap, and exactly how broken combinations ship. Rejected.
