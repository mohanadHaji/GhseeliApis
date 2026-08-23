# Step 13 HTTP Test Plan — Booking Status Callbacks and Reconciliation

Status: implemented with focused automated coverage; live HTTPS execution remains
an explicit pre-deployment gate because this run had no configured local reverse-direction
HMAC secrets or two-API fixture. HTTP required: **Yes** — this step adds authenticated internal
routes, state transitions, callback idempotency, asynchronous delivery, and
cross-database reconciliation.

This plan is intentionally split into:

- **Live sequential** scenarios safe for dedicated local SQL Server databases
  over real HTTPS. These belong in
  `scripts/http-tests/plans/step-13-booking-status.manifest.json`.
- **Automated-only** TestServer/SQL/failpoint scenarios. They require controlled
  clocks, parallel requests, transport failures, background-worker control, or
  transaction failpoints and must not be simulated against a normal live API.

The plan assumes the in-progress neutral contract
`Ghseeli.IntegrationContracts/BookingStatusContracts.cs`: statuses `Pending`,
`Confirmed`, `InProgress`, `Completed`, `Cancelled`, and `NoShow`; messages carry
`eventId`, all three cross-system references, `status`, `sequence`, and
`occurredAtUtc`.

## Required deterministic fixtures and harness support

Use dedicated, newly migrated local Customer and Business databases. Seed through
public APIs where practical and a narrowly scoped fixture script where clock,
status, sequence, outbox scheduling, or exact pre-existing rows are required.
Every fixture uses one unique run token and known:

- Customer user/JWT/device, a second user/device, Business owner/employee/admin,
  two companies, and one work order per independent transition edge.
- Customer booking reference, order GUID, Business reservation public ID, and
  Business work-order public ID whose equality is asserted across databases.
- Initial status `Pending`, sequence `0`, fixed occurred times beginning
  `2030-01-15T09:00:00Z`, and deterministic event GUIDs.
- Separate Customer-bound service IDs/secrets for allowed callback/read,
  wrong-operation, active-secret, next-secret, unknown-client, and wrong-secret
  tests. Never reuse the Customer-to-Business secret in the reverse direction.
- Configurable worker clock, short deterministic retry schedule, disabled
  automatic dispatch during fixture setup, and explicit “dispatch once” /
  “reconcile once” test hooks available only in Testing.
- A callback signing mode in the harness. Existing `internalAuth` already signs
  method, normalized path/query, timestamp, nonce, exact `Idempotency-Key`, and
  raw body hash; Step 13 must verify it works unchanged against the Customer API.
  Add no secret to a manifest. A fresh nonce is required on every retry while
  event ID, idempotency key, body bytes, correlation ID, and request hash remain
  stable.

The current harness accepts one base URL per run. Run the same manifest in
explicit Customer and Business phases, or add a non-secret per-scenario base
alias; do not silently send Business scenarios to the Customer host. Add
eventual/poll support with a bounded timeout only if live outbox verification
cannot be made deterministic. Keep live scenarios sequential.

## Live sequential scenarios

Every response asserts `X-Correlation-Id`; all errors assert
`application/problem+json`, stable `code`, no stack/SQL/secret/PII leakage, and
`Cache-Control: no-store`. All JSON successes assert `application/json`,
contract version, exact references/status/sequence, and no customer snapshot or
pricing fields unless the route contract explicitly includes them.

### Contract, routing, transport, and permission

| ID | Request / setup | Expected assertions |
|---|---|---|
| `STEP13-CONTRACT-CUSTOMER-SWAGGER-001` | Customer Swagger | Callback `POST /api/v1/internal/bookings/status` and reconcile `GET /api/v1/internal/bookings/{reference}` exist with HMAC headers, DTO schemas, declared 200/400/401/403/404/409/413/503 responses; no JWT security requirement. |
| `STEP13-CONTRACT-BUSINESS-SWAGGER-002` | Business Swagger | `POST /api/v1/business/work-orders/{id}/transitions` and `GET /api/v1/internal/reservations/{reference}` exist with correct JWT/HMAC security and schemas. |
| `STEP13-ROUTE-WRONG-VERB-003` | PUT callback route | 405; no state/message row. |
| `STEP13-ROUTE-UNKNOWN-004` | Validly signed unknown internal path | 404; no state/message row. |
| `STEP13-AUTH-HTTPS-REQUIRED-005` | Valid callback over HTTP with insecure-development override off | 403 `https_required`; nonce/event/idempotency are not consumed. |
| `STEP13-AUTH-CUSTOMER-JWT-006` | Customer JWT instead of HMAC on callback | 401; JWT cannot authorize internal route. |
| `STEP13-AUTH-BUSINESS-JWT-007` | Business JWT instead of HMAC on callback | 401; no mutation. |
| `STEP13-AUTH-ANONYMOUS-008` | No auth headers | 401 `internal_auth_missing_header`; deterministic `missingHeaders`. |
| `STEP13-AUTH-OPERATION-FORBIDDEN-009` | Valid HMAC client lacking `booking_status_callback` | 403 `internal_service_forbidden`; nonce handling follows the documented auth policy and no business row changes. |
| `STEP13-AUTH-RECONCILE-FORBIDDEN-010` | Callback-only client signs reconcile GET | 403 `internal_service_forbidden`. |
| `STEP13-BUSINESS-TRANSITION-ANONYMOUS-011` | Transition without JWT | 401 `business_authentication_required`; authentication failures never use media-type error codes. |
| `STEP13-BUSINESS-TRANSITION-WRONG-ROLE-012` | Customer JWT on Business transition | 401/403 per authentication boundary; no outbox event. |
| `STEP13-BUSINESS-TRANSITION-WRONG-OWNER-013` | Owner/employee from company B transitions company A work order | 404 or 403 per finalized non-enumeration contract; no mutation/event. |

### HMAC headers, canonical content, replay, and correlation

| ID | Mutation | Expected assertions |
|---|---|---|
| `STEP13-HMAC-MISSING-EACH-014` | One run for each absent service/timestamp/nonce/signature header | 401 `internal_auth_missing_header`; exact missing header listed. |
| `STEP13-HMAC-EMPTY-EACH-015` | One run for each empty/whitespace required header | Same as missing; no nonce/message/idempotency row. |
| `STEP13-HMAC-SERVICE-MALFORMED-016` | Space, CRLF attempt, over 64 chars, wrong case | 401 `internal_auth_invalid_service`; safe response. |
| `STEP13-HMAC-SERVICE-UNKNOWN-017` | Well-formed unknown service | 401 `internal_auth_invalid_service`. |
| `STEP13-HMAC-TIMESTAMP-MALFORMED-018` | Non-ISO, offset/non-UTC if disallowed, multiple values | 401 `internal_auth_invalid_timestamp`. |
| `STEP13-HMAC-TIMESTAMP-STALE-019` | Older than skew | 401 `internal_auth_timestamp_out_of_range`. |
| `STEP13-HMAC-TIMESTAMP-FUTURE-020` | Newer than skew | Same out-of-range rejection. |
| `STEP13-HMAC-NONCE-MALFORMED-021` | 31 chars, 129 chars, space/CRLF, illegal alphabet, multiple values | 401 `internal_auth_invalid_nonce`. |
| `STEP13-HMAC-SIGNATURE-MALFORMED-022` | Non-hex, wrong length, uppercase if canonical requires lowercase | 401 `internal_auth_invalid_signature`. |
| `STEP13-HMAC-WRONG-SECRET-023` | Correct canonical request signed with unrelated secret | 401 invalid signature. |
| `STEP13-HMAC-WRONG-DIRECTION-024` | Sign with Customer-to-Business secret | 401 invalid signature, proving directional separation. |
| `STEP13-HMAC-NEXT-SECRET-025` | Sign with configured next reverse secret | 200, exactly one applied event. |
| `STEP13-HMAC-TAMPER-METHOD-026` | Signature computed for different verb | 401 invalid signature. |
| `STEP13-HMAC-TAMPER-PATH-027` | Signature computed for altered path/reference | 401 invalid signature. |
| `STEP13-HMAC-TAMPER-BODY-028` | Sign original bytes, send one changed status/reference byte | 401 invalid signature. |
| `STEP13-HMAC-TAMPER-IDEMPOTENCY-029` | Sign one idempotency key, send another | 401 invalid signature. |
| `STEP13-HMAC-NONCE-REPLAY-030` | Re-send an accepted nonce with otherwise valid new request | 401 `internal_auth_replay_nonce`; second event not processed. |
| `STEP13-HMAC-CORRELATION-PRESERVE-031` | Valid bounded correlation ID | Exact value echoed and used on downstream delivery evidence. |
| `STEP13-HMAC-CORRELATION-REPLACE-032` | Missing, empty, illegal, CRLF, and overlong IDs | Safe generated 32-hex ID; invalid input never appears in response/log. |
| `STEP13-HMAC-BODY-HASH-WHITESPACE-033` | Semantically equal JSON with different raw whitespace, newly signed | Authentication succeeds; event-level identity, not raw formatting, controls replay. |

### Idempotency and body validation

| ID | Request | Expected assertions |
|---|---|---|
| `STEP13-IDEM-MISSING-034` | Valid HMAC callback without `Idempotency-Key` (signature includes empty line) | 400 `idempotency_key_required`. |
| `STEP13-IDEM-EMPTY-035` | Empty/whitespace key | 400 required/invalid per finalized rule. |
| `STEP13-IDEM-MALFORMED-036` | Space, CRLF, over 128 chars, multiple values | 400 `idempotency_key_invalid`. |
| `STEP13-BODY-CONTENT-TYPE-037` | Valid JSON as text/plain/form/missing type; exact/different malformed types; different overlong types; then correct to normalized `application/json` under the same and a new key | Exact invalid input replays; different malformed/overlong values conflict. Initial and identical replay deterministically return the same 415 `BOOKING_STATUS_UNSUPPORTED_MEDIA_TYPE`; corrected same-key request returns 409 `idempotency_conflict`; corrected new-key request succeeds. Valid values normalize media type/charset; invalid values use only a bounded class, UTF-8 length, and SHA-256 digest. Raw unbounded values are not persisted or logged. |
| `STEP13-BODY-EMPTY-038` | Empty body | 400 stable contract-invalid code. |
| `STEP13-BODY-NULL-039` | Literal `null` | 400 stable contract-invalid code. |
| `STEP13-BODY-MALFORMED-040` | Truncated fixture | 400 problem, not 500. |
| `STEP13-BODY-OVERSIZE-LENGTH-041` | Declared body over 65,536 bytes | 413 `BOOKING_STATUS_REQUEST_BODY_TOO_LARGE`; no event/inbox state. |
| `STEP13-BODY-OVERSIZE-CHUNKED-042` | `repeatBody` over boundary without content length | Same booking-status 413 and no partial state. |
| `STEP13-BODY-BOUNDARY-043` | Exactly configured byte limit and valid JSON | Passes size middleware and reaches contract processing. |
| `STEP13-BODY-UNKNOWN-FIELDS-044` | Valid event plus identity/status/pricing/snapshot junk | Either documented strict 400 or ignored fields; authoritative stored snapshot, ownership, references, money, and items remain byte-for-byte unchanged. |
| `STEP13-BODY-CONTRACT-VERSION-045` | Missing/empty/unknown/case-changed version | 400 `BOOKING_STATUS_INVALID`; no event. |
| `STEP13-BODY-REQUIRED-FIELDS-046` | Individually omit/empty each GUID, status, sequence, occurred time | 400 invalid with field-safe errors; no partial row. |
| `STEP13-BODY-STATUS-MALFORMED-047` | Unknown, integer enum, wrong case, whitespace status | 400 invalid. |
| `STEP13-BODY-SEQUENCE-INVALID-048` | Negative and overflow/non-integer sequence | 400 invalid. Sequence-zero behavior must match the finalized initial-sequence rule. |
| `STEP13-BODY-OCCURRED-INVALID-049` | Default, non-UTC/invalid timestamp | 400 invalid; timestamp freshness policy, if any, is explicit. |

### Callback behavior and cross-system invariants

| ID | Request / sequence | Expected assertions |
|---|---|---|
| `STEP13-CALLBACK-APPLY-050` | Valid `Pending→Confirmed`, event E1/sequence 1 | 200; `applied=true`, `stale=false`; booking status/sequence updated once; one processed-message row. |
| `STEP13-CALLBACK-SAME-EVENT-REPLAY-051` | E1 with identical semantic body, fresh nonce, same idempotency key | Original 200 body/status/content type; no second update/message. |
| `STEP13-CALLBACK-SAME-EVENT-NEW-IDEM-052` | E1 identical body under a new transport key | Event dedupe returns same logical success; one processed event, two transport records only if transport storage is defined that way. |
| `STEP13-CALLBACK-IDEM-SAME-BODY-053` | Same key/body but fresh nonce | Original response replayed; one processed event. |
| `STEP13-CALLBACK-IDEM-CONFLICT-054` | Same key, changed raw body/status/event | 409 `idempotency_conflict`; original event/result unchanged. |
| `STEP13-CALLBACK-EVENT-CONFLICT-055` | Same event ID, new key, changed status/sequence/reference/time | 409 `BOOKING_STATUS_EVENT_CONFLICT`; original request hash retained. |
| `STEP13-CALLBACK-DUPLICATE-NEW-EVENT-056` | New event ID/sequence but repeats current status | 409 transition-invalid unless explicitly defined as stale; never another state mutation. |
| `STEP13-CALLBACK-DELAYED-057` | Valid next sequence with old-but-valid occurred time | Sequence governs ordering; applies once unless timestamp ordering is explicitly contractual. |
| `STEP13-CALLBACK-OUT-OF-ORDER-058` | Deliver sequence 2 then sequence 1 | Final sequence/status remains 2; older callback returns 200 `stale=true,applied=false`, creates one non-applied processed-message record, and never rolls back. |
| `STEP13-CALLBACK-SEQUENCE-GAP-059` | Current 0 receives sequence 2 | Applies only when the status edge itself is directly allowed; response/status expose sequence 2. Reconciliation uses the same rule. |
| `STEP13-CALLBACK-UNKNOWN-BOOKING-060` | All references well formed but unknown | 404 `BOOKING_NOT_FOUND`; no processed success/event row. |
| `STEP13-CALLBACK-RESERVATION-MISMATCH-061` | Correct booking/work order, wrong reservation | 409 `BOOKING_REFERENCE_MISMATCH`; no mutation. |
| `STEP13-CALLBACK-WORKORDER-MISMATCH-062` | Correct booking/reservation, wrong work order | Same mismatch behavior. |
| `STEP13-CALLBACK-CROSS-BOOKING-MIX-063` | Valid references drawn from two fixtures | Same mismatch behavior; neither booking mutates. |
| `STEP13-CALLBACK-IMMUTABLE-064` | Apply complete valid chain | Only status/sequence/status timestamp change; owner user/device, order/cross references, localized provider/service data, vehicle/location, all pricing/tax/fee/duration/item/selection snapshots and Payment count are unchanged. |
| `STEP13-CALLBACK-TERMINAL-REPLAY-065` | Replay the event that entered a terminal state | Original success; no extra mutation. |
| `STEP13-CALLBACK-TERMINAL-NEW-066` | New event attempts to leave Completed/Cancelled/NoShow | 409 transition-invalid; terminal state/sequence unchanged. |

Transport idempotency is durable and scoped by service ID, operation, and
`Idempotency-Key`. Its request fingerprint uses normalized method/path/query and
canonical JSON (property order and insignificant JSON whitespace do not create
a new request). Completed status, exact response bytes, and content type are
replayed. Each in-progress generation owns a durable random token and renews its
bounded recovery lease at a safe configured fraction throughout endpoint
execution, including long reconciliation calls. The computed interval is
validated at startup (finite, at least 100 ms, and strictly before the lease
safety deadline); it is never silently clamped. Renewal retries transient
database failures with bounded backoff while safely before the last confirmed
expiry. Losing ownership or reaching the safety deadline cancels the token
passed to downstream async work. Renewal and completion use independent,
server-bounded tokens and fresh scopes, not the request-abort token.

Once endpoint execution starts, client disconnects and ambiguous endpoint
cancellation cannot delete the claim or cancel durable completion. Such failures
preserve the owned in-progress generation until safe domain-idempotent reclaim
after lease expiry. Reclaim atomically verifies expiry and replaces ownership;
renewal and completion verify both active lease and owner, so stale owners cannot
overwrite/delete a reclaimed generation. Process crashes stop the heartbeat and
become reclaimable after lease expiry. Tokens are never returned or logged.
Relational/TestServer coverage includes post-domain-commit client cancellation
and replay, transient renewal recovery, repeated renewal loss, changed
ownership, pacing, multi-period execution, cleanup, stale completion, crash
recovery, and one-winner concurrent reclaim.

The chunked oversize scenario additionally requires the receiver to stop after
reading byte 65,537. The automated counting-stream test proves the remaining
input is neither read nor buffered, while exactly 65,536 bytes remain accepted.
Nonce rows reject replay only until `ExpiresAtUtc`; expired unique-key rows can
be atomically replaced, and opportunistic cleanup removes only expired rows.

### Business transition, delivery, and reconciliation smoke

| ID | Action | Expected assertions |
|---|---|---|
| `STEP13-BUSINESS-TRANSITION-067` | Owning Business principal moves one Pending work order to Confirmed | Business reservation/work order change atomically; sequence increments exactly once; one event/outbox row with stable event ID/message hash. |
| `STEP13-BUSINESS-TRANSITION-REPEAT-068` | Repeat same requested transition | 409 transition-invalid (or documented command idempotency); never a second logical event. |
| `STEP13-OUTBOX-LIVE-DELIVER-069` | Explicitly dispatch event from 067 to live Customer HTTPS | Customer reaches Confirmed/sequence 1; outbox becomes delivered only after 2xx; event ID/body/idempotency/correlation stable and nonce fresh. |
| `STEP13-OUTBOX-LIVE-RETRY-070` | Re-dispatch already delivered event through supported test hook | Receiver replay is harmless and Business does not create a second outbox record. |
| `STEP13-RECONCILE-CUSTOMER-READ-071` | HMAC GET Customer state by booking reference | Exact Customer references/status/sequence, no PII/snapshots/pricing, no state mutation, no `Idempotency-Key` required for GET. |
| `STEP13-RECONCILE-BUSINESS-READ-072` | HMAC GET Business reservation by booking reference | Exact authoritative references/status/sequence/time; no PII/snapshots/pricing. |
| `STEP13-RECONCILE-UNKNOWN-073` | Validly signed unknown reference on both APIs | 404 stable not-found problem; no leakage. |
| `STEP13-RECONCILE-REPAIR-MISSED-074` | Fixture suppresses callback, runs supported reconciliation operation | Customer converges exactly once only when its current status has a directly allowed edge to the authoritative status; invalid skipped edges conflict without altering snapshots. |
| `STEP13-RECONCILE-ALREADY-CURRENT-075` | Reconcile equal status/sequence | 200 no-op result; counts and versions unchanged. |
| `STEP13-LOCALIZATION-AR-076` | Arabic error via query/header/default | Arabic safe detail; same stable code/status. |
| `STEP13-LOCALIZATION-HE-077` | Hebrew error and missing-Hebrew fallback | Hebrew where available, Arabic fallback; same code/status. |
| `STEP13-LOCALIZATION-PRECEDENCE-078` | Query language conflicts with `Accept-Language` | Explicit query wins; malformed explicit value returns stable 400 rather than 500. |

## Automated-only TestServer, relational SQL, concurrency, and failpoints

Automated references for the final reliability cases are
`BookingStatusCallbackSecurityIntegrationTests.Callback_ContentTypeParticipatesInTransportIdempotencyIdentity`,
`WorkOrdersControllerTests.Transition_WhenNameIdentifierIsMissingOrMalformed_ReturnsTypedUnauthorizedWithoutMutation`,
`BookingStatusOutboxAdminControllerTests.Requeue_WhenNameIdentifierIsMissingOrMalformed_ReturnsTypedUnauthorizedWithoutMutation`,
and
`BookingStatusDeadLetterServiceTests.RequeueAsync_SameRequest_AuditsNoOpWithoutChangingPersistentAudit`.

### Complete transition matrix

Each matrix case sends a **new event ID and the exact next sequence** to the
Customer callback receiver. Allowed edges return 200 and increment
status/sequence once. All other edges, including self transitions under a new
event ID, return 409 `BOOKING_TRANSITION_INVALID` and preserve status/sequence.
Test every non-self edge against the Business command rule too. Business command
self-target behavior is the separate 409 `BOOKING_TRANSITION_INVALID` contract
in scenario 068 and never creates a second logical event.

Concurrent Business row-version transition conflicts return 409
`BOOKING_TRANSITION_CONFLICT`, never an untyped 500. Capacity uses the same
central status contract as transition validation: `Pending`, legacy `Reserved`,
`Confirmed`, and `InProgress` consume capacity; only `Completed`, `Cancelled`,
and `NoShow` release it.

| From | To | Scenario ID | Expected |
|---|---|---|---|
| Pending | Pending | `STEP13-MATRIX-PENDING-PENDING-101` | forbidden |
| Pending | Confirmed | `STEP13-MATRIX-PENDING-CONFIRMED-102` | allowed |
| Pending | InProgress | `STEP13-MATRIX-PENDING-INPROGRESS-103` | forbidden |
| Pending | Completed | `STEP13-MATRIX-PENDING-COMPLETED-104` | forbidden |
| Pending | Cancelled | `STEP13-MATRIX-PENDING-CANCELLED-105` | allowed |
| Pending | NoShow | `STEP13-MATRIX-PENDING-NOSHOW-106` | forbidden |
| Confirmed | Pending | `STEP13-MATRIX-CONFIRMED-PENDING-107` | forbidden |
| Confirmed | Confirmed | `STEP13-MATRIX-CONFIRMED-CONFIRMED-108` | forbidden |
| Confirmed | InProgress | `STEP13-MATRIX-CONFIRMED-INPROGRESS-109` | allowed |
| Confirmed | Completed | `STEP13-MATRIX-CONFIRMED-COMPLETED-110` | forbidden |
| Confirmed | Cancelled | `STEP13-MATRIX-CONFIRMED-CANCELLED-111` | allowed |
| Confirmed | NoShow | `STEP13-MATRIX-CONFIRMED-NOSHOW-112` | allowed |
| InProgress | Pending | `STEP13-MATRIX-INPROGRESS-PENDING-113` | forbidden |
| InProgress | Confirmed | `STEP13-MATRIX-INPROGRESS-CONFIRMED-114` | forbidden |
| InProgress | InProgress | `STEP13-MATRIX-INPROGRESS-INPROGRESS-115` | forbidden |
| InProgress | Completed | `STEP13-MATRIX-INPROGRESS-COMPLETED-116` | allowed |
| InProgress | Cancelled | `STEP13-MATRIX-INPROGRESS-CANCELLED-117` | allowed |
| InProgress | NoShow | `STEP13-MATRIX-INPROGRESS-NOSHOW-118` | allowed |
| Completed | Pending | `STEP13-MATRIX-COMPLETED-PENDING-119` | forbidden |
| Completed | Confirmed | `STEP13-MATRIX-COMPLETED-CONFIRMED-120` | forbidden |
| Completed | InProgress | `STEP13-MATRIX-COMPLETED-INPROGRESS-121` | forbidden |
| Completed | Completed | `STEP13-MATRIX-COMPLETED-COMPLETED-122` | forbidden |
| Completed | Cancelled | `STEP13-MATRIX-COMPLETED-CANCELLED-123` | forbidden |
| Completed | NoShow | `STEP13-MATRIX-COMPLETED-NOSHOW-124` | forbidden |
| Cancelled | Pending | `STEP13-MATRIX-CANCELLED-PENDING-125` | forbidden |
| Cancelled | Confirmed | `STEP13-MATRIX-CANCELLED-CONFIRMED-126` | forbidden |
| Cancelled | InProgress | `STEP13-MATRIX-CANCELLED-INPROGRESS-127` | forbidden |
| Cancelled | Completed | `STEP13-MATRIX-CANCELLED-COMPLETED-128` | forbidden |
| Cancelled | Cancelled | `STEP13-MATRIX-CANCELLED-CANCELLED-129` | forbidden |
| Cancelled | NoShow | `STEP13-MATRIX-CANCELLED-NOSHOW-130` | forbidden |
| NoShow | Pending | `STEP13-MATRIX-NOSHOW-PENDING-131` | forbidden |
| NoShow | Confirmed | `STEP13-MATRIX-NOSHOW-CONFIRMED-132` | forbidden |
| NoShow | InProgress | `STEP13-MATRIX-NOSHOW-INPROGRESS-133` | forbidden |
| NoShow | Completed | `STEP13-MATRIX-NOSHOW-COMPLETED-134` | forbidden |
| NoShow | Cancelled | `STEP13-MATRIX-NOSHOW-CANCELLED-135` | forbidden |
| NoShow | NoShow | `STEP13-MATRIX-NOSHOW-NOSHOW-136` | forbidden |

### Atomicity, concurrency, retries, and reconciliation failures

| ID | Controlled case | Required assertion |
|---|---|---|
| `STEP13-ATOMIC-CALLBACK-137` | Fail before commit, plus lose the commit acknowledgement after the real SQL commit | Pre-commit failure rolls back completely; post-commit ambiguity is verified by stable event ID + canonical request hash and returns the committed logical response without retrying the mutation or creating a duplicate. |
| `STEP13-ATOMIC-MESSAGE-138` | Fail after processed-message staging but before booking save | No orphan processed row; retry applies. |
| `STEP13-ATOMIC-BUSINESS-139` | Fail between work-order mutation and outbox insert | Both roll back; no undeliverable state change. |
| `STEP13-CONCURRENT-SAME-EVENT-140` | Two identical deliveries simultaneously | Both return same logical success; one update, one processed event; uniqueness handles race without 500. |
| `STEP13-CONCURRENT-SAME-SEQUENCE-141` | Two different event IDs/statuses for same next sequence | Exactly one wins; loser 409/stale; one sequence increment. |
| `STEP13-CONCURRENT-BUSINESS-142` | Two allowed commands against same rowversion | One transition/event; loser 409; no duplicate outbox. |
| `STEP13-RESPONSE-LOSS-143` | Customer commits then transport drops response | Business retries same stable event/key/hash/correlation with fresh nonce; Customer replays success; outbox delivered once. |
| `STEP13-IDEM-INPROGRESS-144` | First identical request held open beyond wait | Second returns 503 `idempotency_unavailable`; later retry replays final result. |
| `STEP13-IDEM-STORE-FAIL-145` | Cannot persist replay response after endpoint execution began | 503 when the connection remains available; claim is preserved as side-effect-ambiguous and cannot be completed by a stale owner. Domain-idempotent retry is allowed only after safe lease expiry. |
| `STEP13-IDEM-CLIENT-ABORT-173` | Customer commits the domain event, then the caller cancels/disconnects before the response is finalized | Server-controlled completion persists despite request cancellation; retry with the same key/hash and a fresh nonce replays the response; exactly one endpoint/domain execution. Relational TestServer coverage. |
| `STEP13-OUTBOX-NETWORK-146` | DNS/connect/timeout failure | Event remains pending; bounded retry scheduled; no Customer change; sanitized log. |
| `STEP13-OUTBOX-408-429-5XX-147` | Each retryable response, including Retry-After policy | Bounded retries only; stable message/key/hash/correlation, fresh nonce; attempt count exact. |
| `STEP13-OUTBOX-4XX-148` | 400/401/403/404/409 receiver responses | Final retry/dead-letter classification follows policy; never mark delivered; no hot loop. |
| `STEP13-OUTBOX-DEADLETTER-149` | Exhaust retry budget | Durable dead-letter/failed state, attempt count/last safe code/time; business status remains committed and reconcilable. |
| `STEP13-OUTBOX-DEADLETTER-HOL-164` | Sequence 1 is dead-lettered while sequence 2 is due | Sequence 2 remains blocked; dead-letter is a durable ordering barrier. Automated dispatcher test; live mutation is intentionally not exposed. |
| `STEP13-OUTBOX-ADMIN-AUTH-165` | Anonymous, owner, and employee call the dead-letter requeue route | 401/403 before lookup; only global Business Admin is authorized. |
| `STEP13-OUTBOX-ADMIN-REQUEUE-166` | Admin requeues a dead-letter event with a bounded request `Idempotency-Key` | 200 `Requeued`, generation increments atomically, `Cache-Control: no-store`; durable history has safe admin/event/request IDs; immutable event fields are unchanged. Generation 0 key is `booking-status-{eventId:N}` and generation 1 key is distinct. |
| `STEP13-OUTBOX-ADMIN-IDEMPOTENT-167` | Repeat the same requeue request after lease/delivery, then dead-letter again and submit a new request | Same request returns stable 200 `AlreadyRequeued` with its original generation and never double-increments. A new request against the later dead letter increments to the next generation. Concurrent copies of one request produce exactly one increment/history row. A new request against pending/leased/delivered remains 409. |
| `STEP13-OUTBOX-ADMIN-NOTFOUND-168` | Admin supplies an unknown event ID | Localized, safe 404 Problem Details with no enumeration data. |
| `STEP13-OUTBOX-ADMIN-CONFLICT-169` | Admin targets an actively leased event | Localized 409; lease and event remain unchanged. |
| `STEP13-OUTBOX-RECOVERY-ORDER-170` | Cached permanent 4xx dead-letters generation 0; operator correction requeues generation 1; then dispatch sequence 1 and 2 | Generation 1 bypasses the cached transport result while preserving domain event/body/status/sequence. Delivery occurs exactly in sequence order. A later dead-letter remains recoverable as generation 2; no skip/discard operation exists. |
| `STEP13-OUTBOX-WORKER-RESILIENCE-171` | First background cycle/acquire/persist operation throws transiently | Host remains healthy, safe bounded backoff occurs, and a later event dispatches. |
| `STEP13-OUTBOX-WORKER-CANCEL-172` | Host cancellation arrives during worker delay/cycle | Worker exits cleanly without a host-fatal exception. |
| `STEP13-OUTBOX-RESTART-150` | Stop between commit and dispatch, restart host | Pending event is recovered and delivered once. |
| `STEP13-OUTBOX-MULTIWORKER-151` | Two dispatch workers claim same due row | One effective delivery/lease; no duplicate terminal bookkeeping. |
| `STEP13-OUTBOX-ORDERING-152` | Event 1 retry delayed while event 2 is due | Per-booking ordering prevents corruption, or receiver stale/sequence rules guarantee final sequence 2; exact policy asserted. |
| `STEP13-OUTBOX-BODY-STABILITY-153` | Retry after model/entity changes | Serialized message or canonical semantic hash remains identical to original committed event. |
| `STEP13-RECONCILE-MISSED-154` | Customer seq 0, Business seq 2 | One reconcile converges only for a directly allowed status edge and records source/evidence; an invalid skipped edge is rejected. |
| `STEP13-RECONCILE-CUSTOMER-AHEAD-155` | Customer sequence exceeds Business | Fail closed/conflict and alert; never roll Customer backward. |
| `STEP13-RECONCILE-REF-MISMATCH-156` | Business response has one wrong cross-reference | Reject mismatch; no mutation. |
| `STEP13-RECONCILE-MALFORMED-157` | Business 200 has empty body, invalid JSON, missing field, unknown status/version | Contract exception mapped to safe 502/503 result; no mutation/retry corruption. |
| `STEP13-RECONCILE-NOTFOUND-158` | Business authoritative GET returns 404 | Customer is not deleted/cancelled; safe not-found outcome and audit. |
| `STEP13-RECONCILE-AUTHFAIL-159` | Business returns 401/403 | No blind retry; safe auth/config failure; no mutation. |
| `STEP13-RECONCILE-TRANSIENT-160` | Network, timeout, 408, 429, 500/503 | Exact bounded retry count; eventual convergence on success; otherwise safe 503 and next schedule. |
| `STEP13-RECONCILE-CONCURRENT-CALLBACK-161` | Callback arrives while reconcile is applying | Rowversion/sequence arbitration yields one monotonic final state and no 500. |
| `STEP13-CACHE-HEADERS-162` | Success and every problem branch | `no-store`; no ETag/Last-Modified caching of mutable status. |
| `STEP13-LOG-PRIVACY-163` | Capture success, auth rejects, conflicts, retries, dead-letter | Logs contain operation, safe IDs/state/attempt/correlation only; never HMAC secret/signature, raw token/body, names, email, phone, address, vehicle/license, localized text, or stack/SQL in HTTP. |
| `STEP13-RECONCILE-THEN-CALLBACK-164` | Reconcile records a synthetic event, then the unseen real callback arrives at the same sequence/status/references | 200 idempotent no-op; the real event ID/hash is recorded alongside the synthetic event. Conflicting status or references remains 409. |

## Exact relational database verification

Add a read-only `Verify-Step13DatabaseInvariants.ps1` for dedicated local DBs.
It must take expected counts rather than assume an empty shared database and
must fail on every mismatch. Verify:

1. Customer booking count is unchanged; item/selection/payment/confirmation
   attempt counts and all immutable snapshot columns/checksums equal pre-run.
2. Exactly the expected number of callback transport-idempotency rows exists,
   keyed by `{serviceId, booking_status_callback, idempotencyKey}`, with the
   expected request hash/status/content type; no secret/signature/body containing
   PII is stored.
3. Exactly one processed-message row per event ID; event ID uniqueness and
   stored semantic request hash; conflict/rejected requests do not create a
   completed processed event.
4. Customer status and sequence equal the final accepted Business status and
   sequence for delivered/reconciled fixtures and remain unchanged for every
   rejected fixture.
5. Business work-order and reservation status/sequence agree; each committed
   transition has exactly one outbox event with unique event ID and per-booking
   sequence; delivered, retry, and dead-letter counts/attempts match the plan.
6. Cross-system booking/reservation/work-order/order references still match.
   No query joins databases in production code; cross-database joins are allowed
   only in this local verifier.
7. There are no orphan processed messages, outbox messages, reservations, work
   orders, items, or selections and no duplicate sequence for one booking.

## Finalized contract decisions

- The v1 status message contains only immutable references, status, sequence,
  event ID, and occurrence time. It never carries identity, pricing, snapshots,
  reason text, or localized customer content.
- Existing `Reserved` data migrates to `Pending`; new reservations start at
  `Pending`, sequence `0`.
- The 36-edge matrix allows only `Pending→Confirmed/Cancelled`,
  `Confirmed→InProgress/Cancelled/NoShow`, and
  `InProgress→Completed/Cancelled/NoShow`. Self and terminal transitions fail.
- A lower sequence is a recorded 200 stale no-op. A different event at the
  current sequence conflicts. A forward gap applies only for a directly allowed
  edge, including reconciliation.
- The raw signed body SHA-256 is the event-conflict hash, so byte-level JSON
  changes conflict when an event ID is reused.
- `eventId` is the domain dedupe identity and is independent from the transport
  `Idempotency-Key`. Event, body, key, correlation, and hash remain stable across
  retries; nonce and timestamp are fresh per attempt.
- Directional HMAC clients use separate secrets and operation allowlists.
  Canonical signing includes the exact idempotency-key line. Authentication
  failures do not consume a nonce; accepted nonces are persisted.
- Sequence is authoritative for ordering. `occurredAtUtc` must be UTC but does
  not override sequence.
- Reconciliation looks up the Customer booking reference, returns only minimal
  references/status/sequence/time, and produces repaired, no-op, or conflict
  outcomes under the callback rules.
- Internal responses use `Cache-Control: no-store`; errors use stable
  `application/problem+json`. Signed JSON bodies are capped at 64 KiB.

## Safe execution and completion gate

- Never run against production. Use localhost HTTPS, test credentials, and
  dedicated disposable databases only.
- Start both APIs with separate databases and reverse-direction secrets in user
  secrets/environment. Do not print environment or signatures.
- Freeze automatic workers during setup; enable/trigger them only for the
  intended scenario. Reset by run token, never broad-delete shared data.
- Run targeted unit/TestServer/relational/failpoint tests first, then harness
  self-tests, then the sequential live manifest, then the SQL verifier and
  sanitized log scan.
- Completion requires every applicable scenario above to be implemented and
  passed, exact test/live totals recorded, zero unexplained failures/deferred
  must-ship cases, and sanitized evidence with correlation IDs.
