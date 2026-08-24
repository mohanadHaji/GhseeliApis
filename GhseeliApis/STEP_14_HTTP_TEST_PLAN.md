# Step 14 HTTP Test Plan — Server-Authoritative Payments

Status: reconciled with the complete uncommitted Step 14 implementation,
`CustomerPaymentApiIntegrationTests`, the relational/payment/webhook suites,
and `step-14-payment-rebuild.manifest.json` at Customer API HEAD `53290a1`.

HTTP required: **Yes** — Step 14 replaces the legacy payment creation and
webhook behavior with device/JWT/ownership-protected, idempotent, localized
versioned endpoints and verified Stripe callbacks.

## Implemented observable contract

```http
POST /api/v1/payments/intents?language={ar|he}
Authorization: Bearer <customer JWT>
X-Device-Token: <device token>
Idempotency-Key: <bounded opaque key>
Content-Type: application/json

{ "bookingId": "<customer booking public reference>", "method": "Card" }
```

The request deliberately has no authoritative amount or currency. Unknown
client money/identity/status fields are ignored for forward compatibility and
must not change the persisted Payment or Stripe request. Success returns `200`
and `Cache-Control: no-store`. It contains the
owned payment public ID, booking public reference, server amount/currency,
status, method, Stripe client secret, and publishable key or equivalent
client-safe confirmation data. It never returns the Stripe secret key,
webhook secret, raw provider response, customer PII, or internal database IDs.

```http
GET /api/v1/payments/{paymentId}?language={ar|he}
Authorization: Bearer <customer JWT>
X-Device-Token: <device token>
```

Only the owning customer on the booking's owning device can read it. Wrong
owner/device and unknown IDs are indistinguishable `404 payment_not_found`.

`POST /api/stripe/webhook` remains anonymous to JWT/device middleware but
requires a valid Stripe signature over the exact raw body. It returns a
provider-safe 2xx only after an event is durably recorded/applied or durably
recognized as a duplicate/irrelevant event. Signature, body, persistence, and
transient failures must not be swallowed as false success.

### Frozen implementation alignment

These names and statuses are implemented and protected by the Step 14
controller, service, relational, TestServer, and live-local evidence:

- `Card` is the public method name. `Wallet`, `CashOnArrival`, and `ThirdParty`
  return `409 payment_method_not_yet_supported`.
- Card with missing/invalid Stripe configuration returns
  `503 payment_provider_unavailable`; no Payment or provider object is created.
- Invalid request/model binding uses `400 payment_request_invalid`; unsupported
  media type uses `415 payment_unsupported_media_type`; over 65,536 UTF-8 bytes
  uses `413 payment_request_too_large`.
- Missing/invalid idempotency uses `400 idempotency_key_required` /
  `idempotency_key_invalid`; changed content under the same key uses
  `409 idempotency_conflict`.
- `CustomerBooking` is already the confirmed immutable Customer record created
  by `POST /api/v1/bookings/from-draft`; its separate Business workflow status
  initially remains `Pending`. Owned bookings in either `Pending` or
  `Confirmed` are payable. `InProgress`, `Completed`, `Cancelled`, and `NoShow`
  are not payable. An eligible booking must have a positive immutable
  `GrandTotal` and Stripe-supported ISO currency. Ineligible state uses
  `409 booking_not_payable`; malformed/unsupported immutable currency uses
  `409 booking_currency_not_supported`.
- A Stripe PaymentMethod ID is not required by the Step 14 HTTP contract. The
  standard mobile flow creates an **unconfirmed** PaymentIntent from immutable
  booking money and returns only client-safe confirmation data (for example a
  client secret). The server never sets paid state from intent creation or a
  client confirmation response; only a verified webhook may do so.
- One booking has at most one logical card Payment/PaymentIntent. A different
  idempotency key replays that logical result rather than creating another.
- Provider amount is the checked exact minor-unit conversion of immutable
  `CustomerBooking.GrandTotal`; provider currency comes from immutable
  `CustomerBooking.Currency`.
- Webhook events are deduplicated by Stripe event ID and validated against the
  stored PaymentIntent ID plus booking/payment metadata, amount, and currency.
  Mismatch uses a durable ignored/quarantined result and never mutates paid
  state.
- `payment_intent.succeeded` marks payment paid and booking paid atomically.
  `payment_intent.payment_failed` marks a pending payment failed;
  `payment_intent.canceled` marks it canceled/failed; `charge.refunded` marks it
  refunded and updates booking paid state. Final enum/property names may differ,
  but the observable state semantics must remain.
- A later failure/cancel event cannot roll back paid state, and success cannot
  overwrite refunded state. Event ordering is governed by legal transitions,
  not arrival order alone.

## Execution split

### Live local Stripe-safe

The committed manifest is
`scripts/http-tests/plans/step-14-payment-rebuild.manifest.json`.
It is safe only against dedicated local/test Customer SQL data and Stripe test
mode. It never confirms a PaymentIntent, supplies a payment method, or creates a
charge. Run tags separately:

- `live-common`: routing, auth, transport, validation, ownership, unsupported
  methods, reads, missing/invalid signatures.
- `live-unconfigured`: local host without valid Stripe keys; asserts fail-closed
  `503` and no persistence.
- `live-stripe-test`: local host configured with Stripe **test-mode** keys;
  creates an unconfirmed PaymentIntent only and verifies replay/read invariants.
  Cleanup must cancel test intents through a local fixture/verifier added by the
  implementation phase; it must never use production keys.

The manifest expects local-only variables for JWTs, device tokens, booking
references, payment IDs, and idempotency keys. No secret or PII is committed.
Only `STEP14-INTENT-REAL-STRIPE-057` requires a real Stripe network call
and valid `pk_test_`/`sk_test_` credentials. Seeded owned reads and completed
idempotency replay/conflict/new-key cases do not require Stripe availability.
Scenarios 051–053 and 062 use independent seeded fixtures and have no manifest
dependency on 057.

Locally signed webhook cases use a generated local `whsec_` plus seeded local
records and never require `pk_test_`/`sk_test_` or a Stripe network call. The
harness now generates local Stripe signatures and sends exact UTF-8 bodies, so
signed local cases run from the same runtime manifest. Four transport cases
remain TestServer-only because Kestrel or `HttpClient` may reject malformed
headers or oversized streaming bodies before a stable application response.
Concurrency, deterministic ordering, and persistence failpoints remain
TestServer/relational-only.

### Automated/TestServer-only

Controlled provider fakes, fixed clocks, relational SQL, parallel requests,
Stripe signature generation with a test secret, persistence failpoints, event
reordering, and webhook retry behavior are automated-only. They must never be
simulated by weakening a live host or contacting real Stripe.

## Stable scenario matrix

For **customer payment endpoints only**, every controller/auth/model-binding
error asserts `application/problem+json`, stable `code`, localized safe
`title`/`detail`, `language`, `correlationId`, matching `X-Correlation-Id`,
`Cache-Control: no-store`, and no stack/SQL/Stripe secret/PII leakage unless a
row states a route-level 404/405 with no application problem body. Payment
successes assert `application/json`, `Cache-Control: no-store`, exact public
fields (`id`, public `bookingId`, `amount`, `currency`, normalized `method`,
`status`, nullable `providerStatus`, lifecycle-safe confirmation fields, and
`createdAtUtc`) and no internal booking ID, user/device ID, Stripe secret key,
webhook secret, raw provider body, or charge/provider identifiers.

For the **webhook endpoint only**, errors assert
`application/problem+json`, exact webhook `code`, generic non-localized
`title`/`detail`, `correlationId`, matching `X-Correlation-Id`, and
`Cache-Control: no-store`; webhook problems deliberately have no `language`.
Webhook success asserts exactly the JSON acknowledgement `{ "received": true
}` plus `Cache-Control: no-store` and correlation echo, never payment/customer
data. Route-level 404/405 responses are scoped out of both global problem-body
assertion sets.

### Contract and routing

| ID | Level | Request / setup | Expected result and side effects |
|---|---|---|---|
| `STEP14-CONTRACT-SWAGGER-001` | Live + TestServer | Customer Swagger | Versioned intent POST, owned payment GET, and anonymous unversioned webhook POST exist; the create schema contains only `bookingId`/`method`; Bearer security is present on customer operations and absent on the webhook; legacy payment paths and write verbs are absent. Runtime status contracts are asserted by their individual scenarios rather than inferred from incomplete Swagger response metadata. |
| `STEP14-ROUTE-INTENT-WRONG-VERB-002` | Live manifest | PUT intent route | Exact 405 with `Allow: POST`; no Payment/provider call/idempotency row. |
| `STEP14-ROUTE-READ-WRONG-VERB-003` | Live manifest | POST payment read route | Exact 405 with `Allow: GET`; no mutation. |
| `STEP14-ROUTE-WEBHOOK-WRONG-VERB-004` | Live manifest | GET webhook route | Exact 405 with `Allow: POST`; no event row. |

### Customer JWT, device, and ownership

| ID | Level | Request / setup | Expected result and side effects |
|---|---|---|---|
| `STEP14-AUTH-JWT-MISSING-005` | Live | Valid device, no Authorization | 401 customer authentication problem; no Payment/provider/idempotency row. |
| `STEP14-AUTH-JWT-EMPTY-006` | Live | Empty bearer token | 401; no side effect. |
| `STEP14-AUTH-JWT-MALFORMED-007` | TestServer | Malformed bearer token | 401, never 500. |
| `STEP14-AUTH-JWT-EXPIRED-008` | TestServer | Fixed expired customer JWT | 401; no side effect. |
| `STEP14-AUTH-JWT-WRONG-ROLE-009` | TestServer | Valid Customer-issuer JWT with Admin/non-User role | Exact 403 `customer_authorization_forbidden`, localized payment problem and no side effect. A foreign-issuer Business token is separately an invalid token and receives 401. |
| `STEP14-AUTH-DEVICE-MISSING-010` | Live | Valid customer JWT, no device header | 401 `device_token_missing`. |
| `STEP14-AUTH-DEVICE-EMPTY-011` | Live | Empty device header | 401 stable device error. |
| `STEP14-AUTH-DEVICE-MALFORMED-012` | TestServer | Illegal/oversized token | 401 stable device error; raw token absent from logs. |
| `STEP14-AUTH-DEVICE-EXPIRED-013` | TestServer | Fixed expired token | 401 stable device error. |
| `STEP14-AUTH-DEVICE-ROTATED-014` | TestServer | Previously rotated token | 401 stable device error. |
| `STEP14-OWNERSHIP-WRONG-USER-015` | Live | User B/device B requests user A booking | 404 `booking_not_found`; no provider lookup/call. |
| `STEP14-OWNERSHIP-WRONG-DEVICE-016` | Live | User A JWT with user A's different device | 404 `booking_not_found`; no provider lookup/call. |
| `STEP14-OWNERSHIP-UNKNOWN-BOOKING-017` | Live | Unknown well-formed booking reference | Same 404 shape/timing class as wrong ownership. |

### Transport, model binding, and bounded input

| ID | Level | Request / setup | Expected result and side effects |
|---|---|---|---|
| `STEP14-BODY-MISSING-018` | Live | No request body | 400 `payment_request_invalid`; no idempotency/provider/payment. |
| `STEP14-BODY-NULL-019` | Live | Literal `null` | Same safe 400. |
| `STEP14-BODY-EMPTY-020` | Live | Zero-byte JSON body | Same safe 400. |
| `STEP14-BODY-MALFORMED-021` | Live | Truncated JSON fixture | Same safe 400, not 500. |
| `STEP14-BODY-CONTENT-TYPE-022` | Live | Valid JSON as `text/plain` | 415 `payment_unsupported_media_type`; no side effect. |
| `STEP14-BODY-OVERSIZE-LENGTH-023` | TestServer | Content-Length >65,536 bytes | 413 `payment_request_too_large`; request stops before deserialization/provider. |
| `STEP14-BODY-OVERSIZE-CHUNKED-024` | Live | Chunked byte 65,537 via `repeatBody` | Same 413; bounded reader does not consume remainder. |
| `STEP14-BODY-BOUNDARY-025` | TestServer | Exact 65,536-byte valid JSON | Passes size middleware and reaches semantic validation. |
| `STEP14-BODY-BOOKING-MISSING-026` | Live manifest | Missing/empty bookingId | 400 `payment_request_invalid`; exact `fieldErrors.bookingId = ["Booking ID is required."]`, localized `title`/`detail`, and no other field key. |
| `STEP14-BODY-BOOKING-MALFORMED-027` | Live manifest | Non-GUID bookingId | 400 `payment_request_invalid`; exact normalized `fieldErrors.bookingId = ["payment_request_invalid"]`, never a serializer/type/exception message. |
| `STEP14-BODY-METHOD-MISSING-028` | Live manifest | Missing/blank method | 400 `payment_request_invalid`; no `fieldErrors` is emitted by controller semantic validation; absent/default input cannot silently select Card. |
| `STEP14-BODY-METHOD-UNKNOWN-029` | Live manifest | Unknown string/integer method | 400 `payment_request_invalid`; numeric enum coercion does not enable a method. |
| `STEP14-BODY-UNKNOWN-FIELDS-030` | TestServer | Add amount/currency/user/payment/status/provider/PaymentMethodId fields | Request may proceed, but every supplied value is ignored; provider request and persistence use only owned booking/server state. A supplied PaymentMethodId cannot make the server confirm the intent. |

### Server amount, currency, method, and booking eligibility

| ID | Level | Request / setup | Expected result and side effects |
|---|---|---|---|
| `STEP14-AUTHORITY-AMOUNT-TAMPER-031` | TestServer | Include lower/zero/negative/huge amount variants | Stripe fake always receives immutable booking total in exact minor units or request is rejected before call; one logical Payment only. |
| `STEP14-AUTHORITY-CURRENCY-TAMPER-032` | TestServer | Include another currency | Stripe fake always receives immutable booking currency or request is rejected before call. |
| `STEP14-AUTHORITY-BOOKING-MUTATION-033` | Relational/TestServer | Change booking snapshot after intent fixture boundary | Immutable persisted payment/provider proof remains original; prohibited booking snapshot mutation fails at persistence boundary. |
| `STEP14-CURRENCY-ILS-ROUNDING-034` | TestServer | ILS totals 0.01, 1.005-equivalent persisted precision, max supported value | Deterministic checked minor units; no floating point. |
| `STEP14-CURRENCY-ZERO-DECIMAL-035` | TestServer | Supported zero-decimal currency fixture if enabled | Correct exponent; otherwise stable unsupported-currency conflict. |
| `STEP14-CURRENCY-MALFORMED-036` | TestServer | Empty/lower/overlong/non-ISO immutable currency | Fail closed `booking_currency_not_supported`; no Payment/provider call. |
| `STEP14-CURRENCY-PROVIDER-UNSUPPORTED-037` | TestServer | Well-formed currency unsupported by Stripe policy | Same stable 409; provider not called. |
| `STEP14-METHOD-WALLET-038` | Live | Method Wallet | 409 `payment_method_not_yet_supported`; no Payment/provider call. |
| `STEP14-METHOD-CASH-039` | Live | Method CashOnArrival | Same stable 409. |
| `STEP14-METHOD-THIRD-PARTY-040` | Live | Method ThirdParty | Same stable 409. |
| `STEP14-BOOKING-PENDING-041` | TestServer | Owned immutable Customer booking with Business workflow status Pending; request contains booking ID and Card only | 200 client-safe confirmation response; exactly one unconfirmed pending PaymentIntent/Payment using immutable server amount/currency; booking remains unpaid until a verified success webhook. |
| `STEP14-BOOKING-IN-PROGRESS-042` | TestServer | Booking InProgress | 409 `booking_not_payable`; no Payment/provider call. |
| `STEP14-BOOKING-COMPLETED-043` | TestServer | Booking Completed but unpaid | 409 `booking_not_payable`; no Payment/provider call. |
| `STEP14-BOOKING-CANCELLED-044` | TestServer | Booking Cancelled | Same conflict. |
| `STEP14-BOOKING-NO-SHOW-045` | TestServer | Booking NoShow | Same conflict. |
| `STEP14-BOOKING-ZERO-TOTAL-046` | TestServer | Confirmed immutable total 0 | 409 `booking_not_payable`; no free/zero Stripe intent. |
| `STEP14-BOOKING-NEGATIVE-TOTAL-047` | TestServer | Corrupt negative immutable total | Fail closed; no provider call and safe diagnostic. |
| `STEP14-BOOKING-CONFIRMED-106` | TestServer | Owned immutable Customer booking with Business workflow status Confirmed; request contains booking ID and Card only | Same 200 unconfirmed-intent behavior as 041; exact immutable server amount/currency; booking remains unpaid until verified webhook. |

### Idempotency, provider configuration, failure, and concurrency

| ID | Level | Request / setup | Expected result and side effects |
|---|---|---|---|
| `STEP14-IDEM-MISSING-048` | Live | Omit Idempotency-Key | 400 `idempotency_key_required`; no Payment/provider call. |
| `STEP14-IDEM-EMPTY-049` | Live | Empty/whitespace key | 400 `idempotency_key_invalid`; no side effect. |
| `STEP14-IDEM-MALFORMED-050` | TestServer | Space/CRLF/multiple/overlong key | Stable 400; raw invalid key absent from logs/persistence. |
| `STEP14-IDEM-REPLAY-SAME-051` | Live seeded | Seeded completed intent; same user/device/key and identical canonical booking + case-normalized Card body | Exact original logical 200; same payment, intent-backed client-safe fields, amount and currency; no provider call. Does not require Stripe configuration after completion. |
| `STEP14-IDEM-CONFLICT-052` | Live seeded | Seeded key; same user/device/key but changed booking or canonical method | Exact 409 `idempotency_conflict`; original result unchanged; no provider call. |
| `STEP14-IDEM-NEW-KEY-SAME-BOOKING-053` | Live seeded | Seeded completed payment; new key for same owned booking/Card | Existing logical payment returned and new scoped key records that result; no second PaymentIntent/Payment and no Stripe configuration/network requirement. |
| `STEP14-CONCURRENCY-SAME-KEY-054` | Relational/TestServer | Parallel identical requests/key | One winner/provider call/payment/intent; all callers receive same logical result. |
| `STEP14-CONCURRENCY-DIFFERENT-KEY-055` | Relational/TestServer | Parallel card requests, same booking, different keys | Booking-level uniqueness yields one logical payment/intent; no 500. |
| `STEP14-PROVIDER-UNCONFIGURED-056` | Live unconfigured | Valid payable request, no valid Stripe keys | 503 `payment_provider_unavailable`; no Payment/idempotent success/provider call. |
| `STEP14-INTENT-REAL-STRIPE-057` | Live Stripe test | Valid confirmed booking and test-mode config | 200; unconfirmed test PaymentIntent only; exact server amount/currency/metadata; one pending Payment. |
| `STEP14-PROVIDER-TIMEOUT-BEFORE-ACCEPT-058` | TestServer fake | `TimeoutException`, `IOException`, `HttpRequestException`, or non-caller cancellation before a confirmed response | Exact 503 `payment_gateway_ambiguous`; pending local logical Payment/key remains retryable, lease is released, booking remains unpaid, and provider internals are absent. |
| `STEP14-PROVIDER-AMBIGUOUS-AFTER-ACCEPT-059` | TestServer fake | Provider may have accepted, then transport/commit acknowledgement is lost | Exact 503 `payment_gateway_ambiguous` unless the local intent commit can be re-read, in which case return 200; deterministic provider idempotency key and stable payment ID allow retry with at most one intent/payment. |
| `STEP14-PROVIDER-DECLINE-ON-CREATE-060` | TestServer fake | Stripe rejects/declines during PaymentIntent creation | Exact 503 `payment_gateway_ambiguous` (the implementation intentionally does not expose Stripe decline taxonomy at creation); stable local payment/key is retryable, lease released, raw provider message absent. |
| `STEP14-PERSISTENCE-FAIL-AFTER-PROVIDER-061` | TestServer failpoint | Stripe intent exists, local commit fails | Replay reconciles by provider idempotency/metadata and creates at most one local Payment. |

### Owned payment read and affected endpoint regressions

| ID | Level | Request / setup | Expected result and side effects |
|---|---|---|---|
| `STEP14-READ-OWNED-062` | Live seeded | Owner JWT/device reads a seeded pending payment | 200 exact public response; `status = "Pending"`, normalized `method = "Card"`, and non-empty `clientSecret` plus `publishableKey` are present because mobile confirmation is still required. No Stripe network/configuration dependency for the read. |
| `STEP14-READ-WRONG-USER-063` | Live | User B reads user A payment | 404 `payment_not_found`; no PII/provider identifiers. |
| `STEP14-READ-WRONG-DEVICE-064` | Live | Owner JWT on different device reads payment | Same non-enumerating 404. |
| `STEP14-READ-UNKNOWN-065` | Live | Unknown payment public ID | Same 404. |
| `STEP14-REGRESSION-BOOKING-DETAIL-066` | TestServer/relational | Read the `CustomerBooking` backing a payment before creation, after pending intent, and after success/refund | Exact immutable snapshot fields `PublicReference`, `GrandTotal`, `Currency`, items/selections, provider/branch and reservation/work-order IDs remain unchanged; only `IsPaid` and `PaymentState` change (`false/Unpaid` → `true/Completed` → `false/Refunded`). |
| `STEP14-REGRESSION-BOOKING-LIST-067` | TestServer | Existing legacy customer route `GET /api/Bookings/my-bookings` before/after Step 14 fixtures | Existing legacy Booking list shape/count is unchanged and does not duplicate or expose the new `CustomerPayment`; Step 14's authoritative `CustomerBooking` is not silently substituted into this legacy route. |
| `STEP14-REGRESSION-BOOKING-CANCEL-068` | TestServer | Existing `PUT /api/Bookings/{id}/cancel` plus CustomerBooking pending/paid/refunded fixtures | Legacy cancellation contract is unchanged; it cannot mutate CustomerPayment money/provider state or create/refund a Stripe object. Payment eligibility continues to use immutable CustomerBooking workflow status. |
| `STEP14-REGRESSION-STATUS-CALLBACK-069` | TestServer/relational | Signed `POST /api/v1/internal/bookings/status` after pending/paid/refunded payment states | Existing callback response/status sequence and CustomerBooking `Status` update remain intact; callback cannot alter `GrandTotal`, `Currency`, CustomerPayment amount/intent/status, `IsPaid`, or `PaymentState`. |
| `STEP14-REGRESSION-LEGACY-CREATE-070` | Live manifest | Legacy `POST /api/payments` with a tampered amount | Exact 404 because the legacy controller is non-routable; caller cannot bypass server-authoritative creation. |
| `STEP14-REGRESSION-LEGACY-STATUS-104` | Live manifest | Legacy `PUT /api/payments/{id}/status` | Exact 404; even an admin cannot bypass verified webhook transitions. |
| `STEP14-REGRESSION-LEGACY-REFUND-105` | Live manifest | Legacy `POST /api/payments/{id}/refund` | Exact 404; refund behavior must use a future redesigned provider-safe contract. |

### Appended route, read, lifecycle, and isolation coverage

| ID | Level | Request / setup | Expected result and side effects |
|---|---|---|---|
| `STEP14-REGRESSION-LEGACY-GET-ALL-107` | Live supplemental | `GET /api/payments` with Admin JWT | Exact 404; the legacy all-payments enumeration route is removed and emits no payment data. |
| `STEP14-REGRESSION-LEGACY-GET-ID-108` | Live supplemental | `GET /api/payments/{guid}` | Exact 404; only the owner/device-protected versioned read exists. |
| `STEP14-REGRESSION-LEGACY-GET-MINE-109` | Live supplemental | `GET /api/payments/my-payments` | Exact 404; the legacy user payment-list route is removed. |
| `STEP14-REGRESSION-LEGACY-GET-BOOKING-110` | Live supplemental | `GET /api/payments/booking/{guid}` | Exact 404; booking-based payment lookup cannot bypass the versioned owned-payment route. |
| `STEP14-READ-JWT-MISSING-111` | Live supplemental | Seeded owned payment, valid device, no Authorization | Exact 401 `customer_authentication_required` payment problem; no payment facts disclosed. |
| `STEP14-READ-JWT-EMPTY-112` | Live supplemental | Seeded owned payment, valid device, empty Bearer value | Same exact 401 contract. |
| `STEP14-READ-JWT-MALFORMED-113` | TestServer | Malformed Bearer token on GET | Exact 401 `customer_authentication_required`, never 500. |
| `STEP14-READ-JWT-EXPIRED-114` | TestServer fixed clock | Expired Customer JWT on GET | Exact 401 `customer_authentication_required`. |
| `STEP14-READ-JWT-WRONG-ROLE-115` | TestServer | Valid Customer-issuer Admin/non-User JWT on GET | Exact 403 `customer_authorization_forbidden`; no payment facts disclosed. |
| `STEP14-READ-DEVICE-MISSING-116` | Live supplemental | Valid owner JWT, no `X-Device-Token` | Exact 401 `device_token_missing`; payment service is not called. |
| `STEP14-READ-DEVICE-EMPTY-117` | Live supplemental | Valid owner JWT, empty device header | Exact 401 stable device-token problem; no payment facts disclosed. |
| `STEP14-READ-DEVICE-MALFORMED-118` | TestServer | Illegal/oversized device token | Exact 401 stable device-token problem; raw token absent from response/logs. |
| `STEP14-READ-DEVICE-EXPIRED-119` | TestServer fixed clock | Expired device token | Exact 401 stable device-token problem. |
| `STEP14-READ-DEVICE-ROTATED-120` | TestServer | Previously rotated device token | Exact 401 stable device-token problem. |
| `STEP14-READ-ID-MALFORMED-121` | Live supplemental | `GET /api/v1/payments/not-a-guid?language=he` with valid auth/device | Exact route-level 404 (not `payment_not_found`, no application body) because the `{id:guid}` route does not match; no service call. |
| `STEP14-READ-PENDING-CONFIRMATION-122` | Live seeded | Owned pending payment with stored client secret/publishable key | 200 public response includes non-empty `clientSecret` and `publishableKey`, normalized `method="Card"`, and pending status; it excludes `paymentIntentId`, charge ID, Stripe secret key, webhook secret, user/device/internal booking IDs. No Stripe call/config needed. |
| `STEP14-READ-COMPLETED-TERMINAL-123` | TestServer + live seeded | Owned Completed payment | 200 with `status="Completed"` and `clientSecret=null`, `publishableKey=null`; all non-secret public fields remain stable. |
| `STEP14-READ-FAILED-TERMINAL-124` | TestServer + live seeded | Owned Failed payment | 200 with `status="Failed"` and both confirmation fields null. |
| `STEP14-READ-REFUNDED-TERMINAL-125` | TestServer + live seeded | Owned Refunded payment | 200 with `status="Refunded"` and both confirmation fields null. |
| `STEP14-BOOKING-ALREADY-PAID-126` | TestServer | Eligible-status owned booking with `IsPaid=true` | Exact 409 `payment_already_exists`; no Payment/idempotency/provider call and no confirmation secrets. |
| `STEP14-BOOKING-ALREADY-REFUNDED-127` | TestServer | Eligible-status owned booking with `PaymentState="Refunded"` | Exact 409 `payment_already_exists`; a refunded booking cannot be charged again by changing the idempotency key. |
| `STEP14-METHOD-CARD-CASING-128` | TestServer | `Card`, `card`, `CARD`, and mixed-case variants for equivalent requests | All canonicalize to method `Card`, use the same canonical request hash/idempotency semantics, return `method="Card"`, and never create casing-distinct logical payments. |
| `STEP14-IDEM-ISOLATION-USER-129` | Relational/TestServer | Two users submit the same opaque idempotency key for their separately owned bookings/devices | Keys are isolated by user (and device); both requests may create their own logical payment without conflict or cross-user replay. |
| `STEP14-IDEM-ISOLATION-DEVICE-130` | Relational/TestServer | Same user uses the same opaque key on two valid devices for separately device-owned bookings | Keys are isolated by owner device; each device receives only its own payment. A device cannot replay the other device's key to obtain its client secret. |
| `STEP14-PROVIDER-RESPONSE-MISMATCH-146` | TestServer fake | Gateway returns amount or currency different from the persisted command | Exact 502 `payment_gateway_ambiguous`; no provider result is persisted, booking remains unpaid, lease is released, and retry uses the same logical payment/provider idempotency key. |

### Stripe signature, parsing, and event identity

| ID | Level | Request / setup | Expected result and side effects |
|---|---|---|---|
| `STEP14-WEBHOOK-SIGNATURE-MISSING-071` | Live | Valid-shaped event, no Stripe-Signature | 400 `stripe_signature_missing`; no event/payment/booking mutation. |
| `STEP14-WEBHOOK-SIGNATURE-INVALID-072` | Live | Invalid signature | 400 `stripe_signature_invalid`; no mutation; signature not logged. |
| `STEP14-WEBHOOK-SIGNATURE-STALE-073` | TestServer | Correct signature outside tolerance | 400 stable invalid-signature problem. |
| `STEP14-WEBHOOK-SIGNATURE-BODY-TAMPER-074` | TestServer | Sign bytes then alter whitespace/content | 400; proves exact raw-body verification. |
| `STEP14-WEBHOOK-BODY-MALFORMED-075` | TestServer | Correctly signed malformed JSON | 400 `stripe_event_invalid`; no event row. |
| `STEP14-WEBHOOK-BODY-OVERSIZE-076` | TestServer | Content-Length 65,537 signed bytes | Exact 413 `stripe_webhook_too_large` before parsing/buffering and no event/payment/booking mutation. Kestrel/HttpClient may reject the upload before a stable application response. |
| `STEP14-WEBHOOK-UNKNOWN-EVENT-077` | TestServer + live signed local | Verified unknown Stripe event type | Exact 200 `{ "received": true }` after one durable `Completed` ignored receipt; no payment/booking mutation. |
| `STEP14-WEBHOOK-UNKNOWN-OBJECT-078` | TestServer | Verified event with unsupported Stripe object shape | Exact 200 `{ "received": true }` after durable ignored receipt; no mutation and never 500/false paid. |

### Appended webhook HTTP-boundary coverage

| ID | Level | Request / setup | Expected result and side effects |
|---|---|---|---|
| `STEP14-WEBHOOK-CONTENT-TYPE-131` | Live supplemental | POST a body as missing content type, `text/plain`, or non-JSON media type, regardless of signature | Exact 415 `stripe_webhook_unsupported_media_type`; rejection precedes configuration/signature/parser/service and creates no receipt. `application/json` with parameters is accepted. |
| `STEP14-WEBHOOK-CONFIGURATION-132` | Live unconfigured supplemental | JSON webhook while `WebhookSecret` is missing, blank, or not prefixed `whsec_` | Exact 503 `stripe_webhook_configuration_invalid`; rejection occurs before body read/signature parsing and no receipt/domain mutation occurs. No Stripe API key is relevant. |
| `STEP14-WEBHOOK-EMPTY-SIGNED-133` | Live signed local | Exact zero-byte body signed with generated local `whsec_` | Exact 400 `stripe_event_invalid`; no receipt/domain mutation. |
| `STEP14-WEBHOOK-BODY-EXACT-BOUNDARY-134` | Live signed local | Exact 65,536 UTF-8 byte body and signature over those exact bytes | Size gate accepts and reaches parser; result is determined by event validity (400 `stripe_event_invalid` for padding fixture), never 413. |
| `STEP14-WEBHOOK-BODY-OVERSIZE-CHUNKED-135` | TestServer | Unknown-length/chunked 65,537 exact signed bytes | Exact 413 `stripe_webhook_too_large` from bounded streaming read; parser/service not called and remainder is not unboundedly buffered. Kestrel may enforce its transport limit first. |
| `STEP14-WEBHOOK-ANONYMOUS-136` | Live signed local | Valid matching signed event with no Authorization and no device token | JWT/device middleware is bypassed; request reaches verified processing and returns exact 200 acknowledgement. Adding malformed JWT/device headers does not change webhook authentication semantics. |

### Verified webhook lifecycle, replay, order, and metadata

| ID | Level | Request / setup | Expected result and side effects |
|---|---|---|---|
| `STEP14-WEBHOOK-SUCCESS-079` | TestServer/relational | Verified matching `payment_intent.succeeded` | Exact 200 `{ "received": true }`; Payment becomes Completed and booking `IsPaid=true`, `PaymentState="Completed"` atomically; exact event ID/body hash recorded once. |
| `STEP14-WEBHOOK-FAILURE-080` | TestServer/relational | Verified matching `payment_intent.payment_failed` | Pending payment becomes failed; booking remains unpaid; customer-safe reason only. |
| `STEP14-WEBHOOK-CANCEL-081` | TestServer/relational | Verified matching `payment_intent.canceled` | Pending payment becomes canceled/failed; booking unpaid. |
| `STEP14-WEBHOOK-REFUND-082` | TestServer/relational | Verified matching full `charge.refunded` | Paid payment becomes refunded; booking paid state is updated consistently. |
| `STEP14-WEBHOOK-PARTIAL-REFUND-083` | TestServer + live signed local | Verified partial refund against its own initially Completed payment/booking/charge fixture | Exact 200 and one `Completed` receipt with `partial_refund_ignored` bound to that payment; Payment remains Completed, booking remains `IsPaid=true`/`PaymentState="Completed"`, immutable receipt identity/hash is exact, and the full-refund fixture from 082 is not reused. |
| `STEP14-WEBHOOK-DUPLICATE-SAME-084` | TestServer/relational | Replay identical event ID and exact raw body | Exact 200 `{ "received": true }`; one receipt and no second transition/effect. |
| `STEP14-WEBHOOK-DUPLICATE-CONFLICT-085` | TestServer | Same event ID with a different correctly signed raw body | Exact 409 `stripe_webhook_conflict`; original receipt/hash and domain state remain unchanged. |
| `STEP14-WEBHOOK-CONCURRENT-DUPLICATE-086` | Relational/TestServer | Parallel identical verified deliveries | One processed receipt/transition; all safe responses; no lock/unique-key 500. |
| `STEP14-WEBHOOK-OUT-OF-ORDER-087` | TestServer | Failure/cancel arrives after success | Paid state is not rolled back; later event durably ignored according to transition rules. |
| `STEP14-WEBHOOK-SUCCESS-AFTER-REFUND-088` | TestServer | Delayed success after refund | Refunded state is not rolled back to paid. |
| `STEP14-WEBHOOK-UNKNOWN-INTENT-089` | TestServer | Verified event for unknown intent | Durable ignored/quarantined result; no broad booking lookup or new Payment. |
| `STEP14-WEBHOOK-METADATA-BOOKING-MISSING-090` | TestServer | Matching intent but no booking metadata | No mutation; durable mismatch evidence without PII. |
| `STEP14-WEBHOOK-METADATA-BOOKING-MISMATCH-091` | TestServer | Intent maps to Payment A, metadata names booking B | No mutation to either; durable mismatch/quarantine. |
| `STEP14-WEBHOOK-METADATA-PAYMENT-MISMATCH-092` | TestServer | Payment metadata/public ID mismatches stored intent | No mutation. |
| `STEP14-WEBHOOK-AMOUNT-MISMATCH-093` | TestServer | Verified success amount differs from stored immutable amount | Never paid; mismatch durably recorded. |
| `STEP14-WEBHOOK-CURRENCY-MISMATCH-094` | TestServer | Verified success currency differs | Never paid; mismatch durably recorded. |
| `STEP14-WEBHOOK-CHARGE-INTENT-MISMATCH-095` | TestServer | Refund charge belongs to another intent/payment | Neither payment mutates. |
| `STEP14-WEBHOOK-DB-TRANSIENT-096` | TestServer failpoint | Database fails before durable receipt/transition | Exact 503 `payment_gateway_ambiguous`, never an acknowledgement; later identical delivery returns 200 and applies once. |
| `STEP14-WEBHOOK-ATOMICITY-097` | Relational/TestServer failpoint | Failure between Payment and booking paid update | Transaction rolls back both; retry applies both exactly once. |

### Appended transition-order and acknowledgement coverage

| ID | Level | Request / setup | Expected result and side effects |
|---|---|---|---|
| `STEP14-WEBHOOK-SUCCESS-AFTER-FAILURE-137` | TestServer/relational | Matching success arrives after a previously applied payment failure | Exact 200 acknowledgement; Failed transitions to Completed, booking becomes paid, and both event receipts are durable. |
| `STEP14-WEBHOOK-REFUND-BEFORE-SUCCESS-138` | TestServer/relational | Full matching `charge.refunded` arrives while Payment is Pending and has no persisted charge, then matching success with the same charge arrives | First request returns exact 200 after a durable `Deferred` receipt and leaves booking unpaid; success returns 200 and atomically converges Payment/booking to Refunded/`IsPaid=false`, completing the deferred receipt. |
| `STEP14-WEBHOOK-CONCURRENT-SUCCESS-REFUND-139` | Relational/TestServer concurrency | Matching success and full refund for the same intent/charge execute concurrently | Both requests return safe 200 after durable convergence; final Payment is Refunded, booking is `IsPaid=false`/`PaymentState="Refunded"`, exactly two event receipts exist, and no transient unique/concurrency 500 escapes. |
| `STEP14-WEBHOOK-SUCCESS-ENVELOPE-145` | Live signed local | Any valid, durably completed/ignored/quarantined/deferred event | Exact status 200, `application/json`, body structurally and semantically exactly `{ "received": true }`, `Cache-Control: no-store`, and correlation echo; no `language`, payment, booking, event disposition, or provider data. |

### Localization, correlation, and secrecy

| ID | Level | Request / setup | Expected result and side effects |
|---|---|---|---|
| `STEP14-LOCALIZATION-AR-098` | Live manifest | Arabic unsupported-method and validation errors | Exact Arabic `title`/`detail`, `language="ar"`, and unchanged status/code; model-binding `fieldErrors` contain stable machine codes rather than exception text. |
| `STEP14-LOCALIZATION-HE-099` | Live manifest | Hebrew unsupported-method and validation errors | Exact Hebrew `title`/`detail`, `language="he"`, and unchanged status/code; no English serializer text. |
| `STEP14-LOCALIZATION-PRECEDENCE-100` | Live manifest | `language=he` conflicts with Arabic header; then invalid explicit values (`en`, whitespace/unknown) | Valid query wins. Invalid explicit language returns exact 400 `payment_request_invalid`, resolves response language from normal fallback, and never reaches service/provider. |
| `STEP14-CORRELATION-PRESERVE-101` | Live + TestServer | Valid bounded correlation ID on a provider-free unsupported-method request | Exact value is echoed in header/body. This proves HTTP preservation only; it deliberately makes no provider-metadata or receipt-storage claim. |
| `STEP14-CORRELATION-REPLACE-102` | TestServer + safe live variants | Missing/empty/CRLF/overlong correlation on a provider-free unsupported-method request | Generated 32-character lowercase hexadecimal ID is echoed in header/body; unsafe input is absent from the response and captured application logs, and the controlled payment service is not called. |
| `STEP14-SECURITY-NO-SECRET-103` | TestServer/log capture | Payment/provider Problem Details plus a generic webhook persistence failure containing sentinel secret/PII/SQL text | Response assertions cover the enumerated payment/provider/webhook failures. Full exception-capturing logger proof covers the generic webhook failure and confirms sentinel Stripe key, webhook secret, SQL text, and PII are absent after the controller logs only the exception type. This does not claim capture of every framework/provider log source. |

### Appended localization and affected-route regressions

| ID | Level | Request / setup | Expected result and side effects |
|---|---|---|---|
| `STEP14-FIELD-ERRORS-AR-140` | Live supplemental | Arabic missing booking ID and separately malformed booking JSON | Semantic missing-ID response has exactly `fieldErrors.bookingId = ["Booking ID is required."]`; model-binding response has normalized `fieldErrors.bookingId = ["payment_request_invalid"]` (or `request` only when no field can be identified). `title`/`detail` are exact Arabic; field values are the current frozen locale-invariant safe contract and contain no JSON paths, CLR types, exception text, input fragments, or duplicate keys. |
| `STEP14-FIELD-ERRORS-HE-141` | Live supplemental | Hebrew missing booking ID and separately malformed booking JSON | Same exact field keys/values as 140 while `title`/`detail` and `language="he"` are Hebrew; fieldErrors are deliberately locale-invariant, deterministic, and safe. |
| `STEP14-REGRESSION-BOOKING-CONFIRM-142` | TestServer | `POST /api/v1/bookings/from-draft` before/after registering Step 14 services | Exact existing route, JWT/device/order-guid/idempotency semantics and `ConfirmedBookingResponse` fields remain unchanged; response public booking reference becomes the payment `bookingId`, while immutable `grandTotal`, `currency`, items/selections and status snapshot persist unchanged. |
| `STEP14-REGRESSION-INTERNAL-BOOKING-READ-143` | TestServer signed HMAC | `GET /api/v1/internal/bookings/{reference}` before/after success/refund webhook | Exact internal auth/route and booking-status response fields remain unchanged except the already-defined paid fields in persistence; CustomerPayment/client secret/provider identifiers are never added to the internal booking response. |
| `STEP14-READ-LANGUAGE-MALFORMED-144` | Live supplemental | Owned well-formed payment ID with explicit `?language=en` or whitespace/unknown language | Exact 400 `payment_request_invalid`; fallback response language is resolved without reading/disclosing the payment. This is distinct from malformed ID 121, where route matching returns bare 404 before controller language handling. |

## Coverage-category disposition

- Happy path and lifecycle reads: 041, 057, 062, 079–082, 106, 122–125,
  137–139, 145.
- Malformed/model binding/media/size: 018–030, 075–076, 121, 131,
  133–135.
- Authentication/authorization/ownership: 005–017, 063–065, 111–120,
  136.
- Validation/authority/currency/state: 026–047, 126–128.
- Idempotency/version/concurrency/order: 048–055, 084–088, 129–130,
  137–139.
- Failure/unavailability/atomicity: 056, 058–061, 096–097, 132, 146.
- Contract/Swagger/legacy bypass and booking regressions: 001–004, 066–070,
  104–110, 142–143.
- Localization/correlation/security: 098–103, 140–141, 144–145.
- Deletion/read-after-delete: not applicable; Step 14 adds no delete route.
- External Stripe charge: intentionally not applicable to live execution; no real
  charge is required or allowed by this plan.

## Required fixtures and verification

Implementation must provide TestServer/relational fixtures for every automated
row. Live runs need disposable local rows for:

1. two customers and devices;
2. owned payable bookings in both Pending and Confirmed workflow states;
3. wrong-user, wrong-device, unknown, non-payable, malformed-currency, and
   zero-total, already-paid, and refunded booking references;
4. local-only pending, Completed, Failed, and Refunded owned payments plus
   wrong-owner/device payment IDs;
5. pre-created idempotency records for replay, conflict, new-key, user
   isolation, and device isolation checks;
6. generated local webhook secret and matching pending/failed/completed
   payment/event fixtures for signed callbacks;
7. unique idempotency, Stripe event, and correlation values.

A Step 14 database verifier is needed only when implementation fixes the final
schema. It must assert booking/payment uniqueness, exact decimal/currency
equality, provider/event identity uniqueness, immutable booking snapshots, and
atomic paid/refund state. Do not add a speculative SQL verifier against the
legacy schema.

## Code Test Plan

- Add TestServer HTTP coverage for every automated row and provider fakes that
  capture exact request options without network access.
- Add SQL Server relational tests for uniqueness, concurrency, event dedupe, and
  transactional atomicity.
- Add focused Stripe signature tests using a generated test-only secret and
  fixed clock.
- Run failpoint, lost-acknowledgement, lease takeover, duplicate-delivery, and
  concurrent success/refund scenarios only through TestServer/relational
  fixtures. A live host cannot deterministically place failures between its
  persistence operations and the generic harness cannot coordinate the needed
  request barriers.
- Run ordinary auth, routing, validation, ownership, seeded read/idempotency,
  and local signed-webhook boundary checks against a dedicated live-local host.
  Signed streaming cases use the focused local signer/streamer, not production
  Stripe and not the generic manifest's ordinary body sender.
- Run targeted Step 14 tests, affected booking/status regressions, then the full
  solution only after implementation.

## HTTP Test Plan

- HTTP required: Yes.
- Scenario IDs: **146 stable scenarios** (`001`–`146`). All original
  `001`–`106` IDs are preserved; `107`–`146` are appended, including provider
  mismatch as 146 rather than renumbering later sections.
- Committed manifest: **130 entries representing 117 stable plan IDs**.
  Multiple entries under one ID intentionally exercise transport/localization
  variants. The other **29 plan IDs** are deterministic
  TestServer/relational/failpoint/concurrency scenarios and are not padded into
  the manifest.
- Current manifest disposition: **125 safe live-local entries**, **4
  TestServer-only transport entries**, and **1 real-Stripe entry (057)**. Thus
  129 entries are executable without Stripe network credentials, but only 125
  are sent to the live Kestrel host. Scenario counts, manifest-entry counts,
  and xUnit test-case counts are different measures and are not treated as
  one-to-one.
- Stripe credential matrix:
  - **1** scenario (`057`) requires real Stripe test-network PaymentIntent
    creation with `pk_test_`/`sk_test_`; it creates an unconfirmed intent and no
    charge.
  - **125** live-local manifest entries require no Stripe network/API keys.
    Seeded reads and 051–053 replays use persisted records.
  - Signed webhook live confirmations (077, 133–134, 136, 145) use only a
    generated local `whsec_` and seeded records.
  - 056 and 132 deliberately run with missing/invalid provider or webhook
    configuration and assert fail-closed behavior.
- No scenario may target production or use `pk_live_`, `sk_live_`, or a
  production webhook secret.
- By explicit user decision, scenario `057` is scheduled for Roadmap Step 18,
  the final pre-deployment release gate. This allows Steps 15–17 to proceed
  while test credentials are obtained; it does not waive the scenario or allow
  production deployment before it passes.

## HTTP Test Results

Executed on 2026-08-23 against a fresh explicitly named isolated local Customer
database and exact localhost HTTPS host. The safe gate passed: 125/125 live
entries, all four TestServer-only transport entries through automated coverage,
and hardened database invariants. Full reproducible commands, automated totals,
group counts, corrections, and the sole genuine deferral
(`STEP14-INTENT-REAL-STRIPE-057`, unavailable local test credentials) are in
`scripts/http-tests/plans/step-14-payment-rebuild.results.md`.
