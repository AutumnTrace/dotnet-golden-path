# Resilience

Assume every dependency is slow or down right now, and decide what your service does about it. The defaults below
are justified in [ADR 0004](../adr/0004-resilience-defaults.md); this page says how to use them.

## Calling another service

1. Put the client in `Integrations/<Service>/` behind an interface that speaks the domain's language
   (`ICustomerDirectory.GetStatusAsync`), not HTTP.
2. Register it as a typed `HttpClient` with `AddStandardResilienceHandler()` and bind the numbers from
   configuration, like `CustomerDirectory:Resilience`. Never write your own retry loop.
3. Map the failure modes to a value the caller can handle (`Unavailable`), and log them once as a warning.
4. Add a stub at the HTTP boundary for local runs (`UseStub`), so the resilience pipeline still runs on a laptop.
5. Test it the way `CustomerDirectoryClientTests` does: a scripted handler that fails, hangs or recovers, through
   the real registration.

## The budget

A request that arrives at our edge has a budget, set by our callers (often 15 to 30 seconds end to end). Every hop
spends some of it. Our defaults keep one outbound call under 10 seconds, including retries, and any single attempt
under 2 seconds. If you need more, you probably need a different design (async processing, a cache), not a bigger
timeout.

| Knob | Default | Turn it up when | Turn it down when |
|---|---|---|---|
| `AttemptTimeout` | 2 s | The dependency is known to be slow but healthy (reports, search) | p99 of the dependency is far below 1 s |
| `TotalRequestTimeout` | 10 s | Rarely; prefer async | The caller has a tight SLO |
| `Retry.MaxRetryAttempts` | 3 | Almost never | The dependency is expensive or rate limited |
| `CircuitBreaker.BreakDuration` | 15 s | The dependency takes long to recover | It recovers quickly and failures are random |

## What not to retry

- Non-idempotent calls without an idempotency key. The template disables retries for unsafe methods
  (`DisableForUnsafeHttpMethods()`); turn them back on per call only with a key.
- `4xx` responses other than `408` and `429`: the request is wrong, sending it again will not fix it.
- Business failures from a message handler: return, log a warning, let the message be acknowledged.

## Health checks

- `/health/live` answers as long as the process can serve a request. It has no checks: restarting a pod because
  the database is down only makes things worse.
- `/health/ready` checks only *our* dependencies (database, broker) and the shutdown state. Never another service.
- Health responses never include exception messages; they can contain hosts and connection strings.

## Shutdown and deploys

On SIGTERM the service fails readiness at once, keeps serving for `Shutdown:DrainDelay` (5 s) while the endpoint is
removed from load balancers, then gets `Shutdown:Timeout` (25 s) for in-flight work. Kafka consumers close cleanly:
stored offsets are committed and the consumer leaves its group, so partitions move to other pods immediately.
Keep `terminationGracePeriodSeconds` above the sum.

## Overload

Per-client rate limiting (100 requests per second by default) protects the service from one noisy caller. It is a
safety net, not capacity planning: if legitimate traffic hits it, scale out (the HPA does) or talk to the caller.
