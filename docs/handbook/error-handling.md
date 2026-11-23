# Errors and validation

Clients deserve errors they can act on; operators deserve errors they can find. Both get them from the same rules.

## Expected failures are values, unexpected ones are exceptions

- A business rule that can fail returns `Result<T>` with an `Error`: invalid currency, duplicate SKU, order in the
  wrong state, customer blocked, dependency unavailable. The caller decides what to do.
- Exceptions are for bugs and broken infrastructure. Do not catch `Exception` to turn it into a result; let it reach
  the global handler, which logs it once and answers `500`.
- Catch specific exceptions only where you can do something meaningful, for example a client mapping
  `HttpRequestException` and `BrokenCircuitException` to `CustomerStatus.Unavailable`.

## Validate in two places, for two reasons

| Where | What | Status | Example |
|---|---|---|---|
| Request records (DataAnnotations, built-in minimal API validation) | Shape: required, length, range, format | `400` with `errors` per field | `Currency` must match `^[A-Z]{3}$` |
| Domain (`Order.Place`, `order.Cancel`) | Invariants that hold no matter how the call arrives | `422` or `409` with an `errorCode` | Same SKU twice, cancelling a paid order |

The domain repeats some shape checks (a three-letter currency) because it is also reached from messages, where no
request validation ran. That duplication is deliberate.

## Every error response is problem details (RFC 9457)

Unhandled exceptions, status code pages, validation failures, rate limiting and `Error` results all produce the same
shape:

```json
{
  "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
  "title": "Request cannot be processed",
  "status": 422,
  "detail": "SKU 'SKU-1' appears on more than one line; combine the quantities instead.",
  "instance": "POST /v1/orders",
  "errorCode": "orders.duplicate_sku",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

- **`errorCode` is the contract.** Clients switch on it; it never changes once released. `title` and `detail` are
  for humans and may be reworded.
- **Codes are `<area>.<snake_case>`**: `orders.not_found`, `orders.invalid_state`, `request.malformed`,
  `rate_limited`, `concurrency_conflict`. All codes of a slice live in one `<Slice>Errors` class.
- **`traceId` connects the response to the logs and the trace.** Support asks for it; give it to them.
- **A `500` never leaks internals**: no exception message, no stack trace, `errorCode: internal_error`.

## Status codes we use

| Status | Meaning | Kind |
|---|---|---|
| `400` | The request is malformed or fails shape validation | Validation, binding |
| `401` / `403` | No valid token / token without the required scope | Auth |
| `404` | The resource does not exist | `ErrorKind.NotFound` |
| `409` | Conflicts with the current state, or a concurrent update won | `ErrorKind.Conflict`, `ConcurrencyConflictException` |
| `422` | Well-formed, but breaks a business rule | `ErrorKind.Unprocessable` |
| `429` | Rate limited; honour `Retry-After` | Rate limiter |
| `503` | A dependency is unavailable; safe to retry later | `ErrorKind.Unavailable` |

## Make retries safe

- State transitions are idempotent where they can be: cancelling a cancelled order returns it unchanged; a payment
  event delivered twice marks the order paid once.
- Message handlers must be idempotent: delivery is at least once. Check the current state before changing it, or
  keep the processed message ids.
- A failure that retrying cannot fix (a payment that does not match its order) is logged as a warning and
  acknowledged, not thrown, so it does not loop through retries into the dead-letter topic.
