# 0001. Organise services as vertical slices

- **Status:** Accepted
- **Date:** 2026-11-23

## Context

Most of our services are small: a handful of use cases around one business capability, owned by one team. The
default layered layout (`Controllers/`, `Services/`, `Repositories/`, `Models/`) spreads every change across four
folders and invites "shared" services that every feature depends on. After a year, deleting a feature is
impossible because nobody knows what else uses its pieces.

We also want the structure to be checked by the build, not by memory, because the template will be copied into
services we no longer look at every day.

## Decision

Generated services are organised by feature, not by layer:

- `Features/<Slice>/` holds everything one capability needs: request/response contracts, handlers, endpoint
  mapping, the domain model (`Domain/`), persistence adapters (`Persistence/`) and message contracts. One use case
  is usually one file (`PlaceOrder.cs`: request, handler, endpoint).
- `Infrastructure/` holds cross-cutting building blocks every slice uses the same way (problem details,
  telemetry, health, rate limiting, persistence plumbing, messaging transport). It never references a slice.
- `Integrations/` holds clients for other services. They describe the other service, not how a slice uses it.
- Handlers are plain classes with constructor injection. There is no mediator library: a minimal API endpoint
  calls its handler directly, and a message handler is resolved by the dispatcher.

The rules are enforced by `*.ArchitectureTests` (ArchUnitNET): slices do not reference each other,
`Infrastructure` and `Integrations` never depend on `Features`, domain code depends only on the base library and
the shared error types, handlers and endpoints are internal, and EF Core, Kafka and `HttpClient` are used only from
the namespaces meant for them.

## Consequences

- A feature can be read, changed and deleted in one folder. Code review diffs stay local.
- Some duplication between slices is accepted (two slices may each have a small DTO). We prefer that to coupling.
- Slices that need each other's data talk through messages or a read model, never through each other's types.
  This is a constraint new engineers notice early; the architecture test failure message says what to do.
- Without a mediator there are no pipeline behaviours. Cross-cutting concerns live in middleware, endpoint
  filters, `IExceptionHandler` and the resilience pipeline instead, which covers what we needed.

## Alternatives considered

- **Layered (Controllers/Services/Repositories).** Familiar, but changes scatter and shared services grow without
  bound. Rejected.
- **Clean Architecture with one project per layer.** Good boundaries, but four projects and their ceremony for a
  service with five use cases is more structure than behaviour. The architecture tests give us the boundaries
  we care about inside one project.
- **MediatR for dispatch.** Adds indirection (and, since 2025, a commercial licence for new versions) without
  solving a problem we have. Handlers are called directly.
