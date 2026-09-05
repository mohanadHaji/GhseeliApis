# Ghseeli Frontend AI Integration Guide

## Purpose

This document is the primary context file to give an AI system that is
designing or implementing a Ghseeli frontend. It describes product behavior,
actors, API boundaries, application flows, frontend state, error handling, and
integration rules.

It is intentionally not a duplicate of every request and response schema.
The exact wire contract belongs to each API's OpenAPI document:

| API | Local OpenAPI | Deployed document when explicitly enabled |
|---|---|---|
| Customer | `https://localhost:62878/swagger/v1/swagger.json` | `http://ghseelicustomer.runasp.net/swagger/v1/swagger.json` |
| Business | `https://localhost:7167/swagger/v1/swagger.json` | `http://ghseelibusiness.runasp.net/swagger/v1/swagger.json` |

Production Swagger availability is configuration-dependent. A frontend build
must consume trusted versioned OpenAPI artifacts produced by CI or a controlled
development/test environment, not depend on a production Swagger URL remaining
public.

When this guide and OpenAPI differ on a field name, required property, enum,
format, nullability, route, or status code, **OpenAPI is authoritative**.
Regenerate the frontend API client instead of inventing a compatibility layer.

## Instructions for a frontend-generating AI

1. Build two clearly separated experiences:
   - customer/mobile application;
   - business-owner/staff portal.
2. Generate typed clients from frontend-filtered OpenAPI documents. Exclude
   `/api/v1/internal/*`, webhooks, and administrative operations not used by
   the target frontend.
3. Never call Business internal endpoints from a browser or mobile client.
4. Never calculate an authoritative price, duration, slot capacity, booking
   status, or payment status in the frontend.
5. Treat all identifiers as opaque strings. Never derive one ID from another.
6. Preserve `orderGuid`, draft `version`, catalog `version`, payment ID, and
   booking reference exactly as returned.
7. Show localized server messages, but branch application behavior on stable
   `code` and HTTP status rather than translated text.
8. Support right-to-left layout for Arabic and Hebrew.
9. Do not store JWTs or device tokens in logs, analytics events, URLs, error
   reports, screenshots, or source code.
10. Do not expose internal HMAC credentials, internal routes, Lahza secret
    keys, database IDs, or cross-API implementation details.
11. Production Lahza mutation routes are currently disabled. Hide payment
    actions in Production regardless of capability output until the backend
    release gate is explicitly enabled.
12. Do not infer unfinished features. Wallet, cash-on-arrival, and third-party
    payment methods are unavailable until the backend explicitly enables them.

## Product model

Ghseeli currently supports the `car_wash` vertical:

- a customer selects a business and branch;
- selects one or more compatible services and add-ons;
- supplies a vehicle and service location;
- selects an available appointment slot;
- receives an authoritative server price;
- authenticates and confirms the booking;
- optionally pays through Lahza hosted checkout when enabled;
- receives the initial confirmed booking snapshot. Business status callbacks
  update backend state, but a customer-facing booking read/list endpoint has
  not yet been implemented.

The database is prepared for future verticals, but the current frontend must
not display mechanics, laundry, dry cleaning, or other speculative workflows.

## Applications and trust boundaries

| Surface | API | Identity | Data authority |
|---|---|---|---|
| Customer app | Customer API | Device token, optionally Customer JWT | Customer account, draft, booking snapshot, payment |
| Business portal | Business API | Business JWT | Company, branch, catalog, availability, reservation, work order |
| Customer-to-Business integration | Server-to-server only | HMAC over HTTPS | Catalog validation, availability, reservation |
| Business-to-Customer integration | Server-to-server only | HMAC over HTTPS | Booking status callback and reconciliation |
| Lahza webhook | Customer API only | `X-Lahza-Signature` | Provider event notification |

The frontend must never connect directly to either database or call the other
application's internal API on behalf of a user.

## Base URLs and environments

Keep base URLs in environment configuration.

| Variable | Development default | Current deployed host |
|---|---|---|
| `CUSTOMER_API_BASE_URL` | `https://localhost:62878` | `http://ghseelicustomer.runasp.net` |
| `BUSINESS_API_BASE_URL` | `https://localhost:7167` | `http://ghseelibusiness.runasp.net` |

The current MonsterASP hosts do not have active public TLS. This is a known
deployment limitation, not permission to downgrade server-to-server security.
Do not ship a real customer application that transmits credentials over plain
HTTP.

Neither API currently enables CORS. Native mobile clients and server-side
clients can use the API base URLs directly. A browser frontend hosted on a
different origin must use a same-origin reverse proxy/backend-for-frontend.
Do not assume development or production cross-origin browser calls will work,
and do not recommend permissive wildcard CORS.

## Common HTTP rules

### Required headers

| Header | When to send |
|---|---|
| `Accept: application/json` | Normal API requests |
| `Content-Type: application/json` | JSON request bodies |
| `X-Device-Token` | Customer `/api/v1/*` routes unless OpenAPI marks the operation exempt |
| `Authorization: Bearer <customer-jwt>` | Customer account, booking confirmation, and payment operations |
| `Authorization: Bearer <business-jwt>` | Business portal operations |
| `Accept-Language: ar` or `he` | Preferred response language when no explicit `language` query is used |
| `X-Correlation-Id` | Optional safe client correlation token; persist it in diagnostics |
| `Idempotency-Key` | Operations documented as idempotent mutations |
| `X-Order-Guid` | Draft repricing and booking confirmation where documented |

Never send Customer and Business bearer tokens interchangeably.

### Language

Supported languages are:

| Code | UI direction |
|---|---|
| `ar` | RTL |
| `he` | RTL |

Rules:

- an explicit `language=ar|he` query takes precedence;
- otherwise use `Accept-Language`;
- unsupported or malformed headers fall back to Arabic;
- invalid explicit language query values return `400`;
- optional Hebrew content may fall back to Arabic;
- use the response `language` and `Content-Language` as the final selected
  language;
- language changes presentation only, never IDs, money, authorization, or
  state.

### Correlation IDs

Every response includes `X-Correlation-Id`.

- Store it with client-side error diagnostics.
- Display it in a support-friendly error details area.
- Do not place user content, email, phone, tokens, or line breaks in a supplied
  correlation ID.

### Error responses

Customer API modern flows and normalized HTTP policy errors generally use
`application/problem+json`.

```json
{
  "type": "https://api.ghseeli.example/errors/checkout_draft_expired",
  "title": "Localized title",
  "status": 410,
  "detail": "Localized safe explanation",
  "code": "checkout_draft_expired",
  "correlationId": "safe-correlation-id",
  "language": "ar",
  "fieldErrors": {
    "items[0].offeringSourceId": ["Localized validation message"]
  }
}
```

Some existing Business API operations still return operation-specific
`application/json` errors, including `{ "message": "..." }` and validation
objects containing `title`, `status`, and `errors`. The generated OpenAPI
operation is authoritative. The frontend error adapter must support both the
modern Problem Details family and each documented legacy Business response
shape.

Frontend behavior for Problem Details:

| Status | Default UI behavior |
|---:|---|
| `400` | Keep the form open; map `fieldErrors` to fields and show `detail` |
| `401` | Inspect the stable code: bearer failures require full login; `device_*` failures go to device recovery |
| `403` | Show access denied; do not treat it as missing data |
| `404` | Show not found or return to the owning list |
| `409` | Refresh current state/version and ask the user to review before retry |
| `410` | Draft expired; start a new checkout |
| `413` | Tell the user the submitted content is too large |
| `415` | Treat as a client bug or unsupported upload/content type |
| `429` | Respect `Retry-After`; disable repeated rapid submission |
| `503` | Show temporary unavailability and allow deliberate retry |

When `code` exists, branch on it rather than `title` or `detail`. When a
Business operation documents a legacy shape without `code`, map that operation
explicitly and preserve a generic recoverable fallback. Include the correlation
ID whenever the response provides one.

### Money, dates, and IDs

- Money is serialized as decimal values. Do not use binary floating-point for
  calculations.
- Display the server currency; do not assume ILS for every future response.
- Appointment instants such as `requestedSlotStartUtc` use ISO 8601 timestamps
  with `Z` or an explicit offset.
- Calendar dates use the OpenAPI `date` format, and recurring/override local
  clock values use the OpenAPI time/duration representation. Do not convert
  those branch-local scheduling fields into UTC before submission.
- Treat returned UTC timestamps as instants and convert only for display.
- Available-slot responses include both UTC and branch-local times.
- UUID/GUID values are opaque. Keep public Customer read-model IDs separate
  from Business source IDs.

## Authentication and session model

### Device identity

The customer app begins with device registration:

`POST /api/v1/devices/register`

Persist:

- `installationId`: stable per app installation;
- returned device `token`: secure platform storage only;
- token expiry.

Send the token as `X-Device-Token`. Re-registering an existing installation
rotates the token; replace the stored value immediately because the old token
stops working.

Recommended storage:

| Platform | Storage |
|---|---|
| iOS | Keychain |
| Android | Keystore-backed encrypted storage |
| Web | Prefer an HttpOnly same-site backend-for-frontend cookie; avoid localStorage when possible |

### Customer authentication

Customer auth is hosted by the Customer API:

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/Auth/register` | Approved customer registration route |
| POST | `/api/Auth/login` | Email/password login |
| GET | `/api/Auth/me` | Validate the current authenticated session and read identity |
| GET | `/api/Auth/external-login` | Existing OAuth initiation; not approved for new frontend use |
| GET | `/api/Auth/external-login-callback` | Existing controller callback; not approved for new frontend use |
| POST | `/api/Auth/link-external-login` | Existing link flow; not approved for new frontend use |
| GET | `/api/Auth/link-external-login-callback` | Existing link callback; not approved for new frontend use |
| DELETE | `/api/Auth/external-login/{provider}` | Existing unlink operation |
| GET | `/api/Auth/external-logins` | Existing linked-provider list |

Do not use `POST /api/Users` for customer signup. It is a legacy anonymous
creation surface; new frontend registration uses `POST /api/Auth/register`.
Do not use `/api/Auth/validate` for routine session management because it
resubmits the raw bearer token in a JSON body. Check local token expiry and use
authenticated `/api/Auth/me`.

Google and Facebook provider consoles must register these Customer API
callbacks:

- `/api/auth/google-callback`;
- `/api/auth/facebook-callback`.

Do not expose the current external-login or account-linking redirect flows in a
new frontend. Their return URLs are not yet allowlisted, and external login can
place the Customer bearer token in a redirect query string. Use email/password
authentication until the backend replaces this with an allowlisted,
one-time-code or secure-cookie flow. When OAuth is hardened, use a system
browser or secure web authentication session, never an embedded page that
captures provider credentials.

### Business authentication

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/v1/business/auth/register-owner` | Register business owner and initial business |
| POST | `/api/v1/business/auth/login` | Business login |

Business roles include `Owner`, `Employee`, and `Admin`. Render controls based
on known permissions for usability, but always expect the server to enforce
authorization and ownership.

### Session lifecycle

Neither API currently issues refresh tokens or exposes a server logout/revoke
operation.

| Event | Required client behavior |
|---|---|
| Login succeeds | Store the correct Customer or Business JWT and its expiry in secure storage |
| App resumes | Check expiry locally; optionally confirm with the authenticated identity/profile route |
| JWT expires | Clear only that session and require full login |
| One request returns `401` | Allow one coordinated reauthentication flow; do not start one flow per failed request |
| Logout | Delete local credentials and sensitive in-memory state |
| Customer/business account switch | Clear the old role-specific store before establishing the new session |
| Offline at expiry | Keep non-sensitive UI state, but do not queue authenticated mutations |

Do not silently replay a mutation after login unless the operation has a stable
idempotency identity and the user confirms the retry.

## Customer application information architecture

Recommended major areas:

| Area | Main responsibility |
|---|---|
| Bootstrap | Device registration, configuration, language, maintenance state |
| Authentication | Email/password login, registration, and profile; OAuth is deferred |
| Discovery | Categories, businesses, branches, offering details |
| Slot picker | Date, selected services/add-ons, branch-local availability |
| Checkout | Vehicle, location, services, add-ons, slot, quote |
| Bookings | Confirmation result and status |
| Payments | Hosted checkout launch, return, verification, payment status |
| Account | Profile, vehicles, addresses, linked login providers |

## Customer bootstrap scenario

1. Load or generate a stable `installationId`.
2. Call `POST /api/v1/devices/register`.
3. Store the returned device token securely.
4. Call `GET /api/v1/configuration` with `X-Device-Token`.
5. Apply:
   - selected language;
   - maintenance state and message;
   - support contact details;
   - legal links;
   - display name and customer-visible display content.
6. If an existing installation token is invalid, expired, inactive, or already
   rotated, stop retrying. The current backend requires the still-valid token
   to rotate an existing installation, so self-service recovery is not
   available. Show a recoverable support/app-reset state until a backend
   recovery flow is implemented.
7. If maintenance mode is enabled, block normal journeys but retain support
   and retry affordances.

Do not require customer login for browsing, draft creation, or repricing.

## Customer discovery scenario

### Endpoint sequence

| Step | Endpoint |
|---:|---|
| 1 | `GET /api/v1/catalog/categories` |
| 2 | `GET /api/v1/catalog/businesses` |
| 3 | `GET /api/v1/catalog/businesses/{id}` |
| 4 | `GET /api/v1/catalog/businesses/{id}/offerings` |
| 5 | `GET /api/v1/catalog/offerings/{id}` |

Rules:

- send the device token;
- honor localization;
- use returned Customer IDs for Customer routes;
- use `sourceId` only where the schema explicitly requests a source ID;
- respect branch/category filters;
- treat `catalog.isStale` as usable-but-stale data and show a subtle freshness
  indication when appropriate;
- a forced `refresh=true` may contact Business and can return `503`;
- never combine offerings from different businesses in one draft;
- use server ordering fields rather than alphabetically reordering everything.

### Add-on UI mapping

| `selectionType` | Suggested control | Required behavior |
|---|---|---|
| `SingleChoice` | Radio list | At most one choice |
| `SegmentedSingleButtonChoice` | Segmented control | At most one choice |
| `MultipleChoice` | Checkboxes | Respect minimum/maximum |
| `QuantityCounter` | Stepper/counter | Respect per-choice and aggregate quantities |
| `FixedIncludedChoice` | Read-only included item | Keep the server-required included default |

Frontend validation improves usability but does not replace server validation.

## Available-slot scenario

Call:

`POST /api/v1/catalog/businesses/{businessId}/branches/{branchId}/available-slots`

The request combines:

- local calendar date;
- optional expected catalog version;
- selected offerings;
- selected add-ons and quantities;
- optional customer coordinates;
- whether unavailable/full slots should be included;
- language.

Render:

- branch-local start/end time;
- availability;
- remaining capacity when product design needs it;
- total selected-service duration;
- timezone context.

Important rules:

- the result is advisory;
- selecting a slot does not reserve it;
- the final booking call performs the authoritative capacity check;
- refresh after `409`, stale catalog, unavailable slot, or meaningful delay;
- do not create slots client-side from business hours.

## Checkout draft scenario

### Create

`POST /api/v1/checkout/drafts`

The draft contains one:

- business;
- branch;
- appointment start;
- vehicle;
- service location;

It may contain multiple compatible offerings and selected add-ons.

Store the response:

| Value | Why it matters |
|---|---|
| `orderGuid` | Public checkout identifier and later header value |
| `version` | Optimistic concurrency token |
| `expiresAt` | Draft expiration deadline |
| `requiresReprice` | Whether confirmation must be blocked |
| `pricing` | Last authoritative price snapshot, when present |
| `paymentCapabilities` | Server decision about available payment methods |

### Read

`GET /api/v1/checkout/drafts/{orderGuid}`

Only the owning device can read the draft. Treat a wrong-device `404` exactly
like a missing draft; do not attempt to discover ownership.

### Update

`PUT /api/v1/checkout/drafts/{orderGuid}`

Send the last returned `expectedVersion`. On `409`:

1. fetch the draft again;
2. compare current server state with unsaved user changes;
3. ask the user to review or safely reapply changes;
4. never automatically overwrite a newer version.

Any meaningful intent change makes the draft require repricing again.

### Expiry

An expired draft returns `410 checkout_draft_expired`. Create a new draft and
let the user review the copied intent. Do not silently submit a replacement
booking.

## Pricing scenario

Two pricing modes exist:

| Mode | Endpoint | Persistence |
|---|---|---|
| Stateless quote | `POST /api/v1/pricing/reprice` | Does not save a draft |
| Draft reprice | `POST /api/v1/checkout/reprice` | Updates owned draft quote |

Draft repricing uses:

- `X-Order-Guid`;
- body `expectedVersion`;
- device token.

The server validates current catalog, selection rules, price, duration,
service area, and slot. The frontend must display the returned breakdown:

- base subtotal;
- add-on subtotal;
- item subtotal;
- service fee;
- taxable subtotal;
- tax;
- grand total;
- currency;
- total duration.

Never submit or reuse a client-computed total as authoritative. If the catalog
version changed, refresh catalog data and have the customer review the new
quote before confirmation.

Outside Production, payment buttons are driven by
`paymentCapabilities.methods[]`. The capability discriminator is
`CreditCard`; the payment-intent request method is `Card`. In Production, hide
payment actions while the Lahza endpoint release gate is disabled even if a
configured provider causes the current capability response to report
`CreditCard` as enabled.

## Booking confirmation scenario

Booking confirmation requires both the device token and Customer JWT.

`POST /api/v1/bookings/from-draft`

Send:

- `X-Order-Guid: <draft orderGuid>`;
- body `expectedVersion`;
- body `cancellationPolicyAcknowledged`.

Do not send a separate public `Idempotency-Key` for this route. The stable
`X-Order-Guid` is the booking confirmation idempotency identity.

Preconditions:

- the draft belongs to the current device;
- customer is authenticated;
- draft is not expired;
- quote is current;
- `requiresReprice` is false;
- cancellation policy is acknowledged;
- selected slot is still valid.

On success, persist the returned public booking identifiers and snapshot.

### Booking states

```text
Pending -> Confirmed -> InProgress -> Completed
    |          |             |
    +----------+-------------+----> Cancelled
               |
               +------------------> NoShow
```

`Completed`, `Cancelled`, and `NoShow` are terminal. The Business API is
authoritative for operational status. The Customer API receives signed
callbacks, but there is currently no customer-authenticated booking read/list
route. The frontend can show the status returned by confirmation, but must not
poll `/api/v1/internal/bookings/*`; those routes require server HMAC. Live
customer status tracking is deferred until a public owned booking read API is
implemented.

### Retry behavior

- Use the same logical `orderGuid` when retrying an ambiguous confirmation.
- Do not create a second draft merely because the first HTTP response was lost.
- A conflict requires state refresh, not blind retry.
- Network cancellation does not prove that the server cancelled processing.

## Payment scenario

Payment initialization, verification, and webhook routes are currently hidden
in Production. The authenticated `GET /api/v1/payments/{id}` status route
remains available, but the frontend must hide actions that start or verify a
Lahza payment in Production regardless of the current capability response.
In an enabled environment, require a capability with
`method == "CreditCard"` and `enabled == true`; initialize the payment using
the request method value `Card`.

When enabled:

1. Confirm the booking.
2. Call `POST /api/v1/payments/intents`.
3. Send the booking ID and method from the OpenAPI schema.
4. Send a stable `Idempotency-Key` for this payment attempt.
5. Receive the server-owned amount, currency, payment ID, provider reference,
   and hosted `checkoutUrl`.
6. Open `checkoutUrl` in a secure external browser/custom tab.
7. Treat navigation to the callback URL only as a signal to return to the app.
8. Call `POST /api/v1/payments/{id}/verify`.
9. Read `GET /api/v1/payments/{id}` until the backend reports a terminal state
   or the UI retry policy ends.

Never mark a booking paid because:

- the browser reached a callback page;
- the user says payment succeeded;
- a client-side redirect contains success text;
- the hosted page closed.

Only backend verification and signed webhook processing can establish payment
state.

Suggested UI mapping:

| Payment state | UI |
|---|---|
| Pending/processing | Progress state with controlled verify/retry |
| Completed/success | Receipt/success state |
| Failed | Failure explanation and safe retry action |
| Refunded | Refunded status |
| Provider unavailable | Disable card and show localized capability reason |

Do not expose provider internals or raw webhook data.

## Customer account scenarios

### Vehicles

| Method | Route |
|---|---|
| GET | `/api/Vehicles/my-vehicles` |
| GET | `/api/Vehicles/{id}` |
| POST | `/api/Vehicles` |
| PUT | `/api/Vehicles/{id}` |
| DELETE | `/api/Vehicles/{id}` |

### Addresses

| Method | Route |
|---|---|
| GET | `/api/Addresses/my-addresses` |
| GET | `/api/Addresses/{id}` |
| POST | `/api/Addresses` |
| PUT | `/api/Addresses/{id}` |
| DELETE | `/api/Addresses/{id}` |
| PUT | `/api/Addresses/{id}/set-primary` |

### User self-service

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/Users/me` | Current profile |
| PUT | `/api/Users/me` | Update allowed self-service fields |
| DELETE | `/api/Users/me` | Deactivate account |
| PUT | `/api/Users/me/reactivate` | Reactivate account |
| PUT | `/api/Users/me/password` | Change password |
| POST | `/api/Users/me/email/request-confirmation` | Request email confirmation |
| POST | `/api/Users/me/email/confirm` | Confirm email |

The server owns role and active-state security. Do not send privileged fields
from self-service screens.

## Business portal information architecture

Recommended areas:

| Area | Purpose |
|---|---|
| Authentication | Owner registration and business login |
| Company | Public profile and branch management |
| Catalog | Categories, offerings, add-on groups, choices |
| Availability | Settings, recurring schedules, date overrides, service area |
| Work orders | Transition a known work order only; list/detail APIs are not implemented |
| Administration | No general UI yet; requeue accepts only a previously known dead-letter event ID |

## Business setup scenario

1. Register an owner with `/api/v1/business/auth/register-owner`.
2. Store the Business JWT separately from any Customer session.
3. Read `/api/v1/business/company`.
4. Update the company profile.
5. Create and update branches.
6. For each branch, configure availability settings and service area.
7. Create recurring schedules.
8. Add date overrides for closures or special capacity.
9. Create categories.
10. Create offerings under categories.
11. Add groups and choices with valid selection rules.
12. Activate only complete records. There is no separate catalog publish
    endpoint; active-state mutations update the authoritative catalog version.

Arabic business/catalog names and branch addresses are required. Hebrew values
are optional and normalized to `null` when blank.

## Business catalog endpoints

Base route: `/api/v1/business/catalog`

| Resource | Supported operations |
|---|---|
| Categories | list, get, create, update, delete |
| Offerings | list, get, create, update, delete |
| Add-on groups | list/get under offering, create, update, delete |
| Add-on choices | list/get under group, create, update, delete |

Use OpenAPI for exact request fields and selection-rule bounds. After a
mutation, refresh the affected resource and any customer-preview data because
catalog versioning can invalidate customer quotes.

## Business availability endpoints

Base route: `/api/v1/business/availability`

| Resource | Routes |
|---|---|
| Settings | `GET/PUT branches/{branchId}/settings` |
| Recurring schedules | list/create by branch; get/update/delete by schedule ID |
| Date overrides | list/create by branch; get/update/delete by override ID |
| Service area | `GET/PUT/DELETE branches/{branchId}/service-area` |

The UI should make branch timezone and overnight schedules explicit. A local
end time earlier than the start time represents an overnight window.

## Work-order scenario

Business staff can transition a known work order with:

`POST /api/v1/business/work-orders/{id}/transitions`

Send the documented `Idempotency-Key` and use the returned status/sequence. Do
not let the UI skip states that the server does not allow.

There is currently no Business work-order list or detail endpoint. Do not build
an operational queue or attempt to discover work-order IDs from internal
routes. A complete work-order portal is deferred until authorized read APIs
exist.

Allowed lifecycle:

```text
Pending -> Confirmed -> InProgress -> Completed
Pending/Confirmed/InProgress -> Cancelled
Confirmed/InProgress -> NoShow
```

Capacity is consumed by `Pending`, `Confirmed`, and `InProgress`; terminal
states release it.

## Target frontend architecture

Use separate deployable applications and generated clients:

| Frontend | Recommended architecture | API access |
|---|---|---|
| Customer mobile | Native or cross-platform mobile application | Direct Customer API calls over trusted HTTPS |
| Customer web, if added | Same-origin backend-for-frontend | Browser calls BFF; BFF calls Customer API |
| Business portal | Same-origin backend-for-frontend | Browser calls BFF; BFF calls Business API |

The BFF owns browser cookies, CSRF protection, server-side token storage, and
API proxying. Do not place API bearer tokens in browser local storage. If the
product later chooses direct browser API calls, the backend must first add
tightly allowlisted CORS and document credential, preflight, and CSRF behavior.

## Screen, navigation, and state contract

### Customer application

| Screen/state | Entry guard | Main operations | Success destination | Required alternate states |
|---|---|---|---|---|
| Bootstrap | None | Device registration, configuration | Discovery or maintenance | Offline, device recovery unavailable, configuration unavailable |
| Maintenance | Configuration says maintenance | Configuration retry | Bootstrap/discovery | Support and legal links remain available |
| Login/register | Valid device | Customer auth | Stored intended destination | Validation, expired session, OAuth unavailable |
| Discovery | Valid device | Categories/businesses | Business or offering detail | Empty, stale, refresh unavailable |
| Offering detail | Valid device | Offering/add-on detail | Slot picker or draft | Inactive/missing offering, invalid defaults |
| Slot picker | Valid device | Available-slot search | Checkout intent | No slots, stale catalog, location outside area |
| Draft checkout | Owning device | Create/read/update/reprice | Login or confirmation | Conflict, expired, repricing required |
| Confirmation result | Device + Customer JWT | Confirm booking | Booking receipt | Ambiguous network result, unavailable slot, stale quote |
| Payment return | Enabled environment + payment ID | Verify/read payment | Payment result | Pending, abandoned, failed, provider unavailable |
| Account | Customer JWT | Profile, vehicles, addresses | Previous account route | Unauthorized, ownership-masked not found |

Navigation rules:

- Preserve the intended confirmation destination when login is required.
- Do not preserve or replay a password, token, or payment URL in navigation
  parameters.
- Prompt before abandoning a dirty draft form.
- Back navigation from hosted payment returns to payment verification, not
  directly to a success screen.
- Restore a draft only from securely persisted `orderGuid` plus the owning
  device token; always fetch current server state.
- A terminal confirmation or payment result replaces the mutation screen in
  navigation history to prevent accidental duplicate submission.

### Business portal

| Route area | Allowed roles | Main operations | Current limitation |
|---|---|---|---|
| Authentication | Anonymous | Owner registration, login | Employee creation/invitation is unavailable |
| Company read | Owner, Employee, Admin | Read assigned company/branches | Assignment remains server enforced |
| Company/branch edit | Owner, Admin | Update company, create/update branch | Branch deletion is unavailable |
| Catalog | Owner, Admin | Category/offering/group/choice CRUD | No separate publish command |
| Availability | Owner, Admin | Settings, schedules, overrides, service area | Use branch-local time |
| Known work-order transition | Owner, Employee, Admin | Transition known work-order ID | No list/detail API |
| Known outbox requeue | Admin | Requeue known dead-letter event ID | No dead-letter browser |

Hide unavailable navigation for usability, but treat `403` as authoritative.

## Identifier provenance

Customer catalog responses can include both Customer read-model IDs and
Business source IDs.

| Identifier | Use |
|---|---|
| `id` on Customer catalog resources | Customer catalog URLs and operations that explicitly request Customer IDs |
| `sourceId` | Checkout intent fields and operations whose OpenAPI schema explicitly says `*SourceId` |
| `orderGuid` | Draft identity, draft reprice header, and booking confirmation identity |
| Booking `reference` | Public cross-system booking reference |
| Database entity IDs not returned by public DTOs | Never infer or request |

Example:

```text
CatalogOfferingResponse.id       -> available-slot item offeringId
CatalogOfferingResponse.sourceId -> checkout item offeringSourceId
```

Always confirm the exact property through the generated operation type.

## Collection, ordering, and pagination

Current public list endpoints are unpaginated. The frontend must:

- preserve server ordering and `displayOrder`;
- render an explicit empty state for an empty successful list;
- avoid assuming all lists remain permanently small;
- avoid infinite-scroll abstractions that imply a nonexistent continuation
  token;
- perform client-side search/filtering only for presentation and never use it
  as authorization or authoritative availability filtering.

When pagination is introduced, it must be a versioned backend contract with a
cursor or page model in OpenAPI. Do not invent `page`, `limit`, or `offset`
query parameters.

## Cache and persistence contract

API responses use `Cache-Control: no-store`. Browser HTTP caches, service
workers, and shared proxies must not persist API responses.

| Data | Allowed retention |
|---|---|
| Device token/JWT | Secure credential storage only |
| Configuration | In-memory for the current app session; refresh on bootstrap |
| Catalog | In-memory application state; optional encrypted mobile snapshot only after product approval |
| Draft identity | Secure local restoration state containing only `orderGuid`, version hint, and non-sensitive UI progress |
| Full draft/quote | Fetch from server; do not treat a persisted copy as current |
| Booking/payment snapshot | In-memory or encrypted local receipt state; server remains authoritative |
| Business forms | In-memory draft only unless an explicit secure draft feature is designed |

Respect `freshUntilUtc`, `maxStaleUntilUtc`, and `isStale` from catalog
metadata. Clear user-specific state on logout/account switch. Clear catalog
and draft state when the device identity changes.

## Form and validation standard

1. Generate basic required, enum, format, length, and range constraints from
   OpenAPI.
2. Validate on submit and optionally on blur after the user has interacted
   with a field; do not show every error on first render.
3. Preserve entered values after `400`.
4. Map modern `fieldErrors` paths, including indexed paths such as
   `items[0].selections[1].quantity`, to the matching control.
5. Put cross-field or unknown paths in a form-level error summary.
6. Focus the first invalid field and announce the error summary to assistive
   technology.
7. Do not trim or transform values beyond documented normalization before the
   user sees what will be sent.
8. Format decimals for display using locale-aware formatting, but parse and
   submit values according to generated numeric types.
9. Confirm destructive actions such as delete, deactivate, or removal.
10. After `409`, compare server state and local edits; never silently overwrite.

Business Arabic names and branch addresses are required. Optional Hebrew
values should submit `null` when blank, following the generated schema and
backend normalization.

## Error adapter contract

Normalize responses into a frontend error model without discarding the raw
HTTP status:

```ts
type FrontendApiError = {
  status: number;
  code?: string;
  message: string;
  correlationId?: string;
  fieldErrors: Record<string, string[]>;
  retryAfterSeconds?: number;
  source: "problem-details" | "business-validation" | "legacy-message" | "network";
};
```

Adapter precedence:

1. Modern Problem Details: use `code`, `detail`, `fieldErrors`, and
   `correlationId`.
2. Business validation object: map `errors` to `fieldErrors`.
3. Legacy `{ message }`: use `message` with no code.
4. Unknown JSON/text: use a safe generic localized client message.
5. Network failure: distinguish offline, timeout, cancellation, and TLS
   failures without claiming the server did not process a mutation.

Never display raw HTML, stack traces, SQL, provider responses, or untrusted
response text as markup.

## Retry and idempotency standard

| Operation | Automatic retry | Stable identity |
|---|---:|---|
| Safe read | At most two transient retries with jitter | Correlation ID may remain stable |
| Forced catalog refresh | User-initiated retry preferred | None |
| Draft update | No after conflict | Draft `orderGuid` + expected version |
| Draft reprice | Only after an unambiguous transport failure | `X-Order-Guid` + expected version |
| Booking confirmation | Controlled retry after ambiguity | `X-Order-Guid` |
| Payment initialization | Controlled retry | Persisted `Idempotency-Key` |
| Payment verification/read | Bounded polling while pending | Payment ID |
| Business mutation documenting idempotency | Controlled retry | Persisted `Idempotency-Key` |

Honor `Retry-After` on `429`. Stop retrying after authentication, validation,
authorization, ownership, state, or version failures. Do not maintain a
general offline mutation queue.

## Accessibility and RTL standard

- Target WCAG 2.2 AA for web and equivalent platform accessibility guidance
  for mobile.
- Use logical CSS/layout properties (`margin-inline-start`, `padding-inline`)
  instead of hardcoded left/right positioning.
- Mirror directional navigation icons where semantics require it; do not mirror
  universal media/payment symbols.
- Keep email, URLs, phone numbers, license plates, UUIDs, money codes, and
  technical references readable with explicit bidirectional isolation.
- Preserve logical focus order in RTL.
- Announce loading completion, errors, validation summaries, and payment state
  changes.
- Provide at least 44x44 CSS-pixel/equivalent touch targets.
- Do not communicate availability, status, or validation using color alone.
- Test Arabic and Hebrew text expansion, mixed-direction strings, dynamic type,
  screen readers, keyboard navigation, and reduced motion.

## Payment return and deep-link contract

`Lahza:CallbackUrl` must point to a controlled frontend universal link, app
link, or same-origin web route. It is not the webhook.

The callback handler:

1. accepts only the exact allowlisted callback route;
2. restores the pending payment ID from secure local state, not arbitrary query
   parameters;
3. ignores success/failure wording in the callback URL;
4. calls backend verification;
5. supports a cold app start;
6. bounds polling and offers a manual retry while pending;
7. removes the pending callback state after a terminal outcome.

Do not include JWTs, device tokens, provider secrets, or authoritative payment
state in deep-link parameters.

## Environment and feature availability

| Capability | Local development | Current deployed environment | Frontend rule |
|---|---|---|---|
| Customer/Business APIs | HTTPS launch profiles | HTTP health/Swagger host; TLS deferred | Never send real credentials over deployed HTTP |
| Browser direct API access | No CORS | No CORS | Use same-origin BFF |
| Customer email/password auth | Available when configured DB/JWT are valid | Available | Approved current auth |
| OAuth | Configurable but unsafe redirect contract | Disabled/unapproved | Do not expose |
| Catalog/checkout/booking | Available with both APIs and HMAC config | Cross-API HTTPS blocked until TLS | Show unavailable state when dependencies fail |
| Lahza payment routes | Enabled by default outside Production when configured | Explicitly hidden | Feature must remain hidden |
| Wallet/cash/third party | Disabled | Disabled | Do not render enabled actions |

Feature availability must come from environment configuration plus server
capabilities. Do not enable a hidden Production feature solely from a build
constant.

## Analytics and privacy

Allowed analytics should describe UI behavior without payload content:

- screen viewed;
- action started/completed/failed;
- stable error code;
- HTTP status category;
- duration bucket;
- language;
- feature availability state.

Never record names, email, phone, address, coordinates, license plate, free
text, tokens, JWT claims, URLs containing secrets, provider references,
checkout URLs, request/response bodies, or raw correlation IDs. If support
needs correlation IDs, keep them in a separate access-controlled diagnostic
channel with a short retention period.

## Frontend testing and mocking

| Level | Required coverage |
|---|---|
| Generated-client contract | Client generation succeeds from filtered OpenAPI; no internal/webhook operations are emitted |
| Unit | Error adapter, header injection, token expiry, ID mapping, money/date formatting, retry/idempotency helpers |
| Component | Arabic/Hebrew forms, add-on controls, validation paths, stale/conflict/expired states |
| Integration with mocks | Generated-schema happy paths plus `400`, `401`, `403`, `404`, `409`, `410`, `429`, and `503` |
| Accessibility | Automated checks plus keyboard/screen-reader/manual RTL checks |
| End-to-end | Bootstrap, browse, slots, draft, reprice, login, confirmation, and enabled-environment payment return |

Use generated schema fixtures or builders. Manually author stateful mock
transitions such as version conflicts and pending-to-terminal payment changes.
Use a deterministic clock for expiry, retry, and slot tests. Never copy
production data or credentials into fixtures.

## High-value implementation examples

### Draft conflict recovery

```text
GET draft -> version 4
User edits locally
PUT expectedVersion 4 -> 409
GET draft -> version 5
Show server changes beside unsaved local changes
User confirms reapply
PUT expectedVersion 5
```

### Stable payment idempotency

```text
Generate key once when the user starts one payment attempt
Persist key with booking ID until initialization resolves
Timeout -> retry same body with same key
Terminal response -> clear pending key
New deliberate payment attempt -> generate a new key
```

### Role-driven navigation

```text
Owner: company edit + catalog + availability + known work-order transition
Employee: company read + known work-order transition
Admin: Owner capabilities + known outbox requeue
```

## Loading, retry, and offline behavior

| Operation type | Frontend retry guidance |
|---|---|
| Safe GET | Retry transient network/`503` failures with bounded backoff |
| Catalog refresh | Retry deliberately; preserve usable stale data when server returns it |
| Draft update | Never blindly retry after `409`; reload version |
| Booking confirmation | Retry only with the same logical order identity |
| Payment initialization | Retry with the same idempotency key |
| Payment verification | Safe controlled retry while pending |
| Business mutation | Reuse documented idempotency key when outcome is ambiguous |

Prevent duplicate taps while a mutation is in flight. A timeout is an
ambiguous outcome, not proof of failure.

## Frontend state model

Keep these stores separate:

| Store | Important fields |
|---|---|
| Device | installation ID, device token, expiry |
| Customer session | Customer JWT, customer summary |
| Business session | Business JWT, role, company/branch assignment |
| Configuration | language, maintenance, support, legal links |
| Catalog | data, catalog version, freshness metadata |
| Draft | order GUID, version, expiry, requires-reprice, intent, quote |
| Booking | public reference, status, immutable snapshot |
| Payment | payment ID, state, provider reference, checkout URL |

Do not put Customer and Business sessions in one interchangeable auth store.

## Security and privacy requirements

- Redact JWTs, device tokens, OAuth codes, HMAC values, Lahza references,
  signatures, and secrets.
- Avoid logging emails, phone numbers, addresses, license plates, localized
  descriptions, or full request/response bodies.
- Do not render server `detail` as HTML.
- Do not trust hidden/disabled controls as authorization.
- Do not accept arbitrary redirect URLs from query parameters.
- Use secure browser APIs for OAuth and hosted payment.
- Clear credentials on explicit logout. Account deactivation is a special
  current limitation: reactivation requires the still-valid Customer JWT, and
  the default JWT lifetime is much shorter than the scheduled deletion grace
  period. If the product offers immediate undo, retain the token only in secure
  storage for that short session; otherwise treat later self-service
  reactivation as unavailable until the backend adds a dedicated recovery
  mechanism.
- Keep correlation ID and stable code in sanitized diagnostics.

## OpenAPI client generation

Recommended workflow:

1. Run both APIs locally.
2. Download both `/swagger/v1/swagger.json` documents as source artifacts.
3. Produce frontend-specific filtered documents:
   - Customer app: Customer auth/profile, device, configuration, catalog,
     checkout, booking confirmation, payment when enabled, vehicles, and
     addresses;
   - Business portal: Business auth, company, catalog, availability, and only
     explicitly approved operational mutations.
4. Exclude:
   - every `/api/v1/internal/*` operation;
   - `/api/lahza/webhook`;
   - admin-only operations from non-admin clients;
   - legacy/unapproved Customer registration and OAuth operations;
   - gated payment operations from Production-generated clients while hidden.
5. Generate separate clients/namespaces, for example:
   - `generated/customer-api`;
   - `generated/business-api`.
6. Fail CI when generated output differs from committed output, if generated
   clients are committed.
7. Wrap generated clients only for:
   - base URL selection;
   - credential/header injection;
   - language;
   - correlation IDs;
   - Problem Details conversion;
   - safe retry policy.
8. Do not manually edit generated files.

Pin the generator and runtime package versions. Treat a filtered-spec diff as
an API review artifact. Removing an operation or making a field required is a
breaking frontend change even when the backend can still compile.

The AI should inspect OpenAPI before creating:

- TypeScript types;
- form validation;
- enum controls;
- route builders;
- request examples;
- API mocks;
- status-specific UI behavior.

## Current backend dependencies and deferred UI

Do not invent screens for these missing contracts:

| Frontend need | Current backend status | Required backend addition |
|---|---|---|
| Customer booking history/detail/live status | Confirmation only | Owned Customer booking list/detail APIs |
| Customer cancellation/reschedule | Not exposed | State-aware owned mutation APIs |
| Lost/expired device credential recovery | Not exposed | Secure recovery/reset contract |
| JWT refresh/server logout | Not exposed | Refresh/revocation/session contract |
| Forgotten password | Not exposed | Unauthenticated recovery flow |
| OAuth login/linking | Existing redirect flow unapproved | Allowlisted one-time-code or secure-cookie flow |
| Reactivation after JWT expiry | Not exposed | Dedicated reactivation proof/recovery flow |
| Business work-order queue/detail | Transition by known ID only | Assigned list/detail APIs |
| Employee management | Roles exist, management API absent | Invitation/assignment/role APIs |
| Dead-letter browser | Requeue by known event ID only | Admin list/detail APIs |
| Business dashboard/reporting/search | Not exposed | Versioned reporting/query contracts |
| Branch deletion | Not exposed | Ownership-safe delete/archive operation |

Each row is a backend dependency, not a frontend estimation task.

## Minimum frontend acceptance scenarios

### Customer

| ID | Scenario |
|---|---|
| `FE-CUST-001` | First launch registers device and loads Arabic configuration |
| `FE-CUST-002` | Hebrew selection produces RTL Hebrew/fallback content |
| `FE-CUST-003` | Invalid/expired/rotated existing device token stops retrying and shows the documented recovery limitation |
| `FE-CUST-004` | Customer browses business, branch, offering, and add-ons |
| `FE-CUST-005` | Slot picker displays branch-local capacity-aware slots |
| `FE-CUST-006` | Anonymous user creates, reads, and updates own draft |
| `FE-CUST-007` | Draft version conflict reloads without overwriting changes |
| `FE-CUST-008` | Expired draft starts a reviewed replacement checkout |
| `FE-CUST-009` | Reprice displays server totals and capability reasons |
| `FE-CUST-010` | Login is requested only when confirming booking |
| `FE-CUST-011` | Booking confirmation handles ambiguous network outcome safely |
| `FE-CUST-012` | Confirmation status is shown without calling internal booking routes; live customer status tracking remains unavailable |
| `FE-CUST-013` | Payment button is absent while Production Lahza gate is disabled |
| `FE-CUST-014` | Enabled hosted payment verifies on backend after callback |
| `FE-CUST-015` | Problem Details maps field errors and exposes correlation ID |
| `FE-CUST-016` | Vehicle/address ownership failures do not leak other users' data |

### Business

| ID | Scenario |
|---|---|
| `FE-BIZ-001` | Owner registers and signs into the Business API only |
| `FE-BIZ-002` | Owner edits only the assigned company and branches |
| `FE-BIZ-003` | Arabic required and optional Hebrew fields validate correctly |
| `FE-BIZ-004` | Catalog editor enforces add-on selection constraints |
| `FE-BIZ-005` | Availability editor handles overnight schedules and closures |
| `FE-BIZ-006` | Service-area editor displays coordinates/radius safely |
| `FE-BIZ-007` | A transition for a known work-order ID permits only server-allowed actions; queue/list UI remains deferred |
| `FE-BIZ-008` | Wrong company/branch access returns a non-destructive denial |
| `FE-BIZ-009` | Mutation timeout reuses the same idempotency identity |
| `FE-BIZ-010` | No outbox browser is exposed; requeue tooling accepts only a known event ID and is restricted to Admin |

## Definition of frontend completion

A frontend feature is complete only when:

- its types come from current OpenAPI;
- required credentials and headers are attached correctly;
- Arabic and Hebrew layouts work;
- loading, empty, stale, conflict, expired, unauthorized, forbidden,
  unavailable, and unexpected states are designed;
- server money and state remain authoritative;
- mutation double-submit is prevented;
- tokens and PII are absent from logs;
- the relevant minimum acceptance scenarios pass;
- no hidden Production feature is accidentally exposed.

## Related backend documents

| Document | Use |
|---|---|
| [`../API_BOUNDARIES.md`](../API_BOUNDARIES.md) | Deep ownership, HMAC, state, idempotency, and route rules |
| [`../HTTP_TEST_PLAN_STANDARD.md`](../HTTP_TEST_PLAN_STANDARD.md) | Backend HTTP testing requirements |
| [`../STEP_18_LAHZA_PAYMENT_MIGRATION_HTTP_TEST_RESULTS.md`](../STEP_18_LAHZA_PAYMENT_MIGRATION_HTTP_TEST_RESULTS.md) | Lahza deterministic and external-provider evidence |
| [`../STEP_20_AVAILABLE_SLOTS_HTTP_TEST_RESULTS.md`](../STEP_20_AVAILABLE_SLOTS_HTTP_TEST_RESULTS.md) | Available-slot implementation evidence |
| [`../STEP_21_LAHZA_ENDPOINT_GATING_HTTP_TEST_PLAN.md`](../STEP_21_LAHZA_ENDPOINT_GATING_HTTP_TEST_PLAN.md) | Current Production payment-route gate |
