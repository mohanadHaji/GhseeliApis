# Step 18 HTTP Test Plan - Lahza Payment Migration

Status: deterministic implementation passed; real Lahza test-mode and public
HTTPS execution pending.

The complete release matrix is maintained in
`STEP_18_LAHZA_EXHAUSTIVE_TEST_SCENARIOS.md`. The stable scenarios below are
the original core contract; the exhaustive catalog adds request permutations,
provider transport failures, mobile/WebView lifecycles, unknown provider
states, exact-byte attacks, migration variants, operations, and every
documented Lahza test-card outcome.

HTTP required: **Yes** - this change replaces the Stripe-specific customer
payment contract with Lahza hosted checkout, server-side verification, and
verified Lahza webhooks.

## Observable contract

Customer payment creation remains:

```http
POST /api/v1/payments/intents?language={ar|he}
Authorization: Bearer <customer JWT>
X-Device-Token: <device token>
Idempotency-Key: <bounded opaque key>
Content-Type: application/json

{ "bookingId": "<customer booking public reference>", "method": "Card" }
```

The request never accepts authoritative amount, currency, payment state,
provider reference, or customer identity. The Customer API initializes Lahza
from the immutable owned booking and returns client-safe hosted-checkout data:

- `provider = "Lahza"`
- `checkoutUrl`
- `providerReference`
- server-owned amount/currency
- local payment status and provider status

The response never exposes the Lahza secret key, raw provider response,
authorization code, card data, customer email, internal booking ID, or webhook
signature material.

Customer verification is:

```http
POST /api/v1/payments/{paymentId}/verify?language={ar|he}
Authorization: Bearer <customer JWT>
X-Device-Token: <device token>
```

Only the owning customer and owning device may verify. The server loads the
stored provider reference and calls Lahza verification. A callback redirect,
client claim, or HTTP 200 from Lahza is never sufficient: the nested
transaction status, reference, amount, and currency must all match before the
payment and booking are atomically marked paid.

The webhook endpoint is:

```http
POST /api/lahza/webhook
Content-Type: application/json
X-Lahza-Signature: <hex HMAC-SHA256 of exact raw body>
```

It is anonymous to JWT/device middleware, bounded to 65,536 bytes, verifies the
exact raw UTF-8 body with the configured Lahza secret key using constant-time
comparison, and durably deduplicates events before acknowledging them.

## Provider rules

- Lahza API base URL is `https://api.lahza.io`.
- Secret-key requests use HTTPS with certificate verification enabled.
- Supported booking currencies are exactly `ILS`, `JOD`, and `USD`.
- Amounts use checked two-decimal minor-unit conversion.
- The server generates a stable provider reference from the local payment ID
  using only Lahza-supported reference characters.
- Lahza does not document an idempotency header. The existing database lease,
  booking uniqueness, and deterministic reference remain the source of
  idempotency.
- If initialization has an ambiguous result, the payment is durably marked
  ambiguous and creation retries fail closed without another initialization.
  The owned verification endpoint may reconcile a transaction found under the
  stable reference; an unrecoverable checkout URL requires support handling.
- Redirect/WebView completion never delivers value without server verification.
- `charge.success` may mark paid only after reference, amount, and currency
  validation.
- `refund.pending` and `refund.processing` are durable non-final states.
- `refund.processed` marks a completed payment refunded.
- `refund.failed` cannot roll back a paid or already refunded payment.
- Duplicate and out-of-order webhook delivery is safe.

## Stable scenario matrix

| ID | Level | Scenario | Expected result |
|---|---|---|---|
| `STEP18-LAHZA-CONTRACT-001` | Contract | Payment response schema | Contains provider, checkout URL, and provider reference; Stripe client-secret/publishable-key fields are absent. |
| `STEP18-LAHZA-CONFIG-002` | Unit/TestServer | Missing or placeholder secret | Card capability disabled; creation returns `503 payment_provider_unavailable`; no provider call. |
| `STEP18-LAHZA-CONFIG-003` | Unit | Non-HTTPS base URL outside Development | Configuration validation fails closed. |
| `STEP18-LAHZA-INIT-004` | Unit/HTTP | Valid owned payable booking | Initializes once with immutable amount/currency, server email, stable reference, callback URL, card channel, and safe metadata. |
| `STEP18-LAHZA-INIT-005` | Unit | Forged client money/identity/provider fields | Ignored; provider receives only server-owned values. |
| `STEP18-LAHZA-INIT-006` | Unit/Relational | Same key replay | Same local payment/reference/checkout URL; no second accepted transaction. |
| `STEP18-LAHZA-INIT-007` | Relational | Concurrent keys for one booking | One logical payment and provider reference. |
| `STEP18-LAHZA-INIT-008` | Unit | Provider returns mismatched reference or invalid checkout URL | `502 payment_gateway_ambiguous`; booking remains unpaid. |
| `STEP18-LAHZA-INIT-009` | Unit/Relational | Transport fails with unknown acceptance | Ambiguous state is durable; stable identity retained; safe `503`; create retry does not initialize again. |
| `STEP18-LAHZA-INIT-010` | Unit/HTTP | Ambiguous initialization then owned verification finds transaction | Verification reconciles the existing transaction and no second initialization occurs. |
| `STEP18-LAHZA-INIT-011` | Unit/HTTP | Ambiguous initialization then verification reports pending/not found | Payment remains explicitly unresolved; no blind initialization retry occurs. |
| `STEP18-LAHZA-VERIFY-012` | HTTP | Owner verifies successful exact transaction | Payment and booking become completed/paid atomically. |
| `STEP18-LAHZA-VERIFY-013` | HTTP | Verify returns pending/failed | No paid state; local status follows legal transition rules. |
| `STEP18-LAHZA-VERIFY-014` | HTTP | Amount mismatch | Safe provider mismatch failure; no paid state. |
| `STEP18-LAHZA-VERIFY-015` | HTTP | Currency mismatch | Safe provider mismatch failure; no paid state. |
| `STEP18-LAHZA-VERIFY-016` | HTTP | Reference mismatch | Safe provider mismatch failure; no paid state. |
| `STEP18-LAHZA-VERIFY-017` | HTTP | Wrong user/device/unknown payment | Indistinguishable `404 payment_not_found`; no provider call. |
| `STEP18-LAHZA-WEBHOOK-018` | Unit/HTTP | Missing signature | `400 lahza_signature_missing`; no parsing or persistence. |
| `STEP18-LAHZA-WEBHOOK-019` | Unit/HTTP | Invalid signature | `400 lahza_signature_invalid`; no state mutation. |
| `STEP18-LAHZA-WEBHOOK-020` | Unit | Valid HMAC-SHA256 over exact raw body | Accepted using constant-time hexadecimal comparison. |
| `STEP18-LAHZA-WEBHOOK-021` | HTTP | Wrong media type or oversized body | Stable 415/413 before event processing. |
| `STEP18-LAHZA-WEBHOOK-022` | Relational | Duplicate identical event | One durable event record and one transition. |
| `STEP18-LAHZA-WEBHOOK-023` | Relational | Same event identity with changed body | `409 lahza_webhook_conflict`; no mutation. |
| `STEP18-LAHZA-WEBHOOK-024` | Relational | `charge.success` exact match | Payment completed and booking paid atomically. |
| `STEP18-LAHZA-WEBHOOK-025` | Relational | Unknown/mismatched transaction | Durably quarantined; no paid state. |
| `STEP18-LAHZA-REFUND-026` | Relational | pending/processing/processed lifecycle | Durable, duplicate-safe convergence to refunded only on processed. |
| `STEP18-LAHZA-REFUND-027` | Relational | Refund before charge success | Deferred then safely converged after matching success. |
| `STEP18-LAHZA-REFUND-028` | Relational | Failed or partial/mismatched refund | Does not incorrectly clear paid state. |
| `STEP18-LAHZA-SECURITY-029` | HTTP/logging | Provider error contains secret/PII/raw response | Stable safe problem and sanitized logs. |
| `STEP18-LAHZA-SWAGGER-030` | Contract | Customer Swagger | Lahza webhook and owned verification route documented; Stripe webhook absent. |
| `STEP18-LAHZA-MIGRATION-031` | Migration | Populated Stripe-shaped schema upgrade | Existing payment rows remain readable with neutral provider fields; old Stripe columns/table are removed or renamed safely. |
| `STEP18-LAHZA-TEST-032` | Real test mode | Documented successful test card | Hosted checkout completes; server verify and signed webhook converge exactly once. |
| `STEP18-LAHZA-TEST-033` | Real test mode | Insufficient funds / do-not-honour / authentication failure cards | No paid state; safe failed status and retry behavior. |
| `STEP18-LAHZA-HTTPS-034` | Production | Public webhook/callback over HTTPS | Deferred until HTTPS hosting is available; this scenario blocks live readiness but not local implementation. |

## Completion gate

- New Lahza unit, contract, TestServer, relational, migration, and HTTP-visible
  scenarios pass.
- All existing non-Stripe Customer and Business regressions pass.
- No Stripe runtime dependency, route, configuration, schema name, or secret
  remains.
- Test credentials are supplied only through user secrets/environment.
- Real Lahza test-mode success/failure evidence is recorded without exposing
  keys, customer data, authorization codes, or card details.
- Production webhook/callback activation remains explicitly deferred until a
  publicly trusted HTTPS endpoint exists.
