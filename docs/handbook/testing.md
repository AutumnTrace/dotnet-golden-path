# Testing

Tests exist so we can change code on a Friday. A test that is slow, flaky or tied to implementation details
works against that, so it gets fixed or deleted.

## The pyramid, as generated

| Layer | Project | Speed | Needs | What it proves |
|---|---|---|---|---|
| Unit | `*.UnitTests` | milliseconds | nothing | Domain rules, handlers with fakes, the resilience pipeline with a scripted handler, shutdown timing with `FakeTimeProvider` |
| API | `*.ApiTests` | ~1 s per class | nothing | The real app in memory: routing, binding, validation, auth, problem details, rate limiting, telemetry, the OpenAPI contract |
| Architecture | `*.ArchitectureTests` | < 1 s | nothing | Slice boundaries and dependency rules ([ADR 0001](../adr/0001-vertical-slices.md)) |
| Integration | `*.IntegrationTests` | seconds to a minute | Docker | Real PostgreSQL (migrations, jsonb, keyset paging, concurrency) and real Kafka (headers, consumer, dead-letter topic) |

Most tests are unit tests. API tests cover each endpoint's success path and each error code once. Integration tests
cover what only the real infrastructure can show you: SQL translation, migrations, broker semantics.

```bash
dotnet test --filter "Category!=Integration"   # everything that needs no Docker
dotnet test --filter "Category=Integration"    # Testcontainers
dotnet test                                     # all of it (CI)
```

## Rules

- **Name tests as sentences about behaviour**: `Cancelling_is_idempotent`, not `CancelTest2`. The list of test
  names should read like a specification.
- **Arrange, act, assert, separated by blank lines.** One behaviour per test; several asserts about that behaviour
  are fine.
- **No sleeping.** Time is `TimeProvider`; tests use `FakeTimeProvider` and advance it. When something is truly
  asynchronous (a message consumer), poll with a deadline, as `WaitForOrderAsync` does.
- **Do not mock what you do not own.** Fake `ICustomerDirectory` in handler tests; for the HTTP client itself, use
  a scripted `HttpMessageHandler` and the real registration. For the database, use Testcontainers, not an EF
  in-memory provider pretending to be PostgreSQL.
- **Pass `TestContext.Current.CancellationToken`** to everything that takes a token, so a cancelled run stops.
- **Assert on contracts, not incidental order.** UUIDv7 ids are time-ordered to the millisecond, so two orders
  placed in the same millisecond can come back in either order; assert the keyset contract instead.
- **Every integration test class carries `[Trait("Category", "Integration")]`.** The template tests check it.

## Contract snapshots

The OpenAPI document is compared to `Snapshots/openapi-v1.approved.json`. A failing snapshot means the public
contract changed: review the diff like any API change, then accept it with `UPDATE_SNAPSHOTS=1 dotnet test` and
commit the approved file. Breaking changes go into a new API version instead.

We use a small approval helper rather than Verify: since version 33, Verify fails the build unless every consuming
project declares a paid sponsorship or a licence exemption, and a template would pass that obligation on to every
service generated from it.

## Coverage

CI publishes a coverage summary for information. We do not gate on a percentage: a number is easy to reach with
tests that assert nothing. Review asks instead: is every business rule and every error code tested?
