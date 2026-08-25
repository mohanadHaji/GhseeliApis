# Step 17 HTTP Test Plan — Complete Local Release Gate

Status: **passed**

HTTP required: **Yes**. Step 17 changes caller-visible host policy and proves
the complete two-host workflow. Live-local HTTPS is required for Kestrel,
transport, proxy, host startup/recovery, cross-process HMAC, and disposable SQL
behavior. TestServer plus relational SQL and failpoints are required for fixed
time, concurrency, ambiguous commits, lease loss, dependency faults, and log
capture. Contract tests are required for route/header policy. Unit tests may
support these levels but cannot replace them.

This file is the test-first artifact. It creates no production implementation
and no Step 17 manifest. IDs are permanent. The registry is:

| Registry | Exact count |
|---|---:|
| A. Chained live-local journey and recovery | **39** |
| B. Deterministic TestServer/SQL/failpoint | **48** |
| C. Host security and transport | **31** |
| **New Step 17 scenarios** | **118** |
| D. Inherited entries in 11 retained manifests | **1,295** |
| **Registered entries including inherited entries** | **1,413** |

## 1. Frozen interpretation and assertion profiles

### 1.1 Corrections made while freezing the audit

Current routes, controllers, error registries, manifests, and Steps 12–16
resolve the audit's provisional alternatives as follows:

- device registration, draft creation, Customer registration, booking
  confirmation, payment-intent creation, internal reservation creation, and
  webhook acknowledgement return exactly **200**;
- an already registered installation without its token is exactly
  `409 device_registration_conflict`;
- cross-device confirmation is exactly `404 checkout_draft_not_found`;
- foreign-company work-order access is exactly `404 BOOKING_NOT_FOUND`;
- a Customer token on Business is `401 business_authentication_required`; a
  Business token on Customer is `401 customer_authentication_required`;
- a changed Business price is returned by a fresh direct reprice as a new
  authoritative **200** quote; confirmation with an old proof is
  `409 checkout_draft_requires_reprice` with
  `businessErrorCode=PRICE_CHANGED`;
- repeated stale catalog versions map to `503 pricing_unavailable`;
- reservation capacity rejection is `409 SLOT_UNAVAILABLE`; reuse of an order
  GUID with changed semantic content is `409 RESERVATION_INVALID`;
- webhook amount/currency/order mismatches are durable ignored **200**
  acknowledgements; an event-ID/body conflict is the separate inherited
  `409 stripe_webhook_conflict` contract;
- removing a Customer catalog provider read-model row makes its detail route
  exactly `404 catalog_business_not_found`;
- the device model has an inactive state and
  `STEP17-DET-DEVICE-002` verifies the exact `401 device_token_inactive`
  contract;
- deterministic rate-limit coverage is implemented for rows 007–010 and
  012–014. Rows 011 and 015, plus every row's declared live-local level, remain
  pending until the live gate executes. CORS has the secure no-opt-in contract
  in §3 and must not gain an allowlist.

No row below uses an unresolved status/code alternative. “Expected-red” means
the exact intended contract is frozen even though the present implementation
cannot yet satisfy it.

### 1.2 Common request and response profiles

Every row supplies its route, auth, relevant headers/body, expected status/code,
side effect, cleanup, level, and coverage. The following named profiles make
the repeated assertions exact rather than implicit:

- **REQ**: `Accept: application/json` and
  `X-Correlation-Id: corr-<scenario-id>`; JSON mutations also send
  `Content-Type: application/json`. Tokens/signatures are generated at runtime
  and never written to evidence.
- **DEV**: REQ plus `X-Device-Token:<current-device-token>`.
- **CUST**: DEV plus `Authorization: Bearer <customer-user-jwt>`.
- **BIZ**: REQ plus `Authorization: Bearer <business-jwt>` and, where shown,
  `Idempotency-Key:<scenario-id>`.
- **HMAC**: REQ plus `X-Ghseeli-Service-Id`, UTC
  `X-Ghseeli-Timestamp`, a fresh 32–128 character
  `X-Ghseeli-Nonce`, lowercase HMAC `X-Ghseeli-Signature`, and
  `Idempotency-Key:<scenario-id>` on POST. The signature covers the exact
  method, normalized path/query, timestamp, nonce, and body bytes. GET omits
  `Idempotency-Key`.
- **STRIPE**: REQ plus `Stripe-Signature` computed over the exact local UTF-8
  body with a generated local test webhook secret.
- **S**: exactly one bounded echoed `X-Correlation-Id`;
  `X-Content-Type-Options:nosniff`; `X-Frame-Options:DENY`;
  `Referrer-Policy:no-referrer`;
  `Permissions-Policy:camera=(), microphone=(), geolocation=(), payment=()`;
  `Content-Security-Policy:default-src 'none'; frame-ancestors 'none'; base-uri 'none'`;
  Business API responses append `form-action 'none'` to that CSP;
  no `Set-Cookie`, `ETag`, or `Last-Modified`; and no
  `Access-Control-Allow-Origin`, `Access-Control-Allow-Headers`, or
  `Access-Control-Allow-Credentials`.
- **J200**: status 200, `application/json`, `Cache-Control:no-store`, S, and
  the exact public fields named in the row; no secret, PII, private database
  ID, provider payload, or stack/SQL detail.
- **P(status,code,U)**: exact status and stable code,
  `application/problem+json`, `type=https://api.ghseeli.example/errors/{code}`,
  matching status/correlation, `Cache-Control:no-store`, and S. `U` means the
  selected `ar|he` language, exact catalog text, `Content-Language`, and
  `Vary:Accept-Language`; `M` means English machine problem with no `language`
  member or `Content-Language`.
- **RL-U**: `P(429,rate_limit_exceeded,U)` plus a positive
  `Retry-After` from 1 through 60 under the isolated 60-second test profile.
  Arabic uses title
  `تعذر إكمال الطلب.` and detail
  `تم تجاوز حد الطلبات. حاول مرة أخرى لاحقًا.`; Hebrew uses title
  `לא ניתן להשלים את הבקשה.` and detail
  `חרגת ממגבלת הבקשות. נסה שוב מאוחר יותר.`
- **RL-M**: `P(429,rate_limit_exceeded,M)` plus a positive
  `Retry-After` from 1 through 60, title `Too many requests.`, and detail
  `The request rate limit was exceeded. Retry after the indicated delay.`
  RL-U/RL-M emit no quota headers other than `Retry-After`.

All mutations retain run-scoped data only until their named verifier or the
end-of-section teardown. “Delete fixture” always means delete only rows tagged
with this run ID, in dependency order; disposable databases are dropped only
after both hosts stop and final invariants pass.

## 2. Fixtures, determinism, safety, and teardown

1. Create uniquely named, newly migrated Customer and Business SQL Server
   databases and separate least-privilege SQL principals. Assert each principal
   is denied the other database. Start Customer and Business on separate unused
   local HTTPS ports. Never use production, a shared database, production
   Stripe keys, production HMAC/JWT keys, or production URLs.
2. Seed minimal Arabic/Hebrew Customer configuration/read models and
   authoritative Business company, branch, catalog, availability, capacity,
   assignment, user, and HMAC grants. Use unique users, installations,
   order/reference/event GUIDs, nonces, idempotency keys, and correlation IDs.
3. A uses one recorded base UTC instant and real local HTTPS; it asserts
   normalized ordering rather than wall-clock equality. B/C use `TimeProvider`
   fixed at `2030-01-15T09:00:00Z`. Lease, expiry, retry, signature-window, and
   rate-window advancement is only through that provider.
4. Faults are named, one-shot testing-only DI failpoints: before-save,
   after-Business-accept/before-Customer-commit, after-domain-commit/before-
   response, response-stream-abort, SQL renewal failure, lease-owner loss,
   gateway timeout, malformed gateway result, and dependency unavailable.
   Production startup must reject enabling any failpoint.
5. Capture structured application logs in B/C. Assert absence of bearer/device
   tokens, HMAC/Stripe signatures and secrets, raw bodies, idempotency keys,
   emails, phones, addresses, names, payment/client secrets, provider IDs,
   connection strings, server/database names, SQL text, stack traces, and lease
   owner tokens. Committed evidence contains only IDs, counts, status/code,
   sanitized correlation IDs, and test names.
6. After each section, assert no cross-database FK/query/transaction, no
   Customer principal can read Business tables, no Business principal can read
   Customer tables, and public cross-system references/totals/statuses agree
   without private IDs. Stop exact host PIDs, verify listeners are closed,
   revoke/delete run principals, drop only the two run databases, remove
   ignored overlays/results/logs, and prove zero run artifacts remain.
7. Step 17 uses an injected deterministic fake payment gateway and locally
   signed webhook bodies only. It never confirms a real Stripe PaymentIntent,
   creates a charge, settles money, or claims external-network success.
   `STEP14-INTENT-REAL-STRIPE-057` remains exclusively a Step 18
   pre-deployment gate.

## 3. CORS and rate-limit decisions

### 3.1 No cross-origin opt-in

These are native-mobile/internal APIs. Neither host registers or invokes CORS
middleware. There is no wildcard, origin allowlist, credentialed CORS, or
preflight shortcut. For every origin—including `null`, same-host-looking,
trusted-looking, and hostile values—responses contain no ACAO, ACAH, or ACAC.
`OPTIONS` reaches normal routing and returns exact 405/404 contracts. Swagger
UI is same-origin only and also emits no CORS opt-in headers.

### 3.2 Behavior-safe application rate limiting

Use ASP.NET Core's built-in `System.Threading.RateLimiting`; add no package or
external dependency. Queue limit is zero. Rejections are the exact RL-U/RL-M
profiles. Limits and windows are configuration-bound and startup-validated;
zero/negative limits, nonpositive windows, missing partition material, and an
untrusted proxy configuration fail closed.

Production defaults (operators may lower/raise them through validated
configuration) are:

| Policy | Partition key | Permit/window |
|---|---|---:|
| Customer device registration | normalized installation SHA-256; trusted client IP only when the body cannot be parsed | 10 / 10 min |
| Customer register/login/validate/OAuth entry | trusted client IP + normalized account/action, with an additional 50 / 5 min trusted-IP aggregate | 10 / 1 min |
| Customer device reads/drafts/pricing | device-token hash + route family | 300 / 1 min |
| Customer bearer mutations | JWT `sub` + device hash + route family | 60 / 1 min |
| Payment intent | JWT `sub` + device hash + booking public ID | 10 / 1 min |
| Business auth | trusted client IP + normalized account/action, with the same aggregate rule | 10 / 1 min |
| Business reads | Business `sub` + company claim + route family | 300 / 1 min |
| Business mutations/admin | Business `sub` + company claim + route family | 120 / 1 min |
| Valid internal HMAC | authenticated service ID + operation | 600 / 1 min |
| Invalid/missing internal HMAC | trusted client IP + host | 60 / 1 min |
| Valid Stripe delivery | verified Stripe account/test fixture identity | 600 / 1 min |
| Invalid Stripe signature | trusted client IP + host | 60 / 1 min |
| Other anonymous routes | trusted client IP + route family | 60 / 1 min |

Only liveness/readiness is exempt: Customer `GET|HEAD /api/Health`,
Customer `GET /api/Health/db`, and Business `GET|HEAD /api/health`. Swagger is
not exempt. Forwarded client IP is honored only from configured trusted proxies;
otherwise the socket peer is the partition value. There is no anonymous
IP-only outer bucket over authenticated device/JWT, valid HMAC, or valid Stripe
traffic. Therefore NATed devices remain independent, internal retries retain
service/operation capacity, and valid Stripe retries are not consumed by an
invalid-signature bucket.

The deterministic test profile uses isolated 60-second fixed windows, no queue:
device registration 2/installation; auth 3/account plus 6/IP aggregate; device
read 3/device; Customer/Business mutation 2/identity+route; payment
2/user+device+booking; valid HMAC 5/service+operation; invalid HMAC 3/IP; valid
Stripe 5/account; invalid Stripe 3/IP; other anonymous 3/IP. `Retry-After` is
required to be a positive whole-second value from `1` through `60`.

## 4. A — chained live-local journey and recovery (39)

All rows are `status=planned`; execution is automated live-local HTTPS. A row
that creates data retains it for later chained rows and names final cleanup.

| ID | Request, auth, setup, headers, body | Exact response and header assertions | Side effects and cleanup | Existing coverage / gap |
|---|---|---|---|---|
| `STEP17-E2E-DEVICE-001` | `POST /api/v1/devices/register`; anonymous REQ; body `{"installationId":"<run>-d1","platform":"Android","appVersion":"17.0"}`; no current token. | J200; nonempty opaque `token`, public device ID, future expiry; code none. | One Customer device; retain token as `d1`; delete in A39. | Chains `STEP7-NEW-TOKEN-VALID-010`. |
| `STEP17-E2E-DEVICE-002` | Repeat the exact POST/body from 001; anonymous REQ; omit `X-Device-Token`. | P(409,`device_registration_conflict`,U). | Still one device; hash/expiry unchanged; A39 cleanup. | Exact correction of `STEP7-DUPLICATE-WITHOUT-TOKEN-006`. |
| `STEP17-E2E-DEVICE-003` | Repeat POST/body; anonymous REQ plus `X-Device-Token:<d1>`. | J200; new token differs from d1. | One row, token hash/expiry/version rotate once; retain `d2`; prove d1 invalid; A39 cleanup. | Chains `STEP7-ROTATE-008`. |
| `STEP17-E2E-CONFIG-004` | `GET /api/v1/configuration?language=ar`; DEV(d2); body none. | J200 + `Content-Language:ar`, `Vary:Accept-Language`; active version and Arabic values. | Read only. | `STEP8-OVERRIDE-AR-008`. |
| `STEP17-E2E-CATALOG-005` | `GET /api/v1/catalog/categories?refresh=true`; DEV(d2), `Accept-Language:he`; body none. | J200 + Hebrew language headers; seeded category present with public IDs only. | Synchronizes the run-scoped Business graph into the Customer read model. | `STEP9-CATEGORIES-BUSINESS-HE-011`. |
| `STEP17-E2E-CATALOG-006` | `GET /api/v1/catalog/businesses?language=he`; DEV(d2); body none. | J200 + Hebrew headers; seeded provider/branch eligible and localized. | Read only. | `STEP9-BUSINESSES-HE-008`. |
| `STEP17-E2E-CATALOG-007` | `GET /api/v1/catalog/businesses/<providerId>?language=he`; DEV(d2); body none. | J200; exact provider/branch public references and Hebrew fallback rules. | Read only. | `STEP9-BUSINESS-DETAIL-OVERRIDE-HE-012`. |
| `STEP17-E2E-CATALOG-008` | `GET /api/v1/catalog/businesses/<providerId>/offerings?categoryId=<categoryId>`; DEV(d2); body none. | J200; only eligible seeded offering and branch. | Read only. | `STEP9-BUSINESS-OFFERINGS-HE-014`. |
| `STEP17-E2E-CATALOG-009` | `GET /api/v1/catalog/offerings/<offeringId>?language=ar`; DEV(d2); body none. | J200; exact price, currency, duration, add-on min/max/default rules and both stored languages. | Read only. | `STEP9-OFFERING-DETAIL-OVERRIDE-AR-016`. |
| `STEP17-E2E-DRAFT-010` | `POST /api/v1/checkout/drafts`; DEV(d2); body has provider/branch, one offering/add-on, UTC slot, vehicle/location public facts. | J200; order GUID, `version=1`, state editable, no authoritative price snapshot. | One draft/items/selections owned by d2; retain; A39 cleanup. | Exact 200 from `STEP10-CREATE-DEFAULT-005`. |
| `STEP17-E2E-DRAFT-011` | `GET /api/v1/checkout/drafts/<orderGuid>?language=he`; DEV(d2); body none. | J200; exact order/device intent, version 1, Hebrew presentation. | Read only. | `STEP10-READ-HE-010`. |
| `STEP17-E2E-DRAFT-012` | `PUT /api/v1/checkout/drafts/<orderGuid>?language=ar`; DEV(d2); body repeats full intent with changed allowed note and `expectedVersion:1`. | J200; `version=2`, pricing invalid/unset, exact changed intent. | Draft updates once; retain. | `STEP10-UPDATE-AR-011`. |
| `STEP17-E2E-PRICE-013` | `POST /api/v1/pricing/reprice`; DEV(d2); body is the complete draft-equivalent intent at catalog version. | J200; authoritative item/add-on/tax/fee/total/currency/duration/slot proof; no client money accepted. | No Customer draft/version mutation and no Business reservation. | `STEP11-DIRECT-AUTHORITATIVE-014`. |
| `STEP17-E2E-PRICE-014` | `POST /api/v1/checkout/reprice`; DEV(d2) plus `X-Order-Guid:<orderGuid>`; body `{"expectedVersion":2}`. | J200; `version=3`, authoritative snapshot equals row 013. | Persist one pricing snapshot and version bump; retain. | `STEP11-DRAFT-REPRICE-018`. |
| `STEP17-E2E-PRICE-015` | Repeat row 013 with a new correlation ID and byte-equivalent semantic body. | J200; every money, currency, duration, catalog-version and selection proof equals row 014. | No new draft/snapshot/reservation/payment row. | New chained equality proof. |
| `STEP17-E2E-AUTH-016` | `POST /api/Auth/register`; anonymous REQ; body has unique run email/password/name and requested role `Admin`. | J200; registered role is exactly `User`; no admin/company role. | One Customer user; retain; delete via disposable DB. | Existing Customer auth regression, newly chained. |
| `STEP17-E2E-AUTH-017` | `POST /api/Auth/login`; anonymous REQ; body has row-016 credentials. | J200; Customer issuer/audience JWT with `User` role, no Business claims; no token in logs. | No domain row; hold JWT in memory only. | Existing auth regression, newly chained. |
| `STEP17-E2E-BOOKING-018` | `POST /api/v1/bookings/from-draft?language=ar`; CUST(d2) plus `X-Order-Guid:<orderGuid>`; body `{"expectedVersion":3,"cancellationPolicyAcknowledged":true}`. | J200; Customer booking, Business reservation/work-order refs, Pending status, slot, total, currency, duration, items exactly match snapshots. | Exactly one Customer booking/attempt and one Business reservation/work order; retain. | `STEP12-CREATE-HAPPY-021` and SQL verifier. |
| `STEP17-E2E-DRAFT-019` | `GET /api/v1/checkout/drafts/<orderGuid>`; DEV(d2); body none. | J200; terminal confirmed/noneditable state and version 3. | No duplicate confirmation. | `STEP12-DRAFT-POSTCONFIRM-GET-029`. |
| `STEP17-E2E-STATUS-020` | `POST /api/v1/business/work-orders/<workOrderId>/transitions`; BIZ assigned employee plus `Idempotency-Key:<scenario-id>`; body `{"status":"Confirmed"}`. | J200; status Confirmed, sequence 1, stable event/public refs. | Business reservation/work order transition atomically; one outbox event. | `STEP13-BUSINESS-TRANSITION-067`. |
| `STEP17-E2E-STATUS-021` | `POST /api/v1/internal/bookings/status`; reverse-direction HMAC; exact row-020 event body, event ID and sequence 1. | J200; `applied=true`, `stale=false`, exact refs/status/sequence. | One Customer inbox row and state Confirmed. | `STEP13-CALLBACK-APPLY-050`. |
| `STEP17-E2E-STATUS-022` | Business transition POST as in 020 with new key/body `{"status":"InProgress"}`, then HMAC callback POST with its exact event/sequence 2. | Both responses J200; Customer and Business end InProgress sequence 2. | One additional outbox and inbox; capacity remains occupied. | Step 13 transition/callback chain. |
| `STEP17-E2E-STATUS-023` | Business transition POST with `{"status":"Completed"}`, then exact HMAC callback POST at sequence 3. | Both J200; both systems Completed sequence 3. | One outbox/inbox; capacity released once. | Step 13 terminal/capacity regression. |
| `STEP17-E2E-RECONCILE-024` | `GET /api/v1/internal/bookings/<bookingReference>`; HMAC read grant; body none. | J200; Customer refs, Completed/3; no PII, price, item, or private IDs. | Read only. | `STEP13-RECONCILE-CUSTOMER-READ-071`. |
| `STEP17-E2E-RECONCILE-025` | `POST /api/v1/internal/bookings/<bookingReference>/reconcile`; HMAC reconcile grant and idempotency key; body empty. | J200; no-op, authoritative Completed/3, `applied=false`. | No duplicate inbox/state/outbox; one completed transport record. | `STEP13-RECONCILE-THEN-REAL-080A`. |
| `STEP17-E2E-PAYMENT-026` | Seed a second payable Pending booking through the same local chain; `POST /api/v1/payments/intents?language=ar`; CUST(d2), `Idempotency-Key:<id>`; body `{"bookingId":"<payableBookingRef>","method":"Card"}`; deterministic fake gateway. | J200; one pending payment, fake client-safe secret/key, exact immutable amount/currency; explicitly no external call. | One payment and scoped idempotency row; fake gateway called once; retain. | Replaces audit's real-Stripe ambiguity; not scenario 057. |
| `STEP17-E2E-PAYMENT-027` | `GET /api/v1/payments/<paymentId>`; CUST(d2); body none. | J200; owned Pending Card payment and client-safe fields only. | Read only. | `STEP14-READ-OWNED-062`. |
| `STEP17-E2E-WEBHOOK-028` | `POST /api/stripe/webhook`; STRIPE; exact local `payment_intent.succeeded` event for row 026 with matching intent, metadata, amount, currency. | J200; exact `{"received":true}`, machine-neutral headers, code none. | Durable event; payment Completed and booking paid atomically. | `STEP14-WEBHOOK-SUCCESS-079`. |
| `STEP17-E2E-WEBHOOK-029` | Repeat row 028 exact event ID/body with a newly computed valid local signature. | J200 same acknowledgement. | One event receipt and no second state transition. | `STEP14-WEBHOOK-DUPLICATE-SAME-084`. |
| `STEP17-E2E-WEBHOOK-030` | For an independent fake-gateway Pending payment, POST valid local `payment_intent.payment_failed` with exact matching proof. | J200 acknowledgement. | One receipt; payment Failed, booking unpaid; no regression of row-026 payment. | `STEP14-WEBHOOK-FAILURE-080`. |
| `STEP17-E2E-WEBHOOK-031` | POST valid local `charge.refunded` for the row-028 paid payment/charge proof. | J200 acknowledgement. | One receipt; payment Refunded and booking paid flag cleared exactly once. | `STEP14-WEBHOOK-REFUND-082`. |
| `STEP17-E2E-CROSSDB-032` | HMAC GET Customer booking state and HMAC GET Business reservation state; then read-only SQL verifiers with each owning principal. | Both J200; refs/status/sequence agree; SQL verifier exits 0. | No mutation; no cross-database access; retain for recovery. | New full-journey invariant. |
| `STEP17-E2E-RECOVERY-033` | Stop Business PID; GET Customer catalog business detail with DEV(d2), then POST direct reprice with DEV(d2) and valid body. | Catalog J200 from Customer read model; reprice P(503,`pricing_unavailable`,U). | No draft/version/reservation mutation; Business remains stopped. | New dependency-separation chain. |
| `STEP17-E2E-RECOVERY-034` | Restart Business against same Business DB; retry exact row-033 reprice with new correlation and same semantic body. | J200 authoritative quote; readiness succeeds. | No stale partial Customer mutation and no reservation. | Recovery gap. |
| `STEP17-E2E-RECOVERY-035` | On an independent Pending fixture, BIZ POST `/api/v1/business/work-orders/<recoveryWorkOrderId>/transitions` with key/body `{"status":"Confirmed"}`; after its J200 stop Customer, dispatch its exact HMAC callback, restart Customer, retry callback with stable key/body/event/correlation and fresh nonce. | Transition J200; first callback has a transport failure/no HTTP response; retry callback J200 and Customer is Confirmed/1. | One durable Business outbox event, one Customer inbox transition; retain until final teardown. | Step 13 retry behavior, new live chain. |
| `STEP17-E2E-RECOVERY-036` | Delete only active run configuration row; `GET /api/v1/configuration`; DEV(d2); body none. | P(503,`configuration_unavailable`,U), never empty 200. | No replacement/fake configuration row. | Current configuration contract. |
| `STEP17-E2E-RECOVERY-037` | Restore the exact configuration fixture; repeat row-036 GET. | J200 with original active version and values. | Exactly one active fixture row. | Recovery gap. |
| `STEP17-E2E-RECOVERY-038` | Delete only run provider read-model graph; `GET /api/v1/catalog/businesses/<providerId>`; DEV(d2); body none. | P(404,`catalog_business_not_found`,U). | Business authoritative rows untouched; no Customer foreign query. | Corrected audit ambiguity. |
| `STEP17-E2E-RECOVERY-039` | Restore one read-model graph through the supported fixture/sync path; repeat row-038 GET; run final verifiers and teardown. | J200 exact provider graph; all A SQL/process/listener/artifact verifiers pass. | No duplicate read-model rows; stop hosts, drop run DBs/principals, remove ignored artifacts. | Final chained recovery/cleanup gate. |

## 5. B — deterministic TestServer/SQL/failpoint (48)

All rows are `status=planned`, use isolated WebApplicationFactory/TestServer
hosts and real disposable SQL unless a row names a fake dependency, and run
under the fixed clock/failpoint rules in §2. Every rejection asserts zero
unlisted calls and rows.

| ID | Level; request, auth, setup, headers, body | Exact response and header assertions | Side effects and cleanup | Existing coverage / gap |
|---|---|---|---|---|
| `STEP17-DET-DEVICE-001` | TestServer+SQL; expire d1 at fixed now; `GET /api/v1/configuration`; DEV(d1); body none. | P(401,`device_token_expired`,U). | No configuration/domain mutation; delete device fixture. | Existing device expiry tests; release rerun. |
| `STEP17-DET-DEVICE-002` | TestServer+SQL; mark a current device inactive in the intended model; same GET/DEV; body none. | **Expected-red** P(401,`device_token_inactive`,U). | No handler/domain mutation; delete fixture. | Gap: present model/code absent; exact intended contract frozen. |
| `STEP17-DET-DEVICE-003` | TestServer+SQL; rotate d1 to d2; `POST /api/v1/bookings/from-draft`; Customer JWT plus old d1, valid order header/body. | P(401,`device_token_invalid`,U). | No confirmation or Business call; delete fixtures. | Step 12 rotated-token regression. |
| `STEP17-DET-AUTH-004` | TestServer+SQL; draft belongs to device A/user A; confirmation POST uses user A JWT plus current device B and valid `X-Order-Guid`; body `{"expectedVersion":2,"cancellationPolicyAcknowledged":true}`. | P(404,`checkout_draft_not_found`,U). | No Business call/booking/attempt; delete draft/devices. | Exact correction of `STEP12-OWNERSHIP-024`. |
| `STEP17-DET-AUTH-005` | TestServer+SQL; booking belongs to user A/device A; `POST /api/v1/payments/intents`; user B/device B CUST, new key, Card body for A booking. | P(404,`booking_not_found`,U). | No payment/key/gateway call; delete booking fixtures. | `STEP14-OWNERSHIP-WRONG-USER-015`. |
| `STEP17-DET-AUTH-006` | TestServer+SQL; company-A work order; company-B assigned employee BIZ; transition POST, key, body `{"status":"Confirmed"}`. | P(404,`BOOKING_NOT_FOUND`,U). | No status/sequence/outbox change; delete work order. | Exact non-enumeration outcome for Step 13 wrong owner. |
| `STEP17-DET-AUTH-007` | TestServer; Customer JWT on Business transition POST with otherwise-valid key/body. | P(401,`business_authentication_required`,U). | No controller service call; clear host. | Cross-host auth regression. |
| `STEP17-DET-AUTH-008` | TestServer; Business JWT plus valid Customer device on payment-intent POST with valid key/body. | P(401,`customer_authentication_required`,U). | No payment/key/gateway call; clear host. | Cross-host auth regression. |
| `STEP17-DET-DRAFT-009` | TestServer+SQL; advance fixed clock to exact draft expiry; `GET /api/v1/checkout/drafts/<orderGuid>`; DEV; body none. | P(410,`checkout_draft_expired`,U). | Version/snapshot unchanged; delete draft. | Existing service boundary, new release HTTP evidence. |
| `STEP17-DET-DRAFT-010` | Same expired fixture; `PUT /api/v1/checkout/drafts/<orderGuid>`; DEV; full valid body with current expected version. | P(410,`checkout_draft_expired`,U). | No intent/version/snapshot update; delete draft. | HTTP mutation-expiry gap. |
| `STEP17-DET-DRAFT-011` | Same expired fixture; `POST /api/v1/checkout/reprice`; DEV + order header; body `{"expectedVersion":2}`. | P(410,`checkout_draft_expired`,U). | No pricing call/snapshot/version change; delete draft. | HTTP pricing-expiry gap. |
| `STEP17-DET-DRAFT-012` | Same expired priced fixture; confirmation POST with CUST/order header/current version/acknowledgement. | P(410,`checkout_draft_expired`,U). | No attempt/booking/Business call; delete draft. | Step 12 automated expiry release case. |
| `STEP17-DET-DRAFT-013` | Current version 3; draft PUT with DEV and otherwise-valid full body `expectedVersion:2`. | P(409,`checkout_draft_version_conflict`,U). | No update/version change; delete draft. | `STEP10-CONFLICT-012`. |
| `STEP17-DET-DRAFT-014` | Current version 3; checkout reprice POST with DEV/order header and `{"expectedVersion":2}`. | P(409,`checkout_draft_version_conflict`,U). | No Business call/snapshot/version change; delete draft. | `STEP11-DRAFT-STALE-VERSION-020`. |
| `STEP17-DET-PRICE-015` | TestServer+SQL; update authoritative Business offering from 100 to 120 ILS; direct reprice POST with DEV and otherwise-current catalog body. | J200; quote is exactly 120 plus server-derived add-ons/tax/fees, not the prior 100. | No draft/reservation/payment; restore/delete offering. | Corrects audit: fresh direct reprice is authoritative success. |
| `STEP17-DET-PRICE-016` | Fake Business returns `STALE_CATALOG_VERSION` twice despite one read-model refresh; direct reprice POST with DEV/valid body. | P(503,`pricing_unavailable`,U). | Exactly two validation attempts and one refresh; no Customer mutation; reset fake. | Current repeated-stale mapping; release gap. |
| `STEP17-DET-PRICE-017` | Business client throws configured unavailable before direct validation; direct reprice POST with DEV/valid body. | P(503,`pricing_unavailable`,U). | No Customer draft or Business row; reset fake. | Existing unavailable service test. |
| `STEP17-DET-PRICE-018` | Business client unavailable for checkout reprice; POST with DEV/order header/current version. | P(503,`pricing_unavailable`,U). | Existing draft/version/snapshot byte-equivalent; reset fake/delete draft. | HTTP draft-unavailable gap. |
| `STEP17-DET-BOOK-019` | Priced draft version 4; confirmation POST with CUST/order header and `expectedVersion:3`, acknowledgement true. | P(409,`checkout_draft_version_conflict`,U). | No Business call/attempt/booking; delete draft. | `STEP12-VERSION-025`. |
| `STEP17-DET-BOOK-020` | Confirmation POST with CUST/order header/current version; body `{"expectedVersion":4,"cancellationPolicyAcknowledged":false}`. | P(400,`booking_request_invalid`,U). | No confirmation/Business call; delete draft. | `STEP12-BODY-POLICY-018`. |
| `STEP17-DET-BOOK-021` | Fake Business rejects valid confirmation with `SLOT_UNAVAILABLE`; CUST/order/current body. | P(409,`booking_reservation_rejected`,U) and exact `businessErrorCode:SLOT_UNAVAILABLE`. | No Customer booking/attempt success; no Business reservation; reset fake/delete draft. | Step 12 slot mapping. |
| `STEP17-DET-BOOK-022` | Change authoritative Business price after draft proof; confirmation POST with current Customer version/proof. | P(409,`checkout_draft_requires_reprice`,U) and `businessErrorCode:PRICE_CHANGED`. | No booking/reservation; draft pricing invalidated exactly once; delete fixtures. | `STEP12-BUSINESS-PRICE-035B`. |
| `STEP17-DET-BOOK-023` | Launch exactly two concurrent byte-identical confirmation POSTs with same CUST/order/version/body. | Both J200 with byte-equivalent logical booking/public refs. | Exactly one attempt, booking, reservation, work order; delete all. | Expected-red if current race returns conflict; deterministic HTTP concurrency gap. |
| `STEP17-DET-BOOK-024` | One-shot failpoint after Business 200 acceptance and before Customer commit; confirmation POST with valid CUST/order/body. | P(503,`booking_confirmation_unavailable`,U). | Business has one reservation/work order; Customer has no committed booking and retains retry-safe attempt state; clear failpoint. | Expected-red dedicated ambiguous-commit gap from Step 12. |
| `STEP17-DET-BOOK-025` | Disable failpoint; retry exact row-024 confirmation identity/body. | J200 with the already accepted Business refs. | Exactly one Customer booking/attempt and one Business reservation/work order; delete all. | Completes row-024 recovery proof. |
| `STEP17-DET-BOOK-026` | SQL serializable fixture capacity=1; send two concurrent HMAC reservation POSTs for distinct orders, fresh nonces/keys, same slot; complete valid bodies. | Exactly one J200; exactly one P(409,`SLOT_UNAVAILABLE`,M). | One reservation/work order, capacity never >1; delete fixtures. | Existing relational service proof, new HTTP exactness. |
| `STEP17-DET-BOOK-027` | Create HMAC reservation with ordered items/add-ons, then replay POST with same order/semantic values reordered, fresh transport key/nonce. | Both J200 with identical refs/result. | One reservation/work order; transport records may differ but domain row does not; delete. | `STEP12-BUSINESS-CANONICAL-REPLAY-038`. |
| `STEP17-DET-BOOK-028` | After valid reservation, HMAC POST with same order GUID but changed add-on quantity, fresh key/nonce. | P(409,`RESERVATION_INVALID`,M). | Original reservation/hash/result unchanged; no second work order; delete. | Exact `STEP12-BUSINESS-ORDER-CONFLICT-039` mapping. |
| `STEP17-DET-STATUS-029` | HMAC callback POST for event E1, then exact replay with fresh nonce and same key/body. | Both J200; second response is original logical result. | One inbox/state transition/transport completion; delete fixture. | Step 13 same-event/idempotency replay. |
| `STEP17-DET-STATUS-030` | After E1, HMAC callback POST with same event ID, new key/nonce, changed status/sequence body. | P(409,`BOOKING_STATUS_EVENT_CONFLICT`,M). | Original event hash/state/sequence unchanged; delete. | `STEP13-CALLBACK-EVENT-CONFLICT-055`. |
| `STEP17-DET-STATUS-031` | Apply sequence 2 first; HMAC callback POST a distinct older event sequence 1 with a valid earlier status. | J200; exact `stale:true, applied:false`, final sequence remains 2. | One non-applied inbox identity, no rollback; delete. | Step 13 out-of-order rule. |
| `STEP17-DET-STATUS-032` | Completed sequence-3 booking; HMAC callback POST new event sequence 4/body status InProgress. | P(409,`BOOKING_TRANSITION_INVALID`,M). | Terminal state/sequence unchanged; delete. | Step 13 terminal-new rule. |
| `STEP17-DET-STATUS-033` | Pending sequence 0; HMAC callback POST sequence 2/body status Confirmed (directly allowed edge). | J200; `applied:true`, Confirmed/2. | One inbox/state update; delete. | `STEP13-CALLBACK-SEQUENCE-GAP-059`. |
| `STEP17-DET-STATUS-034` | Pending sequence 0; HMAC callback POST sequence 2/body status Completed (disallowed edge). | P(409,`BOOKING_TRANSITION_INVALID`,M). | No state/sequence update; delete. | Exact disallowed-gap coverage gap. |
| `STEP17-DET-STATUS-035` | Response-abort failpoint after Customer callback commit; first HMAC POST E1 yields client transport exception/no HTTP response; retry exact key/body with fresh nonce. | Retry J200 original result. | One completed state/inbox/transport record; clear failpoint/delete. | Deterministic ambiguous-response gap. |
| `STEP17-DET-STATUS-036` | Client cancels HMAC callback request immediately after domain-start barrier; server token remains independent; retry same key/body/fresh nonce after completion. | First client observes cancellation/no response; retry J200 original result. | One durable domain execution; claim completed or safely replayable; delete. | Existing relational TestServer cancellation test, release rerun. |
| `STEP17-DET-STATUS-037` | SQL renewal fails once before safety deadline during delayed HMAC callback; fixed clock advances below deadline. | J200 after bounded renewal retry. | One state/inbox; owner and lease cleared; no busy loop; clear failpoint/delete. | Existing heartbeat unit/relational proof, HTTP gap. |
| `STEP17-DET-STATUS-038` | Hold first HMAC callback past lease without renewal, advance beyond expiry, send identical second request to reclaim, then release stale first generation. | Second request J200; stale first receives P(503,`idempotency_unavailable`,M). | One state/inbox; successor completion survives; stale owner cannot overwrite/delete; clear failpoint/delete. | Expected-red exact HTTP owner-loss contract. |
| `STEP17-DET-STATUS-039` | Start HMAC reconcile POST and exact next real callback POST at one barrier for same booking. | Both J200; final directly allowed status/sequence is identical regardless winner. | One real event identity and one monotonic mutation; no duplicate transition; delete. | Existing simultaneous reconciliation relational test. |
| `STEP17-DET-STATUS-040` | Seed sequence-1 outbox DeadLetter and sequence-2 Pending; invoke one dispatcher pass; HMAC GET Customer current state. | GET J200 still at pre-sequence state; no sequence-2 callback occurred. | Sequence 2 remains blocked, sequence 1 dead-letter unchanged; delete outbox fixtures. | Existing head-of-line ordering test, HTTP read evidence gap. |
| `STEP17-DET-STATUS-041` | Admin BIZ POST `/api/v1/business/admin/booking-status-outbox/<eventId>/requeue` twice with the same valid key; body empty. | Both J200; first `result:Requeued,generation:1`, second `result:AlreadyRequeued,generation:1`. | One generation increment/history row; delete fixtures. | `STEP13-BUSINESS-ADMIN-REQUEUE-*`. |
| `STEP17-DET-STATUS-042` | Admin BIZ requeue POST with a new key for a Pending (not DeadLetter) event; body empty. | P(409,`booking_status_event_not_requeueable`,U). | No generation/history/status change; delete fixture. | Existing controller/service conflict coverage. |
| `STEP17-DET-PAY-043` | Empty/missing Stripe options; payment-intent POST with CUST, valid key/payable Card body. | P(503,`payment_provider_unavailable`,U). | No provider object/payment/completed key; delete booking. | `STEP14-PROVIDER-UNCONFIGURED-056`. |
| `STEP17-DET-PAY-044` | Fake gateway throws `TimeoutException`; otherwise-valid payment-intent POST. | P(503,`payment_gateway_ambiguous`,U). | One retry-safe pending logical payment/key, no paid state; lease released; reset fake/delete. | Step 14 timeout contract. |
| `STEP17-DET-PAY-045` | Fake gateway returns intent result with mismatched amount/currency; otherwise-valid payment-intent POST. | P(502,`payment_gateway_ambiguous`,U). | Retry-safe pending logical payment; no client/provider secret leakage; lease released; reset/delete. | Exact current malformed-result mapping. |
| `STEP17-DET-PAY-046` | STRIPE POST valid local succeeded event with matching IDs but amount +1 minor unit. | J200 acknowledgement. | Durable ignored/quarantined receipt; payment Pending, booking unpaid; delete. | `STEP14-WEBHOOK-AMOUNT-MISMATCH-093`. |
| `STEP17-DET-PAY-047` | STRIPE POST valid local succeeded event with matching IDs/amount but currency `usd` instead of `ils`. | J200 acknowledgement. | Durable ignored receipt; no paid mutation; delete. | `STEP14-WEBHOOK-CURRENCY-MISMATCH-094`. |
| `STEP17-DET-PAY-048` | Paid payment fixture; STRIPE POST valid local `payment_intent.payment_failed` event with all matching proof. | J200 acknowledgement. | Durable recognized no-op; payment remains Completed and booking paid; delete. | Step 14 legal-ordering regression. |

## 6. C — host security and transport (31)

All rows are `status=planned`. CORS rows are contract+TestServer and the named
live checks; rate rows use the exact test profile in §3.2. The implemented
TestServer rate rows are green; declared live-local checks remain pending.
Body sizes are UTF-8 byte counts, not .NET character counts.

| ID | Level; request, auth, setup, headers, body | Exact response and header assertions | Side effects and cleanup | Existing coverage / gap |
|---|---|---|---|---|
| `STEP17-SEC-CORS-001` | TestServer+live; `GET /api/v1/configuration`; DEV; no `Origin`; body none. | J200; ACAO/ACAH/ACAC all absent. | Read only; delete device fixture. | No-CORS baseline gap. |
| `STEP17-SEC-CORS-002` | TestServer+live; same GET/DEV with `Origin:https://evil.example`; body none. | J200 with identical JSON to 001; ACAO/ACAH/ACAC absent. | Read only. | Proves hostile origin is not opted in. |
| `STEP17-SEC-CORS-003` | TestServer; same GET/DEV with `Origin:https://127.0.0.1:<customer-port>`; body none. | J200; no CORS headers even for same-host-looking origin. | Read only. | Freezes no allowlist/product exception. |
| `STEP17-SEC-CORS-004` | TestServer+live; `OPTIONS /api/v1/payments/intents`; anonymous REQ plus `Origin:https://app.example`, `Access-Control-Request-Method:POST`, `Access-Control-Request-Headers:authorization,x-device-token,idempotency-key`; body none. | P(405,`method_not_allowed`,U), `Allow:POST`; ACAO/ACAH/ACAC absent. | No auth/payment/provider/idempotency side effect. | Expected-red if host currently bypasses exact fallback. |
| `STEP17-SEC-CORS-005` | TestServer; `OPTIONS /api/v1/internal/reservations`; anonymous REQ plus hostile Origin and requested HMAC/idempotency headers; body none and no actual HMAC headers. | P(405,`method_not_allowed`,M), `Allow:POST`; ACAO/ACAH/ACAC absent. | No nonce/idempotency/reservation row. | No-CORS internal preflight gap; normal routing resolves `OPTIONS` before endpoint HMAC metadata applies. |
| `STEP17-SEC-CORS-006` | TestServer+live; `GET /swagger/index.html`; anonymous, `Origin:null`, `Accept:text/html`; body none, Development host. | 200 `text/html`; CSP exactly `default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'none'`; Permissions-Policy exactly `camera=(), microphone=(), geolocation=()`; remaining S headers; ACAO/ACAH/ACAC absent. | Read only; stop host after group. | Same-origin-only Swagger decision; expected-red if Customer still emits API CSP. |
| `STEP17-SEC-RATE-007` | TestServer fixed window; for installations d1 and d2 behind one peer, send register new then rotate (current token) for each; send a third valid rotation for d1. | First four J200; d1 third request RL-U; d2 was not blocked by d1 or shared NAT. | Exactly two device rows and two successful rotations; rejected request does not rotate; advance window/delete. | Automated; installation partition/NAT safety. |
| `STEP17-SEC-RATE-008` | TestServer fixed window; send four valid `POST /api/Auth/login` requests for one account/peer with REQ and exact valid credential body. | First three 200 JSON JWT responses with S/no-store; fourth RL-U. | No account mutation or token logging; advance/reset limiter/delete user. | Automated; auth account bucket. |
| `STEP17-SEC-RATE-009` | TestServer fixed window; send six valid login POSTs for six accounts from one peer, then a seventh valid account login. | First six 200; seventh RL-U due exact trusted-IP aggregate despite unused seventh account partition. | No account mutation; advance/reset/delete users. | Automated; aggregate spray protection. |
| `STEP17-SEC-RATE-010` | TestServer+SQL fixed window; send three identical valid payment-intent POSTs with CUST, same booking/key/body and fake gateway. | First two J200 same logical result; third RL-U. | One payment/key/provider call; rejected request adds nothing; advance/delete. | Automated; payment partition and idempotency safety. |
| `STEP17-SEC-RATE-011` | TestServer+live fixed window; send four Stripe webhook POSTs from one peer with well-formed event body and deliberately invalid signature. | First three P(400,`stripe_signature_invalid`,M); fourth RL-M. | Zero webhook-event/payment/booking mutation; advance invalid bucket. | Expected-red; invalid-signature bucket. |
| `STEP17-SEC-RATE-012` | Without advancing row-011 invalid bucket, send one STRIPE POST from the same peer with a valid local signed matching event. | J200 acknowledgement; no 429. | One durable event/application; proves valid partition is independent; delete receipt/payment fixture. | Automated; valid Stripe delivery safety. |
| `STEP17-SEC-RATE-013` | TestServer fixed window; send four internal callback POSTs from one peer with a known service ID/timestamp/nonce/body but signatures made with a wrong secret. | First three P(401,`internal_auth_invalid_signature`,M); fourth RL-M. | No nonce/idempotency/inbox rows; advance invalid bucket. | Automated; invalid-HMAC bucket. |
| `STEP17-SEC-RATE-014` | Same peer; valid HMAC client sends one callback POST; injected downstream unavailability makes attempts 1–2 P(503,`idempotency_unavailable`,M); client retries with same key/body/correlation and fresh nonce; attempt 3 after fault removal. | Third attempt J200, never 429; all responses have S and correct cache policy. | Exactly one domain transition/completed transport result; three accepted nonces as policy permits; clear failpoint/delete. | Automated; valid internal retry must not share anonymous bucket. |
| `STEP17-SEC-RATE-015` | TestServer+live behind no trusted proxy; send four valid login POSTs for one account/socket peer, varying `X-Forwarded-For` as `10.0.0.1`…`10.0.0.4`. | First three 200; fourth RL-U; forged values absent from response/log. | No account mutation; limiter used socket peer; reset/delete. | Expected-red; proxy spoofing gap. |
| `STEP17-SEC-BOUND-016` | TestServer+live; device registration POST anonymous REQ with syntactically valid JSON exactly 65,536 bytes but overlong `appVersion`. | P(400,`request_invalid`,U), explicitly not 413. | No device row; remove generated body artifact. | Existing generic boundary, exact lower edge. |
| `STEP17-SEC-BOUND-017` | Same route with syntactically valid UTF-8 JSON exactly 65,537 bytes. | P(413,`request_body_too_large`,U). | Body stops at max+1; no device row; remove body artifact. | Current `CustomerHttpPolicyMiddleware` contract. |
| `STEP17-SEC-BOUND-018` | TestServer+live; draft-create POST DEV with valid required body plus ignored `_padding`, exact total 65,536 bytes. | J200; binding/service reached, not 413. | One draft only; delete draft/body artifact. | Step 10 boundary regression. |
| `STEP17-SEC-BOUND-019` | Same draft route/body at exactly 65,537 bytes. | P(413,`request_body_too_large`,U). | No draft/item/selection; remove body artifact. | Exact current generic draft-size code. |
| `STEP17-SEC-BOUND-020` | TestServer+live; direct reprice POST DEV, valid body plus padding, exactly 65,537 bytes. | P(413,`pricing_request_body_too_large`,U). | No Business call/Customer mutation; remove artifact. | `STEP11-DIRECT-SIZE-012`. |
| `STEP17-SEC-BOUND-021` | TestServer+live; checkout reprice POST DEV/order header, body exactly 65,537 bytes. | P(413,`pricing_request_body_too_large`,U). | Draft/version/snapshot unchanged; remove artifact/delete draft. | `STEP11-DRAFT-SIZE-013`. |
| `STEP17-SEC-BOUND-022` | TestServer+live; confirmation POST CUST/order header, valid body plus padding, exactly 65,537 bytes. | P(413,`booking_request_body_too_large`,U). | No confirmation/Business call; remove artifact/delete draft. | Step 12 body-size contract. |
| `STEP17-SEC-BOUND-023` | TestServer+live; payment-intent POST CUST/key, valid Card body plus padding, exactly 65,537 bytes. | P(413,`payment_request_too_large`,U). | No payment/key/gateway call; remove artifact/delete booking. | `STEP14-BODY-OVERSIZE-*`. |
| `STEP17-SEC-BOUND-024` | TestServer+live; webhook POST with locally signed exact UTF-8 event body of 65,537 bytes. | P(413,`stripe_webhook_too_large`,M). | No event/payment/booking mutation; remove artifact. | Step 14 webhook bound. |
| `STEP17-SEC-BOUND-025` | TestServer+live; chunked HMAC callback POST with valid prefix/signature fixture that streams byte 65,537 without `Content-Length`. | P(413,`BOOKING_STATUS_REQUEST_BODY_TOO_LARGE`,M). | Reader stops at max+1; no nonce/idempotency/inbox/state; remove stream fixture. | Step 13 counting-stream regression. |
| `STEP17-SEC-PROXY-026` | Live Kestrel behind a configured trusted loopback proxy; `GET /api/v1/internal/reservations/<reference>` over backend HTTP with trusted `X-Forwarded-Proto:https`; valid HMAC signs public path; body none. | J200 machine response; no HSTS on backend hop requirement, S and no CORS. | Read only; stop proxy/host and delete fixture. | Step 16 HTTPS integration regression. |
| `STEP17-SEC-PROXY-027` | Live direct plain HTTP, insecure-development override false; same internal GET/HMAC without forwarded proto. | P(403,`https_required`,M). | Nonce not applicable to GET; no domain access/mutation; stop host. | `STEP16-INTEGRATION-HTTP-146`. |
| `STEP17-SEC-PROXY-028` | Live direct HTTP from untrusted peer with forged `X-Forwarded-Proto:https`; same validly formed HMAC GET. | P(403,`https_required`,M). | No domain access; forged value absent from logs; stop host. | Trusted-proxy gap. |
| `STEP17-SEC-HEADERS-029` | TestServer; inject unhandled configuration dependency exception; `GET /api/v1/configuration`; DEV; body none. | P(500,`unexpected_error`,U); every S header occurs exactly once; no duplicate/combined security header. | No mutation; clear failpoint/delete device. | Existing Step 15 header families, new injected-500 proof. |
| `STEP17-SEC-LOG-030` | TestServer captured logs; STRIPE POST with invalid signature; exact body contains sentinel email, phone, address, client secret, SQL/connection/stack strings. | P(400,`stripe_signature_invalid`,M); response and logs contain none of the sentinels, raw signature, or raw body. | No event/domain row; clear captured logger. | Expected-red if any sentinel leaks; focused Step 14/16 privacy gate. |
| `STEP17-SEC-LOG-031` | TestServer; `GET /api/v1/configuration`; DEV plus 129-character `X-Correlation-Id` and 16,385-byte `Accept-Language`; body none. | P(400,`request_invalid`,U); safe generated correlation replaces input; response/log omit both raw overlong values; S once. | No domain mutation; clear logger/delete device. | Step 15 correlation/header-bound release proof. |

## 7. D — inherited manifests (exact registry: 1,295)

No manifest is edited or regenerated in this planning step. Its current entry
IDs remain the execution identities; Step 17 records aggregate and per-manifest
sanitized results without renaming them.

| Unchanged manifest | Entries |
|---|---:|
| `scripts/http-tests/plans/step-06-secure-integration.manifest.json` | 26 |
| `scripts/http-tests/plans/step-07-device-registration.manifest.json` | 10 |
| `scripts/http-tests/plans/step-08-customer-configuration.manifest.json` | 19 |
| `scripts/http-tests/plans/step-09-catalog-readmodel.manifest.json` | 21 |
| `scripts/http-tests/plans/step-10-checkout-drafts.manifest.json` | 28 |
| `scripts/http-tests/plans/step-11-pricing-reprice.manifest.json` | 27 |
| `scripts/http-tests/plans/step-12-booking-confirmation.manifest.json` | 64 |
| `scripts/http-tests/plans/step-13-booking-status.manifest.json` | 90 |
| `scripts/http-tests/plans/step-14-payment-rebuild.manifest.json` | 130 |
| `scripts/http-tests/plans/step-15-localization-swagger.manifest.json` | 90 |
| `scripts/http-tests/plans/step-16-clean-schema-separation.manifest.json` | 790 |
| **Total** | **1,295** |

The registry includes the retained manifest entry
`STEP14-INTENT-REAL-STRIPE-057`, but **Step 17 must not select or execute it**.
The Step 17 inherited disposition is therefore 1,294 executable local entries
that must pass plus one registered entry explicitly carried to Step 18. This
does not waive or rename 057, alter its manifest status, or let Step 17 claim
1,295 Stripe-safe executions. Step 18 remains blocked until 057 passes against
the Stripe test network. All other Step 14 entries use seeded state, a
deterministic fake gateway, or locally signed webhook bodies.

The inherited evidence proves complete retained/removed route inventories,
wrong-host isolation, authentication/authorization, model binding,
localization, Swagger, transport headers, body bounds, HMAC, drafts, pricing,
confirmation, status/reconciliation/outbox/capacity, payments/webhooks, clean
schemas, migrations, startup, outage/recovery, and teardown. New rows A–C add
only the complete chain, deterministic remaining gaps, and Step 17 host policy;
they do not silently replace inherited coverage.

## 8. Traceability and completion gate

| Requirement | Frozen evidence |
|---|---|
| Full mobile journey and recovery | A001–A039 (39 concrete rows) |
| Fixed time, SQL, concurrency, failpoints | B001–B048 (48 concrete rows) |
| No cross-origin opt-in | CORS decision plus C001–C006 |
| Behavior-safe Customer/Business rate limits | §3.2 plus C007–C015 |
| Request bounds | C016–C025 |
| Trusted proxy/HTTPS/security headers | C026–C029 |
| Secret/PII/log redaction | §2.5 plus C030–C031 and every P/J profile |
| Cross-database ownership and invariants | §2.1/§2.6, A018–A039, B SQL rows |
| Existing release coverage | D: 1,295 entries across exactly 11 manifests |
| No real Stripe in Step 17 | §2.7 and D disposition for scenario 057 |

Step 17 is complete only when:

1. all 118 new IDs exist unchanged in tests and, when implementation begins,
   the future live manifest(s); no ID is grouped into hidden variants;
2. all A/B/C requests have current-run sanitized evidence and pass at their
   declared levels; expected-red rows are made green test-first;
3. the 1,294 Stripe-safe inherited entries pass unchanged, all 1,295 remain
   registered, and 057 remains explicitly pending Step 18 rather than falsely
   reported as a Step 17 pass;
4. exact response status, stable code, body, headers, localization, side
   effects, and no-leakage assertions pass—changing expected values to fit a
   defect is forbidden;
5. Customer/Business databases and credentials remain independent and all
   final SQL, process, listener, principal, database, and ignored-artifact
   teardown assertions pass;
6. no failed, merely planned, or merely automated must-ship row remains. A
   deferral other than the already-owned Step 18 scenario 057 blocks Step 17.

## Code Test Plan

- Add focused unit/options tests for rate-limit configuration and partition
  selection, TestServer contract tests for CORS/rate/security behavior, and
  relational/failpoint tests named by B.
- Add the smallest live-local orchestration needed for A/C only when production
  implementation starts; do not create it in this planning change.
- Run targeted tests first, then both API test projects and clean Release
  builds. No implementation or test execution is part of this document-only
  change.

## HTTP Test Plan

- HTTP required: **Yes** — exact rationale and levels are frozen above.
- New scenario IDs: **118** (`A=39`, `B=48`, `C=31`).
- Inherited registry: **1,295 entries across 11 retained manifests**.
- Execution: TestServer/SQL/failpoint, contract, and dedicated live-local HTTPS;
  never production.

## HTTP Test Results

Completed on 2026-08-25 with fresh disposable run
`e0c263078d894d4e9ed31e64835937b8`.

- New Step 17 live-local scenarios: **58 passed, 0 failed**.
- New deterministic/contract mappings: **60 passed, 0 failed**.
- Inherited safe-local selection: **1,294 passed, 0 failed** across Steps
  6-16. The Step 9 orchestration performed one additional support execution,
  so the harness recorded 1,295 inherited executions for 1,294 selected
  scenarios.
- Step 16 clean-schema lifecycle: **790 passed, 0 failed**, including
  migration order/repeat/reset, production failure modes, wrong and missing
  schemas, SQL outage/recovery, independent restart/concurrency, missing-table
  recovery, final invariant verification, and cleanup.
- Automated .NET tests: **1,771 passed, 0 failed** (`1,227` Customer and
  `544` Business).
- HTTP harness self-tests: **30 passed, 0 failed**.
- Release build: **0 warnings, 0 errors**.
- Cleanup: no run state, runtime configuration, fixture overlay, disposable
  database, or owned listener remained.
- Real Stripe scenario `STEP14-INTENT-REAL-STRIPE-057` was not selected or
  executed and remains the mandatory Step 18 release gate.

Sanitized local evidence is retained in ignored artifact
`scripts\http-tests\artifacts\step17-quality-gate.results.local.json`.
