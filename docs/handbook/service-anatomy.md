# Service anatomy

What a generated service contains and where new code goes. The structure is enforced by the architecture tests;
the reasoning is in [ADR 0001](../adr/0001-vertical-slices.md).

```
src/<Name>.Api/
  Program.cs                  composition root: one line per building block and per slice
  Features/<Slice>/           one business capability
    Domain/                   aggregate, value objects, errors; no HTTP, no EF Core, no Kafka
    PlaceOrder.cs             one use case: request contract, handler, endpoint mapping
    <Slice>Module.cs          AddXxxFeature() and MapXxxEndpoints()
    Persistence/              EF Core mapping and store (--database postgres)
  Infrastructure/             shared building blocks: errors, observability, health, rate limiting,
                              lifecycle, security, persistence and messaging plumbing
  Integrations/<Service>/     typed clients for other services, behind an interface
tests/
  <Name>.UnitTests            domain rules, handlers with fakes, resilience pipeline, shutdown
  <Name>.ApiTests             the real app in memory (WebApplicationFactory), contract snapshot
  <Name>.ArchitectureTests    the rules on this page
  <Name>.IntegrationTests     Testcontainers: real PostgreSQL and Kafka (Category=Integration)
  <Name>.Testing              shared test kit: host factory, tokens, API helpers
deploy/helm/<name>/           the chart
```

## Where does my code go?

| You are adding | Put it in | Notes |
|---|---|---|
| A new endpoint for an existing capability | `Features/<Slice>/<UseCase>.cs` | Request record, internal handler, internal endpoint class |
| A business rule | `Features/<Slice>/Domain/` | Return an `Error` from `<Slice>Errors`, do not throw |
| A new capability | `Features/<NewSlice>/` with its own module | One line in `Program.cs`; it may not reference other slices |
| A call to another service | `Integrations/<Service>/` | Interface, typed client, resilience handler, stub for local runs |
| A message handler | `Features/<Slice>/` | `IMessageHandler<T>`, registered with `AddMessageHandler<T, THandler>(topic)` |
| Something every slice needs | `Infrastructure/` | Only if at least two slices need it; it may not reference slices |
| A configuration section | Next to the code that uses it | Options class with DataAnnotations, `ValidateOnStart()` |

## Rules of thumb

- **Handlers are plain classes.** Constructor injection, one public `HandleAsync`, returns `Result<T>`. The endpoint
  maps the result to HTTP; the handler never sees `HttpContext`.
- **Slices do not share entities.** If two slices need the same data, one owns it and publishes events; the other
  keeps the projection it needs.
- **The domain does not know about time zones, clocks or ids from the outside world.** Pass `DateTimeOffset now`
  (from `TimeProvider`) into domain methods.
- **Money is `long` minor units plus an ISO 4217 currency.** Never `double`, never a decimal without a currency.
- **Ids are UUIDv7** (`Guid.CreateVersion7`): unique without coordination and ordered by creation time to the
  millisecond, so they make good clustered keys and keyset cursors.
