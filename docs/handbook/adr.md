# Architecture decision records

An ADR is a short document that records one decision, the context that forced it, and what it costs. We keep them
in `docs/adr/` next to the code they govern, numbered in order, and never delete them: a reversed decision gets a
new ADR that supersedes the old one.

## When to write one

Write an ADR when a choice is hard to reverse, affects more than one team or service, or when you catch yourself
explaining the same reasoning in a second code review. Typical triggers: a new dependency, a new option in the
template, a change to a default (timeouts, retries, log levels), a data store or messaging pattern, a security
boundary.

You do not need one for choices that a pull request can explain and a later pull request can undo cheaply.

## How to write one

1. Copy [`docs/adr/template.md`](../adr/template.md) to `docs/adr/NNNN-short-title.md` with the next number.
2. **Title**: the decision, in the imperative. "Use keyset paging for order lists", not "Paging".
3. **Context**: the forces, in facts. What hurts, what constraints exist, what we measured. Someone reading it in
   two years must understand why it was a question.
4. **Decision**: specific enough that a reviewer can check a pull request against it. Include the numbers.
5. **Consequences**: the good and the bad. An ADR without downsides has not been thought through.
6. **Alternatives**: each serious option and why it lost, in a line or two.
7. Open a pull request with status *Proposed*. Discussion happens in the review; the ADR changes to *Accepted*
   when it merges.

Keep it to one page. If it needs more, it is probably two decisions.

## Example

[0004. Resilience defaults](../adr/0004-resilience-defaults.md) is a good model: it starts from the incidents that
motivated it, puts every number in a table with a reason, and lists what the decision costs (a 5-second drain on
every rolling update).
