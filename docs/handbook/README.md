# Engineering handbook

How we build .NET services. The template in this repository is the executable version of this handbook: if a page
and the generated code disagree, one of them has a bug, so open an issue.

The pages are short on purpose. Each one states the rule, the default, and the reason, so you can disagree with the
reason in a pull request instead of guessing.

| Page | Read it when |
|---|---|
| [Onboarding](onboarding.md) | It is your first week |
| [Service anatomy](service-anatomy.md) | You open a generated service for the first time |
| [Logging, tracing and metrics](observability.md) | You add a log line, a span or a metric |
| [Errors and validation](error-handling.md) | You add an endpoint or a business rule |
| [Resilience](resilience.md) | You call another service, a database or a broker |
| [Testing](testing.md) | You write or review tests |
| [Code review](code-review.md) | You open or review a pull request |
| [Architecture decision records](adr.md) | You change a convention or make a choice others will inherit |

## Principles

1. **Boring by default.** Prefer the platform (ASP.NET Core, `Microsoft.Extensions.*`, OpenTelemetry) over
   libraries, and libraries over frameworks. Every dependency is something we patch for years.
2. **Fail fast and loudly at startup, degrade gracefully at runtime.** Bad configuration stops the process with
   a message that names the setting. A slow dependency produces a fast, typed error, not a hung request.
3. **Make the right thing the easy thing.** If a rule matters, the template does it for you and a test or analyzer
   enforces it. Rules that live only in documents are suggestions.
4. **Optimise for the reader.** Code is read in review and during incidents, usually by someone else, usually in a
   hurry. Names, small files and stable error codes beat cleverness.
5. **Decisions are written down.** If you had to think hard about it, the next person will too: write an ADR.

## The decisions behind the template

- [0001. Organise services as vertical slices](../adr/0001-vertical-slices.md)
- [0002. OpenTelemetry as the single telemetry API](../adr/0002-opentelemetry-single-telemetry-api.md)
- [0003. Chiseled, non-root runtime images](../adr/0003-chiseled-container-images.md)
- [0004. Resilience defaults](../adr/0004-resilience-defaults.md)
- [0005. Template option design](../adr/0005-template-option-design.md)
