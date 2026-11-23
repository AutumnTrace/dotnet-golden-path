# 0004. Resilience defaults for outbound calls, readiness and shutdown

- **Status:** Accepted
- **Date:** 2026-11-23

## Context

Most incidents in a service landscape are not crashes but slowness spreading: one dependency gets slow, callers
wait, their thread pools and connection pools fill, and soon everything is down. Retry storms make it worse. Teams
that configured Polly by hand ended up with different numbers in every service, some without timeouts at all.

Deployments caused the other kind of incident: pods killed while handling requests, and load balancers still
sending traffic to pods that were shutting down.

## Decision

**Outbound HTTP** goes through `AddStandardResilienceHandler()` (`Microsoft.Extensions.Http.Resilience`), with
every number in configuration (`<Client>:Resilience`) so it is visible in review and tunable per environment:

| Strategy | Default | Why |
|---|---|---|
| Total request timeout | 10 s | An upper bound for the caller, including all retries |
| Retry | up to 3 retries, exponential backoff from 200 ms, with jitter | Rides out blips; jitter prevents synchronised retry waves |
| Circuit breaker | opens at 20 % failures over ≥ 20 calls in 30 s, for 15 s | Stops hammering a struggling dependency and fails fast meanwhile |
| Attempt timeout | 2 s | No single attempt can eat the whole budget |

Retries are limited to safe methods (`DisableForUnsafeHttpMethods()`): the standard pipeline would otherwise retry a
`POST` too. Code that must retry a non-idempotent call sends an idempotency key and opts in explicitly. When the
pipeline gives up, the caller gets a typed result (`CustomerStatus.Unavailable`) and the API answers `503` with a
stable error code, never a hung request.

**Readiness** checks only this service's own dependencies (its database, its broker). It never checks other
services: if the customer directory is down, taking all our pods out of rotation would turn a partial outage into a
full one.

**Shutdown** is ordered: on SIGTERM readiness fails immediately, the process keeps serving for
`Shutdown:DrainDelay` (5 s) while endpoints are removed, then has `Shutdown:Timeout` (25 s) to finish in-flight
work. The pod's `terminationGracePeriodSeconds` (35 s) is longer than both.

**Inbound** requests are rate limited per client (token subject, or address for anonymous callers), 100 requests
per second by default, with `429` and `Retry-After`.

**Messaging** consumers retry a failing message three times with backoff, then park it on `<topic>.dlq` with the
reason. A poison message never blocks a partition.

## Consequences

- Every service behaves the same under dependency failure, which makes incidents easier to reason about.
- The numbers are defaults, not laws. Changing them is a configuration change reviewed like code; the unit tests
  pin the behaviour (retry, give up, time out, open circuit) with test-sized timings.
- A 5 s drain delay adds 5 s to every rolling update per pod. That is the price of zero dropped requests.

## Alternatives considered

- **Hand-written Polly pipelines per client.** Flexible, but every team re-derived the same pipeline with
  different mistakes. The standard handler encodes the ordering (timeouts inside and outside retries) correctly.
- **Retries in the service mesh only.** Not every cluster has a mesh, and the mesh cannot know which calls are
  idempotent or how to degrade the response.
- **A `preStop: sleep` hook instead of an in-process drain delay.** Chiseled images have no `sleep` binary, and the
  in-process drain also works outside Kubernetes. The native `sleep` lifecycle action is an option once all
  clusters run Kubernetes 1.30+.
