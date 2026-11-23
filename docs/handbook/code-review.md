# Code review

Review is how we share knowledge and keep the codebase ours rather than one person's. Aim to review within one
working day; small pull requests get reviewed faster, so keep them small.

## For authors

- One concern per pull request. A refactor and a behaviour change are two pull requests.
- Fill in the template: **Summary** (why), **Changes** (what), **Testing** (what you actually ran).
- Review your own diff first. Most nits are cheaper to fix before anyone else sees them.
- Link the ADR if you change a convention, or write one.

## For reviewers: the checklist

**Correctness**
- [ ] Does it do what the summary says, including the unhappy paths?
- [ ] Are new business rules in the domain, returning an `Error` with a new, stable `errorCode`?
- [ ] Are message handlers and state transitions idempotent?
- [ ] Money: `long` minor units with an ISO 4217 currency? Overflow handled (`checked`)?

**API**
- [ ] Did the OpenAPI snapshot change? Is that change backwards compatible, or is it a new version?
- [ ] Are request shapes validated with DataAnnotations and does every error path return problem details?
- [ ] Does every new endpoint have an authorization policy (or a deliberate `AllowAnonymous`)?

**Resilience and operations**
- [ ] Do outbound calls go through a typed client with the resilience handler? Any new timeouts justified?
- [ ] New configuration: an options class validated on start, a default in `appsettings.json`, a value in the Helm
      chart if it differs per environment, secrets only via `existingSecret`?
- [ ] Database changes: a migration, an index for new queries, no `OFFSET` paging on large tables?
- [ ] Readiness still checks only this service's own dependencies?

**Observability and security**
- [ ] Logs use templates and `LoggerMessage` on hot paths, at the right level, with no secrets or personal data?
- [ ] Business ids tagged on the current span; any new metric has low-cardinality tags?
- [ ] Input from outside is validated; no secrets in code, config files or logs?

**Tests**
- [ ] A test for each rule and each error code; names read as behaviour?
- [ ] No sleeps, no shared state between tests, `TimeProvider` for time?
- [ ] Integration tests tagged `Category=Integration`?

## Tone

Comment on the code, not the person. Say why, and say how strongly you feel: prefix optional remarks with
**nit:**, ask questions when you are unsure (**question:**), and approve with comments when the remaining points are
small. If a thread goes back and forth twice, talk instead and write the outcome in the thread.
