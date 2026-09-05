# Step 18 Lahza Exhaustive Test Scenarios

Status: **scenario design complete; deterministic subset passed; Lahza test-mode
execution pending credentials**

This catalog extends the core Step 18 HTTP plan. It is the release checklist
for Lahza and must not be reduced to only a successful card payment.
It defines **227 unique scenarios**.

## Execution levels

| Level | Meaning |
|---|---|
| Unit | Isolated validation, mapping, signature, money, or transition behavior |
| Contract | Exact Lahza request/response and public Ghseeli HTTP shape |
| TestServer | In-process Customer API HTTP behavior |
| Relational | Real SQL Server constraints, transactions, races, and recovery |
| Safe local | Local Customer API plus deterministic provider; no Lahza traffic |
| Lahza test | Real Lahza test-mode API and hosted checkout |
| Public HTTPS | Real Lahza callback/webhook delivery to a trusted HTTPS endpoint |
| Manual mobile | Real Android/iOS WebView lifecycle |

## Evidence and data rules

- Never place a Lahza secret, dashboard credential, card number, CVV, expiry,
  authorization code, raw provider response, or customer email in committed
  files, screenshots, console transcripts, logs, or test results.
- Test card values must be read directly from Lahza's test-payment
  documentation at execution time.
- Record only the scenario ID, UTC time, pass/fail, local payment ID, masked or
  hashed provider reference, final local state, final provider state, and a
  sanitized failure reason.
- Use a new disposable booking for every independent card outcome.
- Never run these scenarios with a live Lahza key or against production.
- A callback visit, WebView close, HTTP 200 envelope, or webhook receipt alone
  never proves payment success. Server verification must match reference,
  amount, currency, and transaction identity.

## A. Configuration and release safety

| ID | Level | Scenario | Expected |
|---|---|---|---|
| `STEP18-EXT-CONFIG-001` | Unit/TestServer | Secret absent | Card capability disabled; create returns safe 503; no payment/provider call. |
| `STEP18-EXT-CONFIG-002` | Unit | Whitespace secret | Treated as absent. |
| `STEP18-EXT-CONFIG-003` | Unit | Placeholder-like secret | Disabled without logging the value. |
| `STEP18-EXT-CONFIG-004` | Unit | Valid key with undocumented prefix | Accepted; implementation does not require `sk_test_` or `sk_live_`. |
| `STEP18-EXT-CONFIG-005` | Unit | HTTP base URL | Startup/configuration fails closed. |
| `STEP18-EXT-CONFIG-006` | Unit | Relative or malformed base URL | Startup/configuration fails closed. |
| `STEP18-EXT-CONFIG-007` | Unit | HTTP callback URL | Startup/configuration fails closed. |
| `STEP18-EXT-CONFIG-008` | Unit | Relative or malformed callback URL | Startup/configuration fails closed. |
| `STEP18-EXT-CONFIG-009` | Unit | Empty callback URL | Initialization omits `callback_url`. |
| `STEP18-EXT-CONFIG-010` | Unit | Timeout below 1 second or above 60 seconds | Startup/configuration fails closed. |
| `STEP18-EXT-CONFIG-011` | Unit | Timeout boundary values 1 and 60 | Accepted exactly. |
| `STEP18-EXT-CONFIG-012` | Deployment | Workflow secret absent | Deployment succeeds with card disabled. |
| `STEP18-EXT-CONFIG-013` | Deployment | Workflow secret present | Secret is runtime-only and absent from retained artifacts. |
| `STEP18-EXT-CONFIG-014` | Deployment | Test key accidentally selected for production | Release gate blocks until test/live mode is independently confirmed. |
| `STEP18-EXT-CONFIG-015` | Deployment | Live key supplied to test runner | Runner refuses execution; no transaction initialized. |
| `STEP18-EXT-CONFIG-016` | Unit/TestServer | Configuration reload removes secret | New payment creation becomes unavailable without corrupting existing rows. |
| `STEP18-EXT-CONFIG-017` | Unit/TestServer | Configuration reload changes timeout | New gateway calls use the bounded updated timeout. |
| `STEP18-EXT-CONFIG-018` | Security | Secret rotation | Old webhook signatures fail after cutover; new signatures pass; rotation procedure prevents an unsafe gap. |

## B. Customer payment request boundary

| ID | Level | Scenario | Expected |
|---|---|---|---|
| `STEP18-EXT-CREATE-001` | TestServer | Missing JWT | 401; no payment or provider call. |
| `STEP18-EXT-CREATE-002` | TestServer | Invalid/expired JWT | 401; no side effect. |
| `STEP18-EXT-CREATE-003` | TestServer | Wrong role | 403; no side effect. |
| `STEP18-EXT-CREATE-004` | TestServer | Missing device token | 401 with stable device error. |
| `STEP18-EXT-CREATE-005` | TestServer | Invalid, expired, inactive, or rotated device token | Rejected; no side effect. |
| `STEP18-EXT-CREATE-006` | TestServer | Wrong content type | 415 before body processing. |
| `STEP18-EXT-CREATE-007` | TestServer | Empty, `null`, or malformed JSON | Stable 400. |
| `STEP18-EXT-CREATE-008` | TestServer | Body exactly at limit | Processed normally. |
| `STEP18-EXT-CREATE-009` | TestServer | Body above limit, including chunked body | 413 before persistence/provider call. |
| `STEP18-EXT-CREATE-010` | TestServer | Empty booking ID | Field-level 400. |
| `STEP18-EXT-CREATE-011` | TestServer | Unknown booking | Non-disclosing 404. |
| `STEP18-EXT-CREATE-012` | TestServer | Booking owned by another user | Same 404 as unknown. |
| `STEP18-EXT-CREATE-013` | TestServer | Booking owned by another device | Same 404 as unknown. |
| `STEP18-EXT-CREATE-014` | Unit/Relational | Pending booking | Eligible when all other rules pass. |
| `STEP18-EXT-CREATE-015` | Unit/Relational | Confirmed booking | Eligible when all other rules pass. |
| `STEP18-EXT-CREATE-016` | Unit/Relational | In-progress, completed, cancelled, or no-show booking | 409; no provider call. |
| `STEP18-EXT-CREATE-017` | Unit/Relational | Already paid or refunded booking | 409; no new payment. |
| `STEP18-EXT-CREATE-018` | Unit | Missing customer email | 409; lease released; no provider call. |
| `STEP18-EXT-CREATE-019` | Unit | `Card` casing variations | Canonicalized to Card. |
| `STEP18-EXT-CREATE-020` | TestServer | Unknown method | 400. |
| `STEP18-EXT-CREATE-021` | TestServer | Wallet, cash-on-arrival, or third-party | Explicit 409 not-yet-supported. |
| `STEP18-EXT-CREATE-022` | Contract | Forged amount, currency, status, provider, reference, email, user, or transaction fields | Ignored by input binding; server-owned values used. |
| `STEP18-EXT-CREATE-023` | Unit | ILS amount conversion | Exact checked minor units. |
| `STEP18-EXT-CREATE-024` | Unit | JOD amount conversion | Exact checked minor units. |
| `STEP18-EXT-CREATE-025` | Unit | USD amount conversion | Exact checked minor units. |
| `STEP18-EXT-CREATE-026` | Unit | EUR or unsupported currency for new Lahza payment | 409 before provider call. |
| `STEP18-EXT-CREATE-027` | Unit | Zero, negative, or more than two decimal places | 409 before provider call. |
| `STEP18-EXT-CREATE-028` | Unit | Minor-unit overflow | Safe 409; no overflow or provider call. |
| `STEP18-EXT-CREATE-029` | TestServer | Missing idempotency header | Stable required-header 400. |
| `STEP18-EXT-CREATE-030` | TestServer | Blank, repeated, whitespace, control-character, or overlong key | Stable invalid-key 400. |
| `STEP18-EXT-CREATE-031` | Unit/Relational | Same key and canonical request replay | Same payment, reference, and checkout URL. |
| `STEP18-EXT-CREATE-032` | Unit/Relational | Same key with changed booking/method | 409 idempotency conflict. |
| `STEP18-EXT-CREATE-033` | Relational | Different keys for one booking | One logical payment; both keys associate with it. |
| `STEP18-EXT-CREATE-034` | Relational | Same key across different users/devices | Correctly isolated scopes. |
| `STEP18-EXT-CREATE-035` | Relational | Parallel same-key requests | One provider initialization and one logical payment. |
| `STEP18-EXT-CREATE-036` | Relational | Parallel different-key requests for one booking | One logical payment and stable provider reference. |
| `STEP18-EXT-CREATE-037` | Relational | SQL commit acknowledgement lost before provider call | Persisted identity is recovered; no duplicate payment. |
| `STEP18-EXT-CREATE-038` | Relational | SQL failure before local payment commit | No provider call and no partial row. |

## C. Lahza initialization contract and transport

| ID | Level | Scenario | Expected |
|---|---|---|---|
| `STEP18-EXT-INIT-001` | Contract | Request method and path | POST `/transaction/initialize` on configured HTTPS base URL. |
| `STEP18-EXT-INIT-002` | Contract | Authorization | Bearer secret sent only in the Authorization header. |
| `STEP18-EXT-INIT-003` | Contract | Content type and accept | JSON request and JSON response negotiation. |
| `STEP18-EXT-INIT-004` | Contract | Amount | Decimal string containing exact server-owned minor units. |
| `STEP18-EXT-INIT-005` | Contract | Email | Server-owned account email; never accepted from client. |
| `STEP18-EXT-INIT-006` | Contract | Currency | Exact uppercase ILS, JOD, or USD. |
| `STEP18-EXT-INIT-007` | Contract | Reference | Stable `GHSEELI-` reference using only documented characters. |
| `STEP18-EXT-INIT-008` | Contract | Channels | Exactly `["card"]`; unrelated checkout channels are unavailable. |
| `STEP18-EXT-INIT-009` | Contract | Metadata | Stringified minimal JSON with local IDs only; no PII or secrets. |
| `STEP18-EXT-INIT-010` | Contract | Callback configured | Exact validated HTTPS callback included. |
| `STEP18-EXT-INIT-011` | Contract | Callback absent | Field omitted rather than blank or null authority. |
| `STEP18-EXT-INIT-012` | Unit | HTTP 200/201 with `status=true` and valid data | HTTPS checkout URL and exact reference accepted. |
| `STEP18-EXT-INIT-013` | Unit | HTTP success with `status=false` | Safe provider-contract failure. |
| `STEP18-EXT-INIT-014` | Unit | Missing/null data | Safe provider-contract failure. |
| `STEP18-EXT-INIT-015` | Unit | Invalid JSON or wrong field types | Safe provider-contract failure. |
| `STEP18-EXT-INIT-016` | Unit | Missing or mismatched reference | Safe 502; booking unpaid. |
| `STEP18-EXT-INIT-017` | Unit | Missing, relative, HTTP, or malformed authorization URL | Safe 502; URL not returned. |
| `STEP18-EXT-INIT-018` | Unit | Oversized provider response | Bounded failure without memory exhaustion. |
| `STEP18-EXT-INIT-019` | Unit | 400/401/403 provider response | Safe failure; provider body and secret not exposed. |
| `STEP18-EXT-INIT-020` | Unit | 404/409 duplicate-reference response | No blind new reference; payment becomes explicitly unresolved unless verified. |
| `STEP18-EXT-INIT-021` | Unit | 429 response | Safe retryable failure; no tight retry loop. |
| `STEP18-EXT-INIT-022` | Unit | 5xx response | Safe ambiguous failure. |
| `STEP18-EXT-INIT-023` | Unit/Relational | DNS, TLS, connection reset, or timeout | Durable Ambiguous state; lease released; no second initialization on replay. |
| `STEP18-EXT-INIT-024` | Unit | Caller cancellation before network acceptance | Cancellation propagated and lease safely released to NotStarted. |
| `STEP18-EXT-INIT-025` | Relational | Stale lease owner completes after winner | Cannot overwrite the winner. |
| `STEP18-EXT-INIT-026` | Relational | Lease expiry and reclaim before provider call | One current owner proceeds. |
| `STEP18-EXT-INIT-027` | Relational | Provider succeeds but SQL acknowledgement is lost | Stored checkout wins when committed; otherwise Ambiguous prevents duplicate provider creation. |
| `STEP18-EXT-INIT-028` | Contract | Access code returned by Lahza | Not persisted, returned, or logged. |

## D. Hosted checkout and mobile WebView

| ID | Level | Scenario | Expected |
|---|---|---|---|
| `STEP18-EXT-WEBVIEW-001` | Contract | API response | Only HTTPS checkout URL and provider reference are client-usable provider fields. |
| `STEP18-EXT-WEBVIEW-002` | Manual mobile | Open checkout | Loads Lahza-hosted page; Ghseeli never receives card fields. |
| `STEP18-EXT-WEBVIEW-003` | Manual mobile | Redirects enabled | Bank and 3DS redirects remain functional. |
| `STEP18-EXT-WEBVIEW-004` | Manual mobile | JavaScript/DOM storage requirements | Checkout completes on supported Android and iOS WebViews. |
| `STEP18-EXT-WEBVIEW-005` | Manual mobile | User closes before payment | Booking remains unpaid; later verification is non-success. |
| `STEP18-EXT-WEBVIEW-006` | Manual mobile | App backgrounded/killed during checkout | Relaunch can read local payment and verify safely. |
| `STEP18-EXT-WEBVIEW-007` | Manual mobile | Device loses network after card submission | Reconnect and verify; no second logical payment. |
| `STEP18-EXT-WEBVIEW-008` | Manual mobile | Callback redirect reached | App closes/continues checkout flow but does not mark paid locally. |
| `STEP18-EXT-WEBVIEW-009` | Manual mobile | Lahza close URL reached | WebView closes; server verification decides state. |
| `STEP18-EXT-WEBVIEW-010` | Manual mobile | 3DS page does not auto-close | App handles documented close/callback navigation safely. |
| `STEP18-EXT-WEBVIEW-011` | Manual mobile | Back, refresh, and repeated pay taps | No duplicate local payment; same checkout identity is reused. |
| `STEP18-EXT-WEBVIEW-012` | Security | Navigation to unrelated URL | Mobile client applies an explicit trusted-host/callback policy and does not expose tokens. |
| `STEP18-EXT-WEBVIEW-013` | Manual mobile | Checkout URL opened on another device | Server ownership still controls read/verify; no cross-device data disclosure. |
| `STEP18-EXT-WEBVIEW-014` | Manual mobile | Checkout URL expires or becomes unusable | Payment remains unpaid/unresolved and support-safe; no blind provider reinitialization. |

## E. Owned server verification

| ID | Level | Scenario | Expected |
|---|---|---|---|
| `STEP18-EXT-VERIFY-001` | TestServer | Missing/invalid JWT or device | Rejected before Lahza call. |
| `STEP18-EXT-VERIFY-002` | TestServer | Wrong user, wrong device, or unknown local payment | Identical 404; no Lahza call. |
| `STEP18-EXT-VERIFY-003` | TestServer | Client sends reference, amount, currency, status, or transaction ID | Ignored; stored local identity is authoritative. |
| `STEP18-EXT-VERIFY-004` | Contract | Request method/path | GET `/transaction/verify/{stored-reference}` with Bearer secret. |
| `STEP18-EXT-VERIFY-005` | Unit | Envelope true and nested status success | Continue only after all identity/money checks pass. |
| `STEP18-EXT-VERIFY-006` | Unit | Envelope true but nested pending/processing | Remain unpaid and nonterminal. |
| `STEP18-EXT-VERIFY-007` | Unit | Nested failed | Pending becomes Failed; paid/refunded state never regresses. |
| `STEP18-EXT-VERIFY-008` | Unit | Nested abandoned | Pending becomes Failed; safe retry policy remains explicit. |
| `STEP18-EXT-VERIFY-009` | Unit | Unknown or undocumented status, including reversed | No paid transition; raw bounded status retained safely. |
| `STEP18-EXT-VERIFY-010` | Unit | Envelope status false | Never interpreted as transaction success. |
| `STEP18-EXT-VERIFY-011` | Unit | Reference mismatch | 502 ambiguous; no mutation. |
| `STEP18-EXT-VERIFY-012` | Unit | Amount mismatch | 502 ambiguous; no mutation. |
| `STEP18-EXT-VERIFY-013` | Unit | Currency mismatch or casing anomaly | Exact canonical comparison; mismatch fails safely. |
| `STEP18-EXT-VERIFY-014` | Unit | Existing transaction ID differs | 502 ambiguous; no overwrite. |
| `STEP18-EXT-VERIFY-015` | Unit | First valid transaction ID | Stored once and reused as an invariant. |
| `STEP18-EXT-VERIFY-016` | Unit | Missing status/reference/currency or malformed JSON | Safe provider-contract failure. |
| `STEP18-EXT-VERIFY-017` | Unit | Oversized response | Bounded failure. |
| `STEP18-EXT-VERIFY-018` | Unit | 401/403/404/429/5xx, timeout, DNS, or TLS failure | Safe problem; no false paid state. |
| `STEP18-EXT-VERIFY-019` | Relational | Repeated successful verification | Idempotent Completed state and one fulfillment. |
| `STEP18-EXT-VERIFY-020` | Relational | Concurrent successful verifications | Converges atomically without 5xx or duplicate value. |
| `STEP18-EXT-VERIFY-021` | Relational | Verification races `charge.success` webhook | Both paths converge to one Completed payment. |
| `STEP18-EXT-VERIFY-022` | Relational | Verification races full refund webhook | Legal final state is preserved; no paid-after-refund regression. |
| `STEP18-EXT-VERIFY-023` | Unit/TestServer | Legacy Stripe payment verification | Rejected as provider-unavailable; legacy row remains readable. |
| `STEP18-EXT-VERIFY-024` | Unit/Relational | Ambiguous initialization later verifies success | Payment completes without a second initialization. |
| `STEP18-EXT-VERIFY-025` | Unit/Relational | Ambiguous initialization later verifies pending/not found | Remains explicitly unresolved; no blind initialization retry. |
| `STEP18-EXT-VERIFY-026` | HTTP | Successful response caching | `Cache-Control: no-store`; no provider secret/card material. |

## F. Webhook transport, signature, and payload

| ID | Level | Scenario | Expected |
|---|---|---|---|
| `STEP18-EXT-WEBHOOK-001` | HTTP | Missing signature | 400 before persistence. |
| `STEP18-EXT-WEBHOOK-002` | HTTP | Blank, repeated, malformed, odd-length, or non-hex signature | 400 before persistence. |
| `STEP18-EXT-WEBHOOK-003` | Unit/HTTP | Lowercase signature | Valid exact HMAC-SHA256 accepted. |
| `STEP18-EXT-WEBHOOK-004` | Unit/HTTP | Uppercase signature | Equivalent valid hex accepted. |
| `STEP18-EXT-WEBHOOK-005` | Unit/HTTP | Wrong secret | Rejected in constant time. |
| `STEP18-EXT-WEBHOOK-006` | Unit/HTTP | Body whitespace/order changed after signing | Rejected even if JSON meaning is equivalent. |
| `STEP18-EXT-WEBHOOK-007` | Unit/HTTP | UTF-8 multibyte byte changed | Rejected using exact raw bytes. |
| `STEP18-EXT-WEBHOOK-008` | Unit/HTTP | BOM added or removed | Signature follows exact transmitted bytes. |
| `STEP18-EXT-WEBHOOK-009` | HTTP | Missing/wrong media type | 415. |
| `STEP18-EXT-WEBHOOK-010` | HTTP | Empty or malformed JSON with valid signature | Stable event-invalid 400. |
| `STEP18-EXT-WEBHOOK-011` | HTTP | Body exactly 65,536 bytes | Accepted for parsing; result depends on payload validity. |
| `STEP18-EXT-WEBHOOK-012` | HTTP | Body above 65,536 bytes by content length or streaming | 413 before event processing. |
| `STEP18-EXT-WEBHOOK-013` | HTTP | Chunked body without content length | Bounded streaming enforcement. |
| `STEP18-EXT-WEBHOOK-014` | HTTP | Secret not configured | 503; body not trusted or persisted. |
| `STEP18-EXT-WEBHOOK-015` | Unit | Missing event, data, transaction, reference, amount, currency, or required identity | Stable event-invalid or durable quarantine, never paid. |
| `STEP18-EXT-WEBHOOK-016` | Unit | Numeric/string identifier variants | Parsed only in documented safe forms. |
| `STEP18-EXT-WEBHOOK-017` | Unit | Root event ID missing | Deterministic synthesized identity from event/transaction/reference. |
| `STEP18-EXT-WEBHOOK-018` | Relational | Identical duplicate | One provider-scoped receipt and one transition. |
| `STEP18-EXT-WEBHOOK-019` | Relational | Same provider/event ID with changed body | 409 conflict; original result preserved. |
| `STEP18-EXT-WEBHOOK-020` | Relational | Same event ID from different provider | Provider-scoped identity prevents collision. |
| `STEP18-EXT-WEBHOOK-021` | Relational | Parallel identical deliveries | All safe acknowledgements; one receipt. |
| `STEP18-EXT-WEBHOOK-022` | Relational | Parallel distinct events for one payment | Optimistic conflicts retry and converge. |
| `STEP18-EXT-WEBHOOK-023` | Relational | Unknown reference | Durable quarantine and 200 acknowledgement. |
| `STEP18-EXT-WEBHOOK-024` | Relational | Amount mismatch | Durable quarantine; no paid/refund mutation. |
| `STEP18-EXT-WEBHOOK-025` | Relational | Currency mismatch | Durable quarantine. |
| `STEP18-EXT-WEBHOOK-026` | Relational | Transaction ID mismatch | Durable quarantine; stored ID not overwritten. |
| `STEP18-EXT-WEBHOOK-027` | Relational | Exact `charge.success` | Payment and booking complete atomically. |
| `STEP18-EXT-WEBHOOK-028` | Relational | Unknown event type | Durable ignored completion; no payment mutation. |
| `STEP18-EXT-WEBHOOK-029` | Relational | Failure before durable receipt/state commit | 5xx so Lahza retries; no partial commit. |
| `STEP18-EXT-WEBHOOK-030` | Relational | SQL commit acknowledgement lost | Persisted receipt/state is detected and safely acknowledged. |
| `STEP18-EXT-WEBHOOK-031` | HTTP | Invalid-signature flood | Separate bounded invalid-signature rate partition. |
| `STEP18-EXT-WEBHOOK-032` | HTTP | Valid-signature traffic | Separate valid-webhook partition; anonymous JWT/device exemption only for this route. |
| `STEP18-EXT-WEBHOOK-033` | Security | Provider payload contains PII/card authorization fields | Unneeded fields ignored and never logged/persisted. |
| `STEP18-EXT-WEBHOOK-034` | Public HTTPS | Lahza retries non-200 delivery | Later identical delivery converges to one result. |
| `STEP18-EXT-WEBHOOK-035` | Public HTTPS | Dashboard webhook points to wrong/non-TLS route | Release gate fails; card remains disabled for production. |

## G. Payment lifecycle and refund events

| ID | Level | Scenario | Expected |
|---|---|---|---|
| `STEP18-EXT-LIFE-001` | Unit/Relational | Success from Pending | Completed and booking paid. |
| `STEP18-EXT-LIFE-002` | Unit/Relational | Success from Failed | Completed when exact provider evidence later succeeds. |
| `STEP18-EXT-LIFE-003` | Unit/Relational | Duplicate success | No duplicate fulfillment. |
| `STEP18-EXT-LIFE-004` | Unit/Relational | Failure after Completed | Completed remains final for non-refund failure. |
| `STEP18-EXT-LIFE-005` | Unit/Relational | Success after Refunded | Refunded remains final. |
| `STEP18-EXT-LIFE-006` | Relational | Payment and booking write failure | Both roll back together. |
| `STEP18-EXT-REFUND-001` | Relational | `refund.pending` | Durable deferred state; payment remains Completed. |
| `STEP18-EXT-REFUND-002` | Relational | `refund.processing` | Durable deferred state; payment remains Completed. |
| `STEP18-EXT-REFUND-003` | Relational | Full `refund.processed` after success | Payment Refunded and booking unpaid atomically. |
| `STEP18-EXT-REFUND-004` | Relational | Full refund before success | Deferred then converged after exact success. |
| `STEP18-EXT-REFUND-005` | Relational | `refund.failed` | Receipt recorded; Completed/Refunded payment does not regress. |
| `STEP18-EXT-REFUND-006` | Relational | Partial refund | Receipt recorded as unsupported partial refund; whole payment remains Completed. |
| `STEP18-EXT-REFUND-007` | Relational | Refund amount zero, negative, or above original | Quarantined/invalid; no state mutation. |
| `STEP18-EXT-REFUND-008` | Relational | Refund currency mismatch | Quarantined. |
| `STEP18-EXT-REFUND-009` | Relational | Refund transaction mismatch | Quarantined. |
| `STEP18-EXT-REFUND-010` | Relational | Duplicate pending/processing/processed/failed event | Idempotent receipt and transition. |
| `STEP18-EXT-REFUND-011` | Relational | Multiple distinct partial refunds | All receipts retained; no false full-refund state. |
| `STEP18-EXT-REFUND-012` | Relational | Concurrent success and processed refund | Both HTTP calls safe; final state Refunded. |
| `STEP18-EXT-REFUND-013` | Relational | Restart between deferred refund and success | Cross-process query converges after restart. |
| `STEP18-EXT-REFUND-014` | Contract | Customer refund-create route | Absent; customer cannot initiate unrestricted refunds. |
| `STEP18-EXT-REFUND-015` | Product/manual | Dashboard-originated full refund | Documented Lahza event sequence converges; no API-side blind retry. |
| `STEP18-EXT-REFUND-016` | Product | Automated refund initiation requested later | Requires separate authorization, idempotency, partial-refund model, and provider lookup design before implementation. |

## H. Migration and legacy Stripe compatibility

| ID | Level | Scenario | Expected |
|---|---|---|---|
| `STEP18-EXT-MIGRATION-001` | Migration | Clean database to latest | Four Customer migrations apply; no pending model changes. |
| `STEP18-EXT-MIGRATION-002` | Migration | Populated Stripe ILS/USD/EUR rows | All remain valid and readable as Provider Stripe. |
| `STEP18-EXT-MIGRATION-003` | Migration | Legacy null provider reference | Null preserved; no fake provider identity invented. |
| `STEP18-EXT-MIGRATION-004` | Migration | Legacy intent and charge IDs | Preserved as provider reference/transaction ID. |
| `STEP18-EXT-MIGRATION-005` | Migration | Legacy webhook receipt | Event/body/state/payment linkage preserved under Provider Stripe. |
| `STEP18-EXT-MIGRATION-006` | Migration | Legacy Pending payment | Remains readable with explicit retired-provider status; never initialized through Lahza. |
| `STEP18-EXT-MIGRATION-007` | Migration | Legacy Completed/Failed/Refunded payment | Status and booking history preserved. |
| `STEP18-EXT-MIGRATION-008` | Migration | Duplicate identifiers across different providers | Composite provider identity permits safe coexistence. |
| `STEP18-EXT-MIGRATION-009` | Migration | Duplicate identifier within same provider | Unique constraint rejects it. |
| `STEP18-EXT-MIGRATION-010` | Migration | Upgrade, downgrade, and re-upgrade populated database | Schema operations complete without duplicate-index failure and preserve mapped identifiers. |
| `STEP18-EXT-MIGRATION-011` | Migration | Repeated latest update | No-op. |
| `STEP18-EXT-MIGRATION-012` | Contract | Current runtime/schema scan | No Stripe SDK, route, configuration, client-secret, publishable-key, or access-code surface remains. |

## I. Security, privacy, observability, and resilience

| ID | Level | Scenario | Expected |
|---|---|---|---|
| `STEP18-EXT-SECURITY-001` | Security | Secret/API response exception | Problem and logs contain no secret, authorization header, raw body, or provider response. |
| `STEP18-EXT-SECURITY-002` | Security | Customer/payment rejection logging | IDs and safe codes only; no email, phone, localized descriptions, or checkout URL. |
| `STEP18-EXT-SECURITY-003` | Security | Webhook logging | Safe event/reference identity only; no raw payload or card authorization object. |
| `STEP18-EXT-SECURITY-004` | Security | Checkout response caching | `no-store`; no secret-bearing fields. |
| `STEP18-EXT-SECURITY-005` | Security | Swagger | Documents Bearer/device/Lahza signature correctly; never contains configured secret. |
| `STEP18-EXT-SECURITY-006` | Security | CORS/browser request to webhook | Does not bypass signature validation. |
| `STEP18-EXT-SECURITY-007` | Security | TLS certificate invalid/expired/name mismatch | Outbound call fails closed; certificate validation never disabled. |
| `STEP18-EXT-SECURITY-008` | Resilience | Lahza outage | Existing reads remain available; create/verify fail safely; no duplicate transaction. |
| `STEP18-EXT-SECURITY-009` | Resilience | Slow Lahza response | Bounded timeout and durable ambiguity behavior. |
| `STEP18-EXT-SECURITY-010` | Resilience | Customer API restart during initialization | Local identity/lease persists and prevents duplicate creation. |
| `STEP18-EXT-SECURITY-011` | Resilience | Customer API restart after webhook commit | Duplicate delivery returns safe success. |
| `STEP18-EXT-SECURITY-012` | Resilience | Database unavailable during webhook | Non-200 retryable response; no acknowledgement-shaped data loss. |
| `STEP18-EXT-SECURITY-013` | Resilience | Database unavailable during verify | Safe 5xx; no false paid state. |
| `STEP18-EXT-SECURITY-014` | Operations | Correlation ID malicious/overlong | Sanitized bounded correlation value. |
| `STEP18-EXT-SECURITY-015` | Operations | Clock/time-zone differences | Provider timestamps never replace server-owned state ordering rules. |
| `STEP18-EXT-SECURITY-016` | Operations | Lahza documentation field/status changes | Unknown fields ignored; unknown required semantics fail closed and trigger contract review. |

## J. Real Lahza test-mode card matrix

Use the current values from:
`https://docs.lahza.io/payments/test-payments`.
Do not copy card details into this repository or result files.

| ID | Level | Scenario | Expected |
|---|---|---|---|
| `STEP18-EXT-REAL-001` | Lahza test | Initialize ILS payment | Test-domain HTTPS checkout opens; exact reference/amount/currency verify. |
| `STEP18-EXT-REAL-002` | Lahza test | Initialize JOD payment | Exact minor units and JOD returned by verification. |
| `STEP18-EXT-REAL-003` | Lahza test | Initialize USD payment | Exact minor units and USD returned by verification. |
| `STEP18-EXT-REAL-004` | Lahza test/manual mobile | Documented successful Visa | Verification success completes exactly once. |
| `STEP18-EXT-REAL-005` | Lahza test/manual mobile | Documented successful MasterCard | Verification success completes exactly once. |
| `STEP18-EXT-REAL-006` | Lahza test/manual mobile | Documented insufficient-funds card | Local payment never becomes paid. |
| `STEP18-EXT-REAL-007` | Lahza test/manual mobile | Documented do-not-honour card | Local payment never becomes paid. |
| `STEP18-EXT-REAL-008` | Lahza test/manual mobile | Documented authentication-failed card | Local payment never becomes paid. |
| `STEP18-EXT-REAL-009` | Lahza test/manual mobile | Valid card with intentionally invalid CVV | Failed/non-success verification; retry behavior safe. |
| `STEP18-EXT-REAL-010` | Lahza test/manual mobile | Valid card with intentionally invalid expiry | Failed/non-success verification; retry behavior safe. |
| `STEP18-EXT-REAL-011` | Lahza test/manual mobile | User abandons hosted checkout | Pending/abandoned outcome does not deliver value. |
| `STEP18-EXT-REAL-012` | Lahza test | Repeat local create after each outcome | Same logical payment/reference; no second initialization. |
| `STEP18-EXT-REAL-013` | Lahza test | Repeated server verification | Stable idempotent local state. |
| `STEP18-EXT-REAL-014` | Lahza test | Provider transaction viewed in dashboard | Reference, amount, currency, mode, and final status match Ghseeli without exposing card data. |
| `STEP18-EXT-REAL-015` | Public HTTPS | Real `charge.success` delivery | Signature passes and converges with verification exactly once. |
| `STEP18-EXT-REAL-016` | Public HTTPS | Dashboard full refund | pending/processing/processed or documented subset converges to Refunded. |
| `STEP18-EXT-REAL-017` | Public HTTPS | Webhook endpoint deliberately returns non-200 once | Lahza retry is observed and deduplicated. |
| `STEP18-EXT-REAL-018` | Public HTTPS | Callback redirect | Callback alone changes no payment state; owned verification completes the flow. |

## Required execution order

1. Keep production card payments disabled.
2. Run the complete automated solution suite and safe-local Step 18 manifest.
3. Configure a **test-mode** Lahza secret through user secrets or an
   environment variable; never paste it into chat or a tracked file.
4. Confirm in the Lahza dashboard that the key/account is in test mode.
5. Run `STEP18-EXT-REAL-001` through `014` against disposable bookings.
6. Enable a temporary trusted HTTPS test endpoint and configure the Lahza
   webhook/callback there.
7. Run `STEP18-EXT-REAL-015` through `018`.
8. Re-run verification and database invariants after delayed webhook retries.
9. Record sanitized evidence and rotate/revoke temporary test credentials.
10. Enable production card capability only after every release-blocking
    scenario passes or has an explicit non-shipping disposition.

## Known provider-documentation uncertainties

- Lahza documents HMAC-SHA256 but its C# webhook sample constructs SHA-512.
  Ghseeli follows the protocol text and other language samples: HMAC-SHA256.
- Complete transaction status vocabulary is not documented. Unknown statuses
  fail closed and never mark paid.
- Webhook payload schemas and root event-ID guarantees are incomplete.
- Duplicate-reference behavior and recovery of a lost authorization URL are
  not documented. Ghseeli durably marks ambiguous initialization and does not
  issue a blind second initialization.
- Refund idempotency and lookup guarantees are insufficient for safe automated
  refund initiation. Only inbound refund lifecycle handling is currently in
  scope.
- The mobile documentation contains inconsistent close-host examples. Mobile
  clients must use a reviewed allow-list and server verification rather than
  trusting a close URL.

## Authoritative Lahza references

- https://api-docs.lahza.io/api-endpoints/transactions
- https://api-docs.lahza.io/api-endpoints/refunds
- https://docs.lahza.io/payments/test-payments
- https://docs.lahza.io/payments/verify-payments
- https://docs.lahza.io/payments/webhooks
- https://docs.lahza.io/guide/checkout-in-a-mobile-webview
