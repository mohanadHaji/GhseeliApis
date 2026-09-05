# Ghseeli Customer and Business API Boundaries

Status: Final Step 16 ownership boundary and HTTP contract

## 1. Applications

The solution will contain two independently deployable ASP.NET Core APIs.

### Customer API

The existing `GhseeliApis` project evolves into the customer-facing API.

It owns:

- customer registration, login, profile, and roles;
- devices and device tokens;
- customer configuration;
- vehicles and addresses;
- customer-facing catalog read models;
- checkout drafts and repricing orchestration;
- customer bookings and immutable booking snapshots;
- payments, Lahza hosted checkout, verification, refunds, and Lahza webhooks.

### Business API

`Ghseeli.BusinessApi` will be added as an independent application.

It owns:

- business-owner, employee, and administrator identities;
- companies and branches;
- service areas;
- categories;
- service offerings;
- add-on groups and choices;
- authoritative prices and durations;
- schedules, closures, capacity, and availability;
- appointment reservations and business work orders;
- business-initiated booking status transitions.

### Shared code limit

Only a small `Ghseeli.IntegrationContracts` project may be shared. It may contain:

- versioned HTTP request and response DTOs;
- integration enums;
- stable error-code constants.

It must not contain:

- EF Core entities or DbContexts;
- repositories or handlers;
- Identity models;
- authentication implementation;
- business rules;
- migrations;
- application configuration or secrets.

Neither API may reference the other API's implementation project or connect to the other API's database.

## 2. Current surface disposition

| Current surface | Destination |
|---|---|
| `AuthController` customer registration/login/profile | Customer API |
| `UsersController` customer self-service | Customer API |
| `VehiclesController` | Customer API |
| `AddressesController` | Customer API |
| Legacy `PaymentsController` | Removed; modern `/api/v1/payments/*` and Lahza webhook routes remain in Customer API |
| Legacy `BookingsController` | Removed; modern Customer booking confirmation/status integration remains |
| Legacy `CompaniesController` | Removed; Customer catalog reads and Business company management replace it |
| Legacy `ServicesController` and `ServiceOptionsController` | Removed; Customer catalog reads and Business catalog management replace them |
| Service/company catalog reads | Customer API read model populated from Business API |
| `CompanyAvailability` writes and rules | Business API |
| Customer-facing availability lookup | Customer API backed by Business API validation |
| Wallet, wallet transactions, and notifications | No runtime/schema owner until their deferred features are implemented |
| Health and Swagger | Separate implementation in each API |

The removed legacy routes are not compatibility aliases. They are absent from
runtime routing and Swagger and return a route-level `404` before
authentication on the Customer host.

## 3. Data ownership

### Customer database

- CustomerUser
- UserAddress
- Vehicle
- DeviceRegistration
- CustomerConfiguration
- CatalogProviderReadModel
- CatalogCategoryReadModel
- CatalogOfferingReadModel
- CatalogAddonGroupReadModel
- CatalogAddonChoiceReadModel
- CheckoutDraft
- CheckoutDraftItem
- CheckoutDraftSelection
- CustomerBooking
- CustomerBookingItem
- CustomerBookingSelection
- BookingConfirmationAttempt
- CustomerPayment
- CustomerPaymentIdempotencyRecord
- PaymentWebhookEvent
- CustomerInternalServiceNonce
- CustomerInternalIdempotencyRecord
- ProcessedBookingStatusMessage

### Business database

- BusinessUser
- BusinessUserAssignment
- Company
- Branch
- ServiceArea
- ServiceCategory
- ServiceOffering
- AddonGroup
- AddonChoice
- BusinessSchedule
- BusinessClosure
- AppointmentReservation
- WorkOrder
- WorkOrderItem
- WorkOrderSelection

Database IDs are private to their owning API. Integration contracts use explicit public IDs and cross-system references.

## 4. Identity and authorization

- Customer JWTs are issued and accepted only by Customer API.
- Business JWTs are issued and accepted only by Business API.
- Issuers, audiences, signing keys, roles, and Identity tables are separate.
- Business roles begin with `Owner`, `Employee`, and `Admin`.
- Business authorization always checks the authenticated user's company/branch assignment.
- Device tokens identify an application installation; they do not authenticate a customer user.
- Customer endpoints may require a device token, a customer JWT, or both.
- Customer device tokens are 256-bit opaque values returned only at issuance or rotation; only SHA-256 hashes are stored.
- `X-Device-Token` is required by default for matched `/api/v1/*` Customer endpoints unless the endpoint has an explicit device-token exemption.
- Registering an existing installation requires its current unexpired token and rotates it immediately; old tokens stop authorizing requests.
- Device tokens expire without sliding renewal. Rotation issues a new configured lifetime.
- Internal service credentials are separate from both user identity systems.

### Runtime authentication schemes and exemptions

- `CustomerBearer` is the Customer API HTTP bearer scheme. Customer
  self-service operations use it according to their `[Authorize]` policies;
  modern booking and payment operations require it together with
  `X-Device-Token`.
- `BusinessBearer` is the Business API HTTP bearer scheme. Business management
  operations additionally enforce the applicable Owner, Employee, Admin, and
  company/branch-assignment policies.
- `X-Device-Token` is an API-key-style installation credential. Modern
  configuration, catalog, draft, and pricing operations are device-only;
  booking confirmation and payment operations require both device and Customer
  bearer authentication.
- Internal routes on either host require all four HMAC credentials described in
  section 6. HMAC credentials never satisfy a bearer or device requirement, and
  either host's bearer token never satisfies HMAC.
- The Lahza webhook is authenticated only by `X-Lahza-Signature` over the exact
  bounded raw body. It is exempt from JWT, device, and internal HMAC.
- Device registration, implemented Customer and Business authentication entry
  points, implemented OAuth initiation/callback routes, health, and enabled
  Swagger are anonymous. Protected OAuth link/unlink operations retain their
  Customer bearer requirement. Exemptions are operation-specific; neither API
  has a global Swagger security requirement.

## 5. API conventions

### Versioning and routes

- New routes use URI versioning under `/api/v1`.
- Customer routes use customer concepts without a redundant `customer` segment.
- Business owner routes use `/api/v1/business`.
- Service-to-service routes use `/api/v1/internal`.
- Lahza uses the provider-specific anonymous route `/api/lahza/webhook`.

### Language

- Supported user-facing languages on both APIs are `ar` and `he`.
- Where an operation documents `language`, its presence is authoritative on
  reads and writes. Exactly one trimmed, nonblank, case-insensitive `ar` or
  `he` value is accepted. Duplicate, blank, comma-delimited, or unsupported
  values return localized `400 language_invalid`; the header cannot rescue an
  invalid explicit query.
- Otherwise `Accept-Language` is parsed by positive quality and then wire order.
  `ar-*` maps to Arabic and `he-*` maps to Hebrew. Missing, blank, malformed,
  unsupported, wildcard-only, or all-`q=0` headers safely default to Arabic.
- The selected success language and Problem Details language are identical.
  Localized responses carry `Content-Language: ar|he` and merge
  `Vary: Accept-Language`.
- Catalog records retain both Arabic and Hebrew values.
- Missing optional Hebrew content falls back to the required Arabic value.
- User-facing errors on either host use a stable code plus a localized message.
- Business user-facing success and error payloads follow the same selection
  rules, including mutations and authorization failures.
- Internal HMAC and Lahza webhook diagnostics are deliberately English,
  machine-oriented, and safe. They omit `language`, `Content-Language`, and
  language negotiation. Health and Swagger are also language-neutral.
- Language changes presentation only. It cannot change authorization,
  ownership, persistence, money, versions, selection rules, or idempotency
  identity.

### Standard error

Application-generated failures use `application/problem+json` and RFC 7807
`ProblemDetails` with these extensions:

```json
{
  "type": "https://api.ghseeli.example/errors/checkout_draft_expired",
  "title": "Request could not be completed",
  "status": 410,
  "detail": "Localized customer-safe message",
  "code": "checkout_draft_expired",
  "correlationId": "string",
  "language": "ar",
  "fieldErrors": {
    "fieldName": ["localized message"]
  }
}
```

Rules:

- `code` is stable and machine-readable.
- `type` is exactly `https://api.ghseeli.example/errors/{code}`.
- `correlationId` exactly matches the response `X-Correlation-Id`.
- `language` is present only on localized user-facing problems.
- `fieldErrors` is present only for field failures. Its camelCase JSON-path
  keys (including indexes) are ordinally ordered; values are nonempty,
  deduplicated, deterministic localized catalog messages. Framework exception
  names and attempted values are never returned.
- `detail` never exposes stack traces, SQL, secrets, or provider internals.
- Validation failures return `400`.
- Missing authentication returns `401`.
- Insufficient role/ownership returns `403`.
- Missing resources return `404`.
- State, version, and idempotency conflicts return `409`.
- Expired checkout drafts return `410 checkout_draft_expired`.
- Upstream unavailability returns `503`; it must not be returned as an empty successful result.

The shared generic stable-code registry is:

| Status | Stable code |
|---:|---|
| 400 | `language_invalid`, `request_invalid` |
| 401 | `customer_authentication_required`, `business_authentication_required` |
| 403 | `customer_authorization_forbidden`, `business_authorization_forbidden` |
| 404 | `resource_not_found` |
| 405 | `method_not_allowed` |
| 409 | `request_conflict` |
| 413 | `request_body_too_large` |
| 415 | `unsupported_media_type` |
| 500 | `unexpected_error` |
| 503 | `service_unavailable` |

Feature registries take precedence over generic codes: `device_*`,
`configuration_*`, `catalog_*`, `checkout_*`, `pricing_*`, `booking_*`,
`payment_*`, `lahza_*`, `internal_*`, and the Business domain code registry.
Existing code/status/detail pairs from Steps 3 and 5–14 are frozen. Exact
Arabic/Hebrew generic strings and all scenario snapshots are normative in
[`STEP_15_HTTP_TEST_PLAN.md`](STEP_15_HTTP_TEST_PLAN.md); adding a translation
must not rename a stable code.

Internal HMAC problems retain their English machine diagnostics and stable
authentication, permission, HTTPS, body, media-type, idempotency, replay, and
unavailability codes. Missing-header diagnostics may include only the
deterministic missing header names. Lahza problems retain their English
signature/body/content/event diagnostics. Neither diagnostic family may echo
raw bodies, signatures, secrets, nonces, JWTs, device tokens, idempotency keys,
PII, provider payloads, or internal row IDs.

### Response headers and transport hardening

- Every response carries a bounded `X-Correlation-Id`; a valid supplied value is
  echoed, while missing, blank, comma/CRLF-bearing, or overlong values are
  replaced before use or forwarding.
- Every application problem and every sensitive or mutable success carries
  `Cache-Control: no-store`. Problems carry no `ETag`, `Last-Modified`, or
  `Set-Cookie`.
- Both hosts emit `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
  `Referrer-Policy: no-referrer`, a restrictive `Permissions-Policy`, and a
  restrictive API Content Security Policy on success, empty, redirect,
  authentication, fallback, 404, 405, and error responses. Swagger UI uses a
  separate narrow CSP.
- Production HTTPS responses carry configured HSTS. Development/plain HTTP does
  not. Internal HTTP still fails closed unless the explicit trusted local
  development override is enabled.
- JSON mutation bodies are bounded at 65,536 bytes unless an earlier feature
  contract states a stricter bound. Unsupported content types and charsets fail
  as Problem Details rather than framework HTML.

### Correlation and idempotency

- Every response carries `X-Correlation-Id`.
- A supplied valid correlation ID is propagated and echoed; otherwise the receiving API creates one and echoes it.
- Correlation IDs are bounded safe tokens only; overlong or CRLF-bearing values are rejected and replaced before logging or forwarding.
- Cross-system mutating requests require `Idempotency-Key`.
- Customer booking confirmation uses `orderGuid` as the logical idempotency source.
- Each receiving API stores the key, operation, request hash, status, and serialized response body.
- Reusing a key with different request content returns `409 idempotency_conflict`.
- Reusing a completed identical request returns the original logical result without re-executing the operation.
- Customer in-progress transport claims have a durable, per-generation owner
  token and a separately renewable lease. The receiver renews at the configured
  safe fraction of `InProgressRecoverySeconds` for the entire endpoint
  execution, including reconciliation calls. The computed interval must be at
  least 100 ms and strictly before the configured safety margin; invalid values
  fail startup instead of being clamped. Renewal retries transient database
  failures with bounded backoff while before the last confirmed expiry's safety
  deadline. Ownership loss or an unconfirmed renewal cancels the server-provided
  endpoint token so cooperative downstream work stops before further side
  effects. Each renewal and completion uses a fresh dependency-injection scope
  and a bounded server token independent of client disconnects.
- Once endpoint execution begins, client cancellation cannot cancel heartbeat or
  durable completion. Cancellation or exceptions after that boundary are
  side-effect-ambiguous: middleware preserves the owned `InProgress` claim and
  never deletes it. Callback/reconciliation domain operations are independently
  idempotent, so an abandoned claim may be safely reclaimed only after its lease
  expires. Completion, reclaim, and renewal verify both owner and active lease;
  a stale generation cannot overwrite or delete its successor. A crash stops
  renewal and is eventually reclaimable. Owner tokens are internal concurrency
  data and are never returned or logged.
- Internal retries use a fresh nonce on every attempt and the same `Idempotency-Key` and `X-Correlation-Id`.

### Business reservation durability

- A successful Business reservation is immediately and durably `Pending`; legacy
  `Reserved` rows are normalized by the Step 13 migrations and remain capacity-occupying.
- `reservationExpiresAtUtc` is therefore `null`. Capacity remains consumed until a later explicit business booking-status transition releases or completes the reservation.
- Authoritative catalog, selection, price, duration, service-area, aggregate-slot, and capacity checks run in the same serializable transaction that creates the reservation and work order.
- Reservation replay uses a canonical semantic request hash: item and add-on ordering do not affect replay identity, while changed values return an idempotency conflict.

## 6. Internal HTTPS security

Initial integration uses HMAC-authenticated HTTPS and does not depend on an external identity provider or message broker.

Required headers:

- `X-Ghseeli-Service-Id`
- `X-Ghseeli-Timestamp`
- `X-Ghseeli-Nonce`
- `X-Ghseeli-Signature`
- `X-Correlation-Id`
- `Idempotency-Key` for mutating operations

Canonical request signing uses shared wire version `ghseeli-hmac-sha256-v1` and the exact UTF-8 canonical form:

```
ghseeli-hmac-sha256-v1
{serviceId}
{UPPERCASE_METHOD}
{normalizedPathAndQuery}
{timestampUtcIso8601}
{nonce}
{sha256BodyHex}
```

Rules for canonical fields:

- `normalizedPathAndQuery` keeps the absolute request path and appends query parameters sorted by key and then value using ordinal comparison.
- Query keys and values are percent-encoded before joining with `&`.
- Empty bodies use the SHA-256 of the empty byte sequence.
- Signatures are lowercase hexadecimal HMAC-SHA256 values and comparisons are constant-time.

Rules:

- Each direction uses a separate rotatable secret with active and next slots.
- Secrets are loaded from environment variables or user secrets only.
- Plain API secrets are never logged or committed.
- Business JWTs never authorize `/api/v1/internal/*` routes.
- Requests outside the configured clock-skew window are rejected.
- Previously accepted nonces inside the replay window are rejected from a persistent Business database table keyed by `{serviceId, nonce}`.
- Internal `POST` requests persist idempotency records keyed by `{serviceId, operation, idempotencyKey}` with request hash, status, content type, body, timestamps, and expiry.
- Customer typed clients use explicit timeouts and bounded retries only for retry-safe GET snapshot requests and idempotent POST validate requests on network errors, `408`, `429`, and `5xx`.
- Safe retries reuse the same idempotency key and correlation ID and generate a fresh nonce per attempt.
- HTTPS is required by default; development HTTP is allowed only by an explicit override and only after trusted ASP.NET forwarded-header processing.

## 7. Final route map

The exhaustive verb/path/security inventories are maintained in
[`STEP_16_HTTP_TEST_PLAN.md`](STEP_16_HTTP_TEST_PLAN.md) and are checked
against runtime endpoint metadata and both OpenAPI documents. The tables below
summarize the cross-domain routes; they do not create compatibility aliases.

### Customer API

| Method | Route | Device | Customer JWT | Purpose |
|---|---|---:|---:|---|
| POST | `/api/v1/devices/register` | No | No | Register/rotate a device token |
| GET | `/api/v1/configuration` | Yes | No | Get localized active app configuration |
| GET | `/api/v1/catalog/categories` | Yes | No | Browse localized categories |
| GET | `/api/v1/catalog/businesses` | Yes | No | Browse eligible companies/branches |
| GET | `/api/v1/catalog/businesses/{id}` | Yes | No | Get business details |
| GET | `/api/v1/catalog/businesses/{id}/offerings` | Yes | No | Browse offerings |
| POST | `/api/v1/catalog/businesses/{businessId}/branches/{branchId}/available-slots` | Yes | No | List authoritative capacity-aware appointment slots |
| GET | `/api/v1/catalog/offerings/{id}` | Yes | No | Get offering and add-on rules |
| POST | `/api/v1/pricing/reprice` | Yes | No | Stateless authoritative reprice for a checkout-like intent |
| POST | `/api/v1/checkout/drafts` | Yes | No | Create anonymous draft |
| GET | `/api/v1/checkout/drafts/{orderGuid}` | Yes | No | Read device-owned draft |
| PUT | `/api/v1/checkout/drafts/{orderGuid}` | Yes | No | Update anonymous draft intent |
| POST | `/api/v1/checkout/reprice` | Yes | No | Reprice a device-owned draft using `X-Order-Guid` and `expectedVersion` |
| POST | `/api/v1/bookings/from-draft` | Yes | Yes | Confirm a draft as a booking |
| POST | `/api/v1/payments/intents` | Yes | Yes | Initialize Lahza hosted checkout from booking total |
| GET | `/api/v1/payments/{id}` | Yes | Yes | Read owned payment |
| POST | `/api/v1/payments/{id}/verify` | Yes | Yes | Verify an owned Lahza transaction |
| POST | `/api/lahza/webhook` | No | No | Lahza HMAC-SHA256 signature-protected webhook |

Customer profile, vehicle, and address routes remain Customer API responsibilities and will be versioned during their migration.

### Business API

| Method group | Route | Business JWT | Purpose |
|---|---|---:|---|
| POST | `/api/v1/business/auth/*` | No/varies | Business registration and login |
| GET/PUT/POST | `/api/v1/business/company/*` | Yes | Owned company and branch management |
| GET/POST/PUT/DELETE | `/api/v1/business/catalog/*` | Yes | Category, offering, add-on group, and choice management |
| GET/POST/PUT/DELETE | `/api/v1/business/availability/*` | Yes | Schedules, closures, and capacity |
| POST | `/api/v1/business/work-orders/{id}/transitions` | Yes | Allowed status transition |
| POST | `/api/v1/business/admin/booking-status-outbox/{eventId}/requeue` | Global Admin only | Explicit dead-letter recovery requiring a bounded `Idempotency-Key`. Each new audited request against a dead letter increments its delivery generation. Repeating the same request returns `AlreadyRequeued` without another increment; non-dead-letter events with a new request return 409. |

### Business API internal endpoints

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/internal/catalog/snapshot` | Return a versioned catalog snapshot |
| POST | `/api/v1/internal/appointments/validate` | Validate catalog selections, duration, price, service area, and slot |
| POST | `/api/v1/internal/appointments/available-slots` | Generate branch-local slots with current remaining capacity |
| POST | `/api/v1/internal/reservations` | Idempotently reserve an appointment and create a work order |
| GET | `/api/v1/internal/reservations/{reference}` | Reconcile reservation/work-order state |

Business internal routes accept only HMAC-authenticated internal service calls and never accept Business or Customer JWTs.

### Customer API internal callback endpoints

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/v1/internal/bookings/status` | Idempotently apply a business status callback |
| GET | `/api/v1/internal/bookings/{reference}` | Reconcile customer booking state |
| POST | `/api/v1/internal/bookings/{reference}/reconcile` | Query authoritative Business state and repair a missed callback |

### Independent Swagger documents

Each host owns `/swagger/v1/swagger.json` and `/swagger`. Swagger is enabled in
Development or by explicit non-production configuration and is disabled by
default in Production. Each UI reads its own document and "Try it" targets only
that host.

The Customer document contains only retained Customer routes, Customer
internal callbacks, health, OAuth, and Lahza webhook only. The Business
document contains Business owner/staff and Business internal routes only.
Neither document requires the other implementation or database.

Both OpenAPI 3 documents provide deterministic unique operation IDs, summaries,
descriptions, tags, request/response schemas, JSON formats, requiredness,
nullability, bounds, string enum values, defaults, safe examples, and all
runtime statuses with `application/problem+json`. They document reusable
Problem Details/`fieldErrors`, `X-Correlation-Id`, `Cache-Control`,
`Content-Language`, and localization precedence where applicable.

Security is attached per operation rather than globally. The documents define
Customer bearer, Business bearer, device token, the complete HMAC quartet,
`Idempotency-Key`, `X-Order-Guid`, and Lahza signature only where applicable.
Anonymous, OAuth, health, and webhook exemptions are explicit. Examples use
placeholders and contain no credential, signature, secret, token, PII,
production host, provider payload, or internal database ID.

## 8. Booking status authority and delivery

- Business is authoritative for operational booking status. Customer callback payloads contain only immutable public references, status, sequence, event ID, and occurrence time; identity, ownership, catalog snapshots, and money are never accepted from callbacks.
- Allowed transitions are `Pending→Confirmed/Cancelled`, `Confirmed→InProgress/Cancelled/NoShow`, and `InProgress→Completed/Cancelled/NoShow`. Completed, Cancelled, and NoShow are terminal.
- Capacity classification is shared with those transition rules: `Pending`, legacy `Reserved`, `Confirmed`, and `InProgress` consume capacity; only `Completed`, `Cancelled`, and `NoShow` release it.
- Every Business transition updates reservation/work-order state and inserts a durable outbox message in one transaction. Normal delivery retries preserve event ID, request bytes, delivery generation, transport idempotency key, and correlation ID while using a fresh HMAC timestamp and nonce. Generation 0 uses `booking-status-{eventId:N}`. Each explicit audited dead-letter requeue atomically increments the durable generation and uses `booking-status-{eventId:N}-g{generation}` without changing the domain event or body.
- Per-reservation delivery is strict head-of-line ordering: pending, leased, and dead-letter messages all block later sequences. A dead letter remains an ordering barrier until a global Business Admin explicitly requeues it through the audited recovery endpoint. Requeue clears only delivery bookkeeping and never changes the immutable event payload, hash, reservation, work order, status, or sequence.
- Requeue history is durable and unique by event/generation and event/request ID. Repeating the same admin request returns its original generation as a stable successful no-op even after lease, delivery, or another dead-letter cycle. A new request can recover each later dead-letter exactly once; active leases and new requests against non-dead-letter states conflict. Unknown IDs return a non-enumerating localized problem. Silent skip/discard is not supported.
- The outbox hosted worker requires a valid Customer callback URL and signing configuration at startup outside the explicit `Testing` disable mode. It yields during startup, contains non-cancellation database/transport persistence failures within each cycle, logs only safe operation/event data, applies bounded delays, continues dispatching, and exits cleanly on host cancellation.
- Customer persists the booking update and processed-message inbox row in one serializable transaction. Duplicate event IDs replay only when the raw request hash matches. Lower sequences are recorded as stale and cannot move state backward; a different event at the current sequence is rejected.
- A forward sequence gap is accepted only when the current-to-target status edge is directly allowed. A new event cannot self-transition, and terminal states cannot transition.
- Reconciliation reads minimal authoritative Business state over signed HTTPS and applies the same reference, sequence, and direct-transition rules as callbacks. It cannot skip an invalid edge, move backward, or leave a terminal state; an already-current read is a no-op.

## 9. Initial integration contracts

Contracts are versioned independently from database schemas.

### Catalog snapshot

Contains:

- contract version;
- catalog version;
- generated UTC timestamp;
- businesses and branches;
- service areas;
- categories;
- offerings;
- add-on groups and choices;
- Arabic/Hebrew values;
- activity and ordering data.

### Customer configuration

Contains:

- selected response `language` (`ar` or `he`);
- support contact values such as the active support email and phone number;
- localized display content using required Arabic values and optional Hebrew values;
- localized legal notice text plus stable privacy-policy and terms URLs;
- maintenance mode state plus an optional localized maintenance message.

Rules:

- Arabic values are required for every localized configuration record.
- Hebrew values are optional and fall back to Arabic when omitted.
- The explicit `language` query override accepts only `ar` or `he`; malformed or unsupported `Accept-Language` headers fall back to Arabic unless they still contain a supported weighted language.
- The public Customer API returns only the selected localized values; it does not expose arbitrary JSON blobs or inactive records.
- Device-token middleware protects `GET /api/v1/configuration`; customer JWTs are not required.
- Migrations do not seed placeholder production configuration. Each environment must provision an active configuration record before the endpoint can return data.
- When no active configuration is available, the endpoint returns a stable unavailable problem instead of an empty success payload.

### Customer catalog read model

- Customer catalog browsing persists local read-model IDs and also returns the upstream Business API `sourceId` for providers, branches, categories, offerings, add-on groups, and add-on choices.
- `CatalogReadModel` configuration contains validated `freshWindowSeconds`, `maxStaleWindowSeconds`, `leaseDurationSeconds`, and `providers[]` registrations (`sourceCompanyId`, `enabled`, `order`).
- The committed `appsettings.json` provider list stays empty and safe for production. Real provider registrations must come from environment variables, user secrets, or environment-specific configuration.
- Registration synchronization creates or updates configured providers and disables removed providers without deleting historical cached graph data.
- Public catalog responses are device-token protected, anonymous to customer JWTs, localized with the same Arabic/Hebrew rules as customer configuration, and always return `Cache-Control: no-store`.
- `GET /api/v1/catalog/categories` accepts an optional `businessId` filter. Unfiltered category responses include owning business context so results stay unambiguous.
- `GET /api/v1/catalog/businesses` and `GET /api/v1/catalog/businesses/{id}/offerings` accept optional `branchId` and `categoryId` filters. Cross-provider or cross-business filter combinations return `400 catalog_filter_mismatch`.
- Leaving `providers[]` empty is an intentional safe default: list browse routes return localized empty collections and do not contact Business until a provider is explicitly configured.
- Normal reads refresh missing or hard-stale provider caches on demand. `refresh=true` forces verification even when cache data already exists, and Business client configuration is validated only when a refresh call is attempted.
- Same version plus same deterministic content hash is a no-op verification that only refreshes metadata. Same version with a different hash is reapplied safely. Lower source versions are rejected and logged.
- If no usable cache exists after refresh, the public API returns `503 catalog_unavailable` rather than an empty `200`. Missing business or offering resources return stable localized `404` problems.

### Appointment validation

Request contains:

- business/branch public ID;
- offering public IDs and quantities;
- selected add-on public IDs and quantities;
- `requestedSlotStartUtc` as an ISO 8601 `DateTimeOffset` value; callers may send `Z` or an explicit offset and Business API normalizes it to UTC;
- customer location facts needed for service-area validation;
- expected catalog version;
- currency.

Response contains:

- validity and stable error codes;
- current catalog version;
- normalized selections;
- base subtotal and add-on subtotal;
- authoritative business-controlled fees;
- duration;
- slot/service-area result;
- reservation eligibility;
- price-expiry timestamp.

Operational rules:

- Appointment-validation request and response timestamps are always evaluated as UTC instants. Response facts emit UTC timestamps only.
- Recurring schedules and date overrides are authored in branch-local clock time. When `endLocalTime` is less than `startLocalTime`, the window is treated as an overnight window that continues into the next local date.
- If a requested slot lands in an ambiguous daylight-saving clock time or crosses a daylight-saving transition, validation fails closed as unavailable.
- `configuredCapacity` reports the selected schedule or override capacity.
- Available-slot search subtracts overlapping `Pending`, `Confirmed`, and
  `InProgress` reservations and can include or omit full slots.
- Search results are advisory snapshots. Reservation creation remains the
  authoritative serializable capacity check, so concurrent customers cannot
  overbook a capacity-one slot.

The Customer API may add its own disclosed customer-side tax, discount, or platform-fee components. It must not override Business API prices.

The add-on selection type is a string enum with exactly:

- `SingleChoice`: at most one active choice;
- `SegmentedSingleButtonChoice`: the same single-choice invariant with a
  segmented-button presentation hint;
- `MultipleChoice`: multiple active choices up to configured limits;
- `QuantityCounter`: per-choice and aggregate quantity constraints apply;
- `FixedIncludedChoice`: exactly one active included default that cannot add
  price or duration.

Required/default/minimum/maximum/quantity, active-state, duplicate-choice, and
cross-group rules are server validated. Item and add-on wire order does not
change semantic replay identity; normalized response ordering is deterministic.
Selection types are serialized as strings and integer enum values are rejected.

### Authoritative customer pricing

- Direct repricing accepts the checkout intent but is stateless. Draft repricing
  requires device ownership, body `expectedVersion`, and `X-Order-Guid`.
- Business validates source business, branch, catalog version, offering,
  add-on, quantity, slot, duration, and service area. Customer then applies only
  its configured disclosed service fee, tax, and discount components.
- Client-provided subtotal, add-on subtotal, fee, tax, discount, total,
  currency, provider result, duration, capability, transaction, paid, or status
  fields are never authoritative. Unknown JSON extension fields cannot change
  canonical pricing or idempotency identity.
- The response owns normalized selections, catalog version, quote timestamp,
  item/base and add-on subtotals, discounts, service fee, taxable subtotal, tax,
  grand total, currency, duration, and payment capabilities. Decimal rounding
  and response ordering are deterministic.
- `Card`/`CreditCard` is available only with a valid configured Lahza HTTPS API
  endpoint and secret key. `Wallet`, `CashOnArrival`, and `ThirdParty` remain unavailable
  with stable localized reason codes until their server flows exist.

### Reservation

Customer confirmation uses `POST /api/v1/bookings/from-draft`, requires both the
device token and Customer JWT, sends `orderGuid` in `X-Order-Guid`, and sends
`expectedVersion` plus `cancellationPolicyAcknowledged` in the JSON body.
The Customer API persists a durable confirmation attempt before calling Business,
so retries reuse the exact reservation body and `booking-{orderGuid:N}`
idempotency key.

Request contains:

- cross-system booking reference;
- `orderGuid`;
- validated appointment selection;
- price/version proof;
- requested slot;
- customer-safe contact and location snapshot required to perform the service;
- cancellation/expiry policy acknowledgement.

Response contains:

- reservation public ID;
- work-order public ID;
- accepted snapshot;
- status;
- reservation expiry when applicable.

Business persists the reservation and work order in one serializable transaction.
Unique `orderGuid`, customer booking reference, reservation public ID, and work
order public ID constraints prevent duplicates. Customer persistence uses a
unique `orderGuid` and immutable provider, branch, service, selection, vehicle,
location, price, fee, tax, duration, and appointment snapshots. A lost Business
response is recovered by replaying the durable attempt with the same key; no
payment or Lahza transaction initialization occurs during confirmation.

### Booking status callback

Contains:

- callback ID;
- cross-system booking reference;
- work-order public ID;
- previous and new status;
- occurred UTC timestamp;
- reason code and customer-safe localized message data where applicable.

Customer API validates the transition and stores callback IDs to prevent duplicate application.

## 10. Failure behavior

### Catalog reads

- Customer API serves its last successfully synchronized read model when it is within the configured staleness limit.
- Responses disclose freshness metadata when stale data is served.
- When no providers are configured, list browse routes return localized empty collections instead of a failure.
- With no usable snapshot, return `503 catalog_unavailable`.
- Never replace an upstream failure with an empty `200` list.

### Pricing and booking

- Repricing and final booking fail closed when Business API cannot authoritatively validate current price and availability.
- Cached catalog data may support browsing but cannot authorize final booking.
- Customer API does not create a payable booking until Business API accepts the idempotent reservation.
- If Customer API persistence fails after reservation acceptance, the reservation remains reconcilable and expires or can be recovered by reference.

### Internal authentication and failure mapping

- Missing or invalid internal authentication headers, unknown services, bad signatures, stale timestamps, and replayed nonces return `401` with a safe JSON problem payload and echoed correlation ID.
- Authenticated internal services that lack the required operation permission return `403`.
- Insecure internal HTTP requests return `403 https_required` unless development HTTP is explicitly enabled.
- Missing or invalid `Idempotency-Key` values return `400`.
- Reusing a Customer internal transport idempotency key with a changed canonical
  request returns `409 idempotency_conflict`. Its bounded canonical identity is
  the method, normalized path/query, normalized content media type/charset, and
  exact body bytes; arbitrary request headers are excluded. Valid content types
  use the normalized media type/charset. Malformed or overlong content types use
  a bounded class, UTF-8 length, and SHA-256 digest, so distinct invalid values
  cannot collapse while raw unbounded header values are never persisted or logged. Consequently, an
  identical wrong-content-type request deterministically replays its stored
  `415`, while correcting it to `application/json` requires a new key.
- Oversized internal request bodies return `413`.
- When an idempotent in-progress result cannot be replayed in time, return `503 idempotency_unavailable`.
- Customer transport-idempotency retention deletes expired completed records and
  expired orphaned in-progress claims in configurable bounded batches using a
  clock-driven conditional predicate. It never removes an actively renewed
  claim; cleanup rechecks its current lease expiry at deletion time.
- Anonymous or expired Business JWT requests return `401 business_authentication_required`; authentication errors never reuse request media-type codes.
- Customer typed clients map `401`/`403` to authentication exceptions, `409` to conflict exceptions, `410` to expired/gone exceptions for checkout drafts, malformed or empty successful payloads to contract exceptions, persistent timeouts to timeout exceptions, and retry-exhausted network/`408`/`429`/`5xx` failures to unavailable exceptions.

### Authoritative business mutations

- Business company, catalog, branch, service-area, schedule, and override mutations increment the authoritative catalog version atomically with the underlying write.
- Reads never bump catalog version.
- When optimistic concurrency or relational uniqueness detects a stale or overlapping write, the Business API returns `409` instead of silently dropping a version increment or surfacing a `500`.
- Concurrent work-order status transitions return `409 BOOKING_TRANSITION_CONFLICT` with Problem Details.

### Status callbacks

- Duplicate callback IDs return the original success.
- Invalid or out-of-order transitions return `409`.
- A reconciliation endpoint is available for support when a callback could not be delivered.

## 11. Acceptance test matrix

These are behavioral contracts for later roadmap steps. Tests are written before each corresponding implementation and must first fail for the expected missing behavior.

### Application isolation

- Customer API starts with only the Customer database configured.
- Business API starts with only the Business database configured.
- Customer JWT is rejected by Business API.
- Business JWT is rejected by Customer API.
- Neither API references the other's implementation assembly.

### Ownership and authorization

- Business owners cannot access another company's resources.
- Employees receive only assigned permissions.
- Customers cannot call business management routes.
- Anonymous devices cannot confirm bookings or create payments.
- Authenticated customers cannot read another customer's drafts, bookings, vehicles, addresses, or payments.

### Device security

- Registration returns a token while persistence stores only a hash.
- Missing, invalid, expired, rotated, and inactive tokens are rejected.
- Explicit infrastructure exemptions remain accessible.
- A device cannot access another device's anonymous draft.

### Localization and errors

- Arabic and Hebrew requests return the correct localized content.
- Unsupported language falls back to Arabic.
- Error codes remain identical across languages.
- Problem responses contain correlation IDs and no sensitive internals.
- Invalid fields return stable field-level errors.

### Internal authentication

- Valid signed requests succeed.
- Invalid signature, old timestamp, reused nonce, unknown client, and modified body fail.
- Secrets and raw signatures are absent from logs.
- The same idempotency key and body return one logical result.
- The same idempotency key with a different body returns `409`.

### Catalog and availability

- Only active businesses, offerings, groups, and choices are published.
- Catalog versions change when authoritative catalog behavior changes.
- Invalid add-on constraints cannot be saved or validated.
- Stale read data is identified.
- No snapshot plus upstream outage returns `503`, not an empty success.

### Pricing and drafts

- Client-supplied prices are ignored/rejected.
- `POST /api/v1/pricing/reprice` is stateless, returns `Cache-Control: no-store`, accepts the checkout intent envelope, and never creates or mutates a draft.
- `POST /api/v1/checkout/reprice` is device-owned draft repricing only; it requires `X-Order-Guid` plus a body `expectedVersion`, fails closed on missing/wrong device ownership, expiry, or optimistic concurrency, and increments the public draft version exactly once on success.
- Direct and `orderGuid` repricing return equal totals for equal selections.
- Mixed-business drafts are rejected.
- Anonymous drafts are owned only by the issuing device identity and are addressed by a public `orderGuid`, never by an internal database key.
- Draft writes require an expected public version and fail closed on expiry or optimistic concurrency conflicts.
- Draft intent stores immutable source IDs plus anonymous vehicle/location snapshots and always returns `requiresReprice=true` until authoritative repricing succeeds.
- Successful draft repricing persists an immutable authoritative pricing snapshot with itemized selections, catalog version, currency, fees, tax, grand total, and quote timestamp; later draft intent mutations delete that stored snapshot and restore `requiresReprice=true`.
- Authoritative repricing validates each draft item against Business with the exact catalog version and stable per-request idempotency keys, refreshes stale catalog data at most once, and never trusts client-supplied currency, fee, tax, subtotal, or total fields.
- Invalid min/max, quantity, inactive option, expired draft, stale version, unavailable slot, and out-of-area location return stable errors.
- Decimal rounding is deterministic.
- Safe pricing defaults are non-billable until configured: currency defaults to `ILS`, tax defaults to `0`, service fees default to `None`, and only a valid configured Lahza HTTPS endpoint and secret enable `CreditCard`. `Wallet`, `CashOnArrival`, and `ThirdParty` remain disabled with localized reason codes until implemented.

### Booking integration

- Repeated draft confirmation creates at most one customer booking and one business work order.
- Business rejection creates no payable customer booking.
- Cross-system references correlate both records without shared database IDs.
- Catalog edits do not alter confirmed booking snapshots.
- Duplicate and out-of-order status callbacks do not corrupt state.
- Reconciliation identifies and repairs supported mismatch cases.

### Payment

- `POST /api/v1/payments/intents` requires `UserPolicy`, the existing device token, and a
  bounded `Idempotency-Key` (1-128 non-control characters). Its JSON contract contains only
  public `CustomerBooking.PublicReference` as `bookingId` and method `Card`. It never accepts
  card data, a provider reference, or a provider authorization code. Unknown extension
  fields, including client-authoritative money, currency, transaction, paid, or status
  values, are ignored and cannot affect the canonical request.
- Payment amount and currency come only from the immutable `CustomerBooking.GrandTotal` and
  `CustomerBooking.Currency`. Supported two-decimal currencies are `ILS`, `JOD`, and `USD`;
  conversion to Lahza minor units is exact and checked.
- Transaction initialization is eligible only while the canonical booking status is `Pending` or
  `Confirmed`. `InProgress`, terminal, already-paid, refunded, or previously associated
  bookings cannot create another server payment.
- Card is available only when the Lahza API uses an absolute HTTPS URL and a non-placeholder
  secret key. `Wallet`, `CashOnArrival`, and `ThirdParty` remain unavailable with stable
  localized error/reason codes.
- Customer payment ownership and every idempotency replay require both the authenticated customer ID and issuing device
  ID. Missing and wrong-owner reads/creates return the same localized `404`.
- A server payment ID and deterministic Lahza provider reference are committed before Lahza I/O.
  No SQL transaction spans the network call. A random, expiring database lease grants one
  API instance ownership of transaction initialization; acquisition, completion, and release are
  owner-conditional, expired leases are reclaimable, and stale owners cannot overwrite the
  winner. The unique booking association, device-scoped idempotency records, and stable Lahza
  reference prevent duplicate local records. Ambiguous initialization is durably marked and
  fails closed instead of issuing another provider initialization; the owned verification
  endpoint can reconcile any transaction that Lahza reports for that stable reference.
  Changed canonical requests conflict. A different key
  for the same owned booking is durably associated with and replays the existing logical
  payment.
- Lahza initialization returns a hosted HTTPS checkout URL and reference. Only
  the hosted checkout URL and reference are returned to the mobile client; card data and
  sensitive Lahza material are never accepted, persisted, returned, or logged.
- `POST /api/v1/payments/{id}/verify` calls Lahza from the server and requires exact reference,
  amount, currency, and nested transaction-status validation. A callback redirect or provider
  envelope status never marks a booking paid by itself.
- `POST /api/lahza/webhook` accepts bounded JSON only, verifies `X-Lahza-Signature` as
  HMAC-SHA256 over the exact raw body using constant-time comparison, and stores a stable
  event identity plus SHA-256 body
  hash. Identical events no-op; reused IDs with changed bodies conflict; incomplete internal
  processing returns retryable `5xx` rather than an acknowledgement.
- Webhook mutations match the persisted provider reference and verify amount, currency, and
  provider transaction identity. Safe transitions are
  `Pending -> Completed|Failed`, `Failed -> Completed`, and `Completed -> Refunded`.
  Failure/cancel after completion and success after refund are no-ops.
- Verified events with an unknown reference or invariant mismatch are durably quarantined with
  a safe reason and acknowledged without payment mutation; only true persistence/transient
  failures remain retryable.
- Lahza `refund.pending` and `refund.processing` events are recorded without clearing paid
  state. A verified processed refund received before success is stored as a deferred durable
  receipt. Once matching success establishes the transaction, reconciliation converges the
  payment to `Refunded` and the booking to unpaid, including concurrent cross-instance event
  ordering; duplicate receipts remain idempotent.
- Intent request bodies are limited to exactly 65,536 bytes for both known content lengths
  and chunked bodies and return localized `payment_request_too_large` Problem Details.
- Intent validation uses the frozen mobile error contract: malformed requests and unknown
  methods return `payment_request_invalid`; wrong media types return
  `payment_unsupported_media_type`; missing keys return `idempotency_key_required`, while
  empty or malformed keys return `idempotency_key_invalid`. Disabled known methods return
  `payment_method_not_yet_supported`. Booking totals must be positive exact two-decimal
  values (`booking_not_payable`), and currency must be canonical uppercase `ILS`, `USD`, or
  `JOD` (`booking_currency_not_supported`); both booking failures return HTTP 409. Missing,
  unknown, and wrong-owner booking creation uses `booking_not_found`.
- The legacy unversioned payment create/refund/admin-status controller is non-routable.

## 12. Step 1 completion rules

Step 1 is complete when:

- the ownership table has no ambiguous current surface;
- route and versioning conventions are defined;
- language and error behavior are defined;
- internal authentication and idempotency behavior are defined;
- initial integration DTO responsibilities are defined;
- failure behavior is explicit;
- every meaningful boundary has an acceptance-test specification;
- discoveries made during the step are recorded and dispositioned.

No application split, schema migration, or production implementation is part of Step 1.
