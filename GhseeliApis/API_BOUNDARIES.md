# Ghseeli Customer and Business API Boundaries

Status: Roadmap Step 1 architecture contract

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
- payments, Stripe PaymentIntents, refunds, and Stripe webhooks;
- customer notifications.

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
| `PaymentsController` and `StripeWebhookController` | Customer API, redesigned around server totals |
| Customer booking history/cancel operations | Customer API |
| Company booking confirm/start/complete operations | Business API |
| `CompaniesController` public reads | Replaced by Customer API catalog read endpoints |
| `CompaniesController` writes | Business API |
| `ServicesController` and `ServiceOptionsController` writes | Business API |
| Service/company catalog reads | Customer API read model populated from Business API |
| `CompanyAvailability` writes and rules | Business API |
| Customer-facing availability lookup | Customer API backed by Business API validation |
| Customer wallet | Customer API, implementation deferred |
| Health and Swagger | Separate implementation in each API |

Existing customer routes may be replaced. Compatibility aliases are not required.

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
- Payment
- Notification

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

## 5. API conventions

### Versioning and routes

- New routes use URI versioning under `/api/v1`.
- Customer routes use customer concepts without a redundant `customer` segment.
- Business owner routes use `/api/v1/business`.
- Service-to-service routes use `/api/v1/internal`.
- Stripe retains `/api/stripe/webhook` unless a versioned Stripe migration is explicitly approved.

### Language

- Supported customer languages are `ar` and `he`.
- `Accept-Language` is the primary selector.
- A documented `language` query parameter may override it on read endpoints.
- Arabic is the default when no supported language is supplied.
- Catalog records retain both Arabic and Hebrew values.
- Responses return the selected localized value and may include a language code.
- Customer-facing errors use a stable code plus a localized message.
- Internal service errors use stable codes and non-localized diagnostic detail safe for logs.

### Standard error

New endpoints use RFC 7807 `ProblemDetails` with these extensions:

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
- `detail` never exposes stack traces, SQL, secrets, or provider internals.
- Validation failures return `400`.
- Missing authentication returns `401`.
- Insufficient role/ownership returns `403`.
- Missing resources return `404`.
- State, version, and idempotency conflicts return `409`.
- Expired checkout drafts return `410 checkout_draft_expired`.
- Upstream unavailability returns `503`; it must not be returned as an empty successful result.

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

## 7. Initial route map

### Customer API

| Method | Route | Device | Customer JWT | Purpose |
|---|---|---:|---:|---|
| POST | `/api/v1/devices/register` | No | No | Register/rotate a device token |
| GET | `/api/v1/configuration` | Yes | No | Get localized active app configuration |
| GET | `/api/v1/catalog/categories` | Yes | No | Browse localized categories |
| GET | `/api/v1/catalog/businesses` | Yes | No | Browse eligible companies/branches |
| GET | `/api/v1/catalog/businesses/{id}` | Yes | No | Get business details |
| GET | `/api/v1/catalog/businesses/{id}/offerings` | Yes | No | Browse offerings |
| GET | `/api/v1/catalog/offerings/{id}` | Yes | No | Get offering and add-on rules |
| GET | `/api/v1/availability` | Yes | No | Query customer-facing slots |
| POST | `/api/v1/pricing/reprice` | Yes | No | Stateless authoritative reprice for a checkout-like intent |
| POST | `/api/v1/checkout/drafts` | Yes | No | Create anonymous draft |
| GET | `/api/v1/checkout/drafts/{orderGuid}` | Yes | No | Read device-owned draft |
| PUT | `/api/v1/checkout/drafts/{orderGuid}` | Yes | No | Update anonymous draft intent |
| POST | `/api/v1/checkout/reprice` | Yes | No | Reprice a device-owned draft using `X-Order-Guid` and `expectedVersion` |
| POST | `/api/v1/bookings/from-draft` | Yes | Yes | Confirm a draft as a booking |
| GET | `/api/v1/bookings` | Yes | Yes | Customer booking history |
| GET | `/api/v1/bookings/{id}` | Yes | Yes | Customer booking details |
| POST | `/api/v1/bookings/{id}/cancel` | Yes | Yes | Request allowed cancellation |
| POST | `/api/v1/payments/intents` | Yes | Yes | Create Stripe intent from booking total |
| GET | `/api/v1/payments/{id}` | Yes | Yes | Read owned payment |
| POST | `/api/stripe/webhook` | No | No | Stripe signature-protected webhook |

Customer profile, vehicle, and address routes remain Customer API responsibilities and will be versioned during their migration.

### Business API

| Method group | Route | Business JWT | Purpose |
|---|---|---:|---|
| POST | `/api/v1/business/auth/*` | No/varies | Business registration and login |
| GET/PUT | `/api/v1/business/profile` | Yes | Owner/employee profile |
| GET/POST/PUT | `/api/v1/business/companies/*` | Yes | Owned company and branch management |
| GET/POST/PUT/DELETE | `/api/v1/business/categories/*` | Yes | Localized category management |
| GET/POST/PUT/DELETE | `/api/v1/business/offerings/*` | Yes | Offering management |
| GET/POST/PUT/DELETE | `/api/v1/business/offerings/{id}/addon-groups/*` | Yes | Add-on group management |
| GET/POST/PUT/DELETE | `/api/v1/business/addon-groups/{id}/choices/*` | Yes | Add-on choice management |
| GET/POST/PUT/DELETE | `/api/v1/business/availability/*` | Yes | Schedules, closures, and capacity |
| GET | `/api/v1/business/work-orders` | Yes | Owned work-order queue |
| GET | `/api/v1/business/work-orders/{id}` | Yes | Work-order details |
| POST | `/api/v1/business/work-orders/{id}/transitions` | Yes | Allowed status transition |
| POST | `/api/v1/business/admin/booking-status-outbox/{eventId}/requeue` | Global Admin only | Explicit dead-letter recovery requiring a bounded `Idempotency-Key`. Each new audited request against a dead letter increments its delivery generation. Repeating the same request returns `AlreadyRequeued` without another increment; non-dead-letter events with a new request return 409. |

### Business API internal endpoints

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/internal/catalog/snapshot?companyId={companyId}` | Return a versioned catalog snapshot or delta for one company |
| POST | `/api/v1/internal/appointments/validate` | Validate catalog selections, duration, price, service area, and slot |
| POST | `/api/v1/internal/reservations` | Idempotently reserve an appointment and create a work order |
| GET | `/api/v1/internal/reservations/{reference}` | Reconcile reservation/work-order state |
| POST | `/api/v1/internal/reservations/{reference}/cancel` | Apply an allowed customer cancellation |

Business internal routes accept only HMAC-authenticated internal service calls and never accept Business or Customer JWTs.

### Customer API internal callback endpoints

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/v1/internal/bookings/status` | Idempotently apply a business status callback |
| GET | `/api/v1/internal/bookings/{reference}` | Reconcile customer booking state |
| POST | `/api/v1/internal/bookings/{reference}/reconcile` | Query authoritative Business state and repair a missed callback |

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
- `configuredCapacity` reports the configured schedule or override capacity only. Reservation occupancy, work-order consumption, and live capacity depletion remain deferred to a later step.

The Customer API may add its own disclosed customer-side tax, discount, or platform-fee components. It must not override Business API prices.

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
payment or Stripe capture occurs during confirmation.

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
- Safe pricing defaults are non-billable until configured: currency defaults to `ILS`, tax defaults to `0`, service fees default to `None`, and only valid configured Stripe keys enable `CreditCard`. `Wallet`, `CashOnArrival`, and `ThirdParty` remain disabled with localized reason codes until implemented.

### Booking integration

- Repeated draft confirmation creates at most one customer booking and one business work order.
- Business rejection creates no payable customer booking.
- Cross-system references correlate both records without shared database IDs.
- Catalog edits do not alter confirmed booking snapshots.
- Duplicate and out-of-order status callbacks do not corrupt state.
- Reconciliation identifies and repairs supported mismatch cases.

### Payment

- Payment amount and currency come only from the confirmed booking.
- Modified client amounts cannot affect Stripe requests.
- Repeated intent requests and webhooks do not duplicate charges or payment records.
- Only verified Stripe events change paid state.

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
