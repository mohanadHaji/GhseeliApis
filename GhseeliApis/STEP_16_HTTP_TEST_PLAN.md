# Step 16 HTTP Test Plan — Clean Schema and Responsibility Separation

Status: **frozen before Step 16 production changes**

HTTP required: **Yes** — Step 16 changes database creation, startup, role
seeding, controller discovery, authorization, Swagger, host isolation, and the
observable fate of obsolete routes on two independently deployed APIs.

This plan follows `HTTP_TEST_PLAN_STANDARD.md`, `API_BOUNDARIES.md`, and the
Step 16 roadmap. IDs are permanent and may only be appended. Nothing authorizes
production execution or destructive work against a shared database.

## 1. Frozen outcome

### 1.1 Ownership

- `GhseeliApis` is the Customer host and uses only its Customer connection.
- `Ghseeli.BusinessApi` is the Business host and uses only its Business
  connection.
- Neither implementation project references the other. The only shared
  application references allowed are `Ghseeli.Common` and neutral versioned
  DTOs/enums/error codes in `Ghseeli.IntegrationContracts`.
- No DbContext, EF entity, migration, Identity type, repository, handler,
  authentication implementation, domain rule, configuration, or secret is
  shared.
- Cross-host business calls are HTTPS plus the complete HMAC quartet only.
  Bearer/device credentials never become integration credentials.
- Database names, SQL users, schemas, IDs, transactions, migration history,
  and connection pools are independent. No query, foreign key, synonym,
  linked server, cross-database transaction, or shared SQL login grants one API
  access to the other's database.

### 1.2 Clean database contract

The migration command is a deployment operation. Against a fresh empty SQL
Server database, each owning migration set creates exactly its inventory below,
including its own `__EFMigrationsHistory`. Applying the same set again is a
no-op. Dropping and recreating either disposable database produces the same
schema without requiring the other database or host.

**Customer-owned application tables**

`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`,
`AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `UserAddresses`,
`Vehicles`, `CustomerDevices`, `CustomerConfigurations`, `CatalogProviders`,
`CatalogBranches`, `CatalogCategories`, `CatalogOfferings`,
`CatalogAddonGroups`, `CatalogAddonChoices`, `CheckoutDrafts`,
`CheckoutDraftItems`, `CheckoutDraftSelections`,
`CheckoutDraftPricingSnapshots`, `CheckoutDraftPricingItemSnapshots`,
`CheckoutDraftPricingSelectionSnapshots`, `CustomerBookings`,
`CustomerBookingItems`, `CustomerBookingSelections`,
`BookingConfirmationAttempts`, `CustomerPayments`,
`CustomerPaymentIdempotencyRecords`, `StripeWebhookEvents`,
`CustomerInternalServiceNonces`, `CustomerInternalIdempotencyRecords`,
and `ProcessedBookingStatusMessages`.

Customer schema must not contain the obsolete/mixed tables `Companies`,
`CompanyAvailabilities`, `Services`, `ServiceOptions`, `Bookings`, `Payments`,
`Wallets`, `WalletTransactions`, or `Notifications`; nor any Business table
from the inventory below.

**Business-owned application tables**

`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`,
`AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `Companies`,
`Branches`, `ServiceCategories`, `ServiceOfferings`, `AddonGroups`,
`AddonChoices`, `BranchAvailabilitySettings`, `BranchRecurringSchedules`,
`BranchAvailabilityOverrides`, `BranchServiceAreas`,
`BusinessUserAssignments`, `InternalServiceNonces`,
`InternalServiceIdempotencyRecords`, `AppointmentReservations`, `WorkOrders`,
`WorkOrderItems`, `WorkOrderSelections`, `BookingStatusOutboxMessages`, and
`BookingStatusRequeueHistory`.

Business schema must not contain `UserAddresses`, `Vehicles`,
`CustomerDevices`, `CustomerConfigurations`, any `Catalog*` read-model table,
any `CheckoutDraft*` table, `CustomerBookings`, `CustomerBookingItems`,
`CustomerBookingSelections`, `BookingConfirmationAttempts`,
`CustomerPayments`, `CustomerPaymentIdempotencyRecords`,
`StripeWebhookEvents`, `CustomerInternalServiceNonces`,
`CustomerInternalIdempotencyRecords`, `ProcessedBookingStatusMessages`,
`Notifications`, `Bookings`, `Payments`, `Wallets`, or
`WalletTransactions`.

Both schemas must have their documented keys, unique/index constraints,
relationships, delete behavior, rowversions, decimal precision, max lengths,
and SQL defaults. There are no unowned orphan tables or foreign keys to a table
outside the same database.

### 1.3 Startup, roles, seeds, and health

- With a current reachable owned database and valid owned configuration, each
  host starts independently and becomes ready without the other host.
- Customer owns only `User` and `Admin` roles. `Company` is not created,
  accepted during registration, or usable by authorization.
- Business owns exactly `Owner`, `Employee`, and `Admin`. Owner registration
  safely creates missing Business roles and its company/assignment transaction;
  it never creates Customer roles.
- Repeated role initialization is idempotent. Concurrent first startup or owner
  registration creates one row per normalized role and never emits a 500 due
  solely to a role race.
- Clean schema creation does not seed fake users, companies, catalog,
  availability, configuration, bookings, payments, devices, webhooks, HMAC
  nonces/idempotency, or provider URLs. A test fixture may add disposable data
  only after this zero-domain-row assertion.
- `/api/Health` and `/api/Health/db` remain the Customer health routes;
  `/api/health` remains Business health. Liveness says the process is alive;
  database health verifies only the owning database. A dependency-host outage
  cannot make owned DB health query the foreign database.
- Missing connection configuration, unreachable SQL, login denial, wrong
  database, pending/missing owned tables, or incompatible schema never falls
  back to the other connection and never auto-creates a partial schema.
  Startup/readiness or the first DB operation fails closed as specified below,
  with safe logs/problems and no connection string, server, database, SQL,
  migration, stack, or credential disclosure.
- Swagger is independently generated in Development/explicit non-production
  mode and disabled in Production/default mode. It reflects the exact retained
  route inventories and excludes every removed/foreign route.

### 1.4 Inherited Step 15 contract

All Step 15 language, exact Problem Details, correlation, cache, content type,
security headers, `Vary`, HSTS, CSP, method/unknown-route behavior, redaction,
body/header bounds, and Swagger rules remain unchanged. In particular:

- user-facing Customer/Business problems use Arabic default or selected Hebrew;
- internal HMAC and Stripe problems remain English and language-neutral;
- unknown/removed paths return safe correlated `404 resource_not_found`;
- a wrong verb on a retained path returns safe correlated
  `405 method_not_allowed` with exact `Allow`;
- a removed mutation never reaches authorization/domain handlers and has no
  domain, Identity, nonce, idempotency, outbox, webhook, or provider side
  effect;
- no response or log reveals whether a removed table/resource used to exist.

The explicit Step 16 decisions are:
`legacyBoundaryDecision=remove`, `walletDecision=remove`, and
`notificationDecision=remove`. They supersede Step 15 retention for **all**
legacy Companies, Services, ServiceOptions, and Bookings operations. Removal is
not an inference from a deleted table or controller: every exact operation is
named in §3.1 and must replace its corresponding Step 15 retention assertion
with a route-level 404 assertion. A removed operation must be absent from
endpoint metadata and Swagger, return 404 before
authentication/authorization (therefore never 401/403 for any credential),
invoke no handler/repository/DbContext or outbound client, and have zero
domain, Identity, nonce, idempotency, outbox, webhook, payment/provider,
row/version, or other persistence side effects.

### 1.5 Step 13–15 regression guarantees

- Step 13 callback, reconciliation, Business transition, durable outbox,
  retry/dead-letter/requeue, sequence, idempotency, and HMAC guarantees remain
  unchanged on their retained routes.
- Step 14 modern payment intent/read and Stripe webhook routes, immutable
  server money, ownership, provider idempotency, replay/order handling,
  signature-only webhook authentication, and legacy Payments non-controller
  disposition remain unchanged.
- Step 15 exact language/problem/header/transport/Swagger behavior remains
  unchanged. Its legacy retention scenarios are superseded for every
  Companies, Services, ServiceOptions, and Bookings operation in §3.1. All
  other tiny/deprecated routes, including OAuth callbacks, email confirmation,
  address set-primary, catalog filter reads, and Customer health HEAD/DB
  health remain retained and covered.
- Step 15/Step 14 payment capability contracts may continue to report `Wallet`
  as an unavailable method with the already-frozen stable reason; this neutral
  wire enum does not imply a wallet entity, table, repository, DI service,
  balance field, or route.

## 2. Exact retained route inventories

Every line is a concrete required route-contract case, not a representative
sample. Contract tests compare runtime endpoint data and OpenAPI to these lists.

### 2.1 Customer host

| Auth | Methods and canonical routes |
|---|---|
| Anonymous | `POST /api/Auth/register`; `POST /api/Auth/login`; `POST /api/Auth/validate`; `GET /api/Auth/external-login`; `GET /api/Auth/external-login-callback`; `POST /api/Users`; `POST /api/v1/devices/register`; `GET /api/Health`; `HEAD /api/Health`; `GET /api/Health/db`; `POST /api/stripe/webhook` (Stripe signature) |
| Customer bearer | `GET /api/Auth/me`; `POST /api/Auth/link-external-login`; `GET /api/Auth/link-external-login-callback`; `DELETE /api/Auth/external-login/{provider}`; `GET /api/Auth/external-logins`; `GET /api/Users/{id}`; `GET /api/Users/me`; `PUT /api/Users/me`; `DELETE /api/Users/me`; `PUT /api/Users/me/reactivate`; `PUT /api/Users/me/password`; `POST /api/Users/me/email/request-confirmation`; `POST /api/Users/me/email/confirm`; `GET /api/Addresses/my-addresses`; `GET /api/Addresses/{id}`; `POST /api/Addresses`; `PUT /api/Addresses/{id}`; `DELETE /api/Addresses/{id}`; `PUT /api/Addresses/{id}/set-primary`; `GET /api/Vehicles/my-vehicles`; `GET /api/Vehicles/{id}`; `POST /api/Vehicles`; `PUT /api/Vehicles/{id}`; `DELETE /api/Vehicles/{id}` |
| Customer Admin bearer | `GET /api/Users`; `PUT /api/Users/{id}`; `DELETE /api/Users/{id}` |
| Device | `GET /api/v1/configuration`; `GET /api/v1/catalog/categories`; `GET /api/v1/catalog/businesses`; `GET /api/v1/catalog/businesses/{id}`; `GET /api/v1/catalog/businesses/{id}/offerings`; `GET /api/v1/catalog/offerings/{id}`; `POST /api/v1/checkout/drafts`; `GET /api/v1/checkout/drafts/{orderGuid}`; `PUT /api/v1/checkout/drafts/{orderGuid}`; `POST /api/v1/pricing/reprice`; `POST /api/v1/checkout/reprice` |
| Device + Customer `User` bearer | `POST /api/v1/bookings/from-draft`; `POST /api/v1/payments/intents`; `GET /api/v1/payments/{id}` |
| Internal HMAC | `POST /api/v1/internal/bookings/status`; `POST /api/v1/internal/bookings/{reference}/reconcile`; `GET /api/v1/internal/bookings/{reference}` |

The explicit unmatched internal-booking guard remains a safe non-Swagger 404
endpoint shape only; it is not an additional callable domain operation.

### 2.2 Business host

| Auth | Methods and canonical routes |
|---|---|
| Anonymous | `POST /api/v1/business/auth/register-owner`; `POST /api/v1/business/auth/login`; `GET /api/health`; `HEAD /api/health` |
| Business member | `GET /api/v1/business/company`; `POST /api/v1/business/work-orders/{id}/transitions` |
| Business Owner/Admin | `PUT /api/v1/business/company`; `POST /api/v1/business/company/branches`; `PUT /api/v1/business/company/branches/{branchId}`; `GET /api/v1/business/catalog/categories`; `GET /api/v1/business/catalog/categories/{categoryId}`; `POST /api/v1/business/catalog/categories`; `PUT /api/v1/business/catalog/categories/{categoryId}`; `DELETE /api/v1/business/catalog/categories/{categoryId}`; `GET /api/v1/business/catalog/offerings`; `GET /api/v1/business/catalog/offerings/{offeringId}`; `POST /api/v1/business/catalog/offerings`; `PUT /api/v1/business/catalog/offerings/{offeringId}`; `DELETE /api/v1/business/catalog/offerings/{offeringId}`; `GET /api/v1/business/catalog/offerings/{offeringId}/addon-groups`; `GET /api/v1/business/catalog/addon-groups/{addonGroupId}`; `POST /api/v1/business/catalog/offerings/{offeringId}/addon-groups`; `PUT /api/v1/business/catalog/addon-groups/{addonGroupId}`; `DELETE /api/v1/business/catalog/addon-groups/{addonGroupId}`; `GET /api/v1/business/catalog/addon-groups/{addonGroupId}/choices`; `GET /api/v1/business/catalog/addon-choices/{addonChoiceId}`; `POST /api/v1/business/catalog/addon-groups/{addonGroupId}/choices`; `PUT /api/v1/business/catalog/addon-choices/{addonChoiceId}`; `DELETE /api/v1/business/catalog/addon-choices/{addonChoiceId}`; `GET /api/v1/business/availability/branches/{branchId}/settings`; `PUT /api/v1/business/availability/branches/{branchId}/settings`; `GET /api/v1/business/availability/branches/{branchId}/recurring-schedules`; `GET /api/v1/business/availability/recurring-schedules/{scheduleId}`; `POST /api/v1/business/availability/branches/{branchId}/recurring-schedules`; `PUT /api/v1/business/availability/recurring-schedules/{scheduleId}`; `DELETE /api/v1/business/availability/recurring-schedules/{scheduleId}`; `GET /api/v1/business/availability/branches/{branchId}/date-overrides`; `GET /api/v1/business/availability/date-overrides/{overrideId}`; `POST /api/v1/business/availability/branches/{branchId}/date-overrides`; `PUT /api/v1/business/availability/date-overrides/{overrideId}`; `DELETE /api/v1/business/availability/date-overrides/{overrideId}`; `GET /api/v1/business/availability/branches/{branchId}/service-area`; `PUT /api/v1/business/availability/branches/{branchId}/service-area`; `DELETE /api/v1/business/availability/branches/{branchId}/service-area` |
| Business Admin | `POST /api/v1/business/admin/booking-status-outbox/{eventId}/requeue` |
| Internal HMAC | `GET /api/v1/internal/catalog/snapshot`; `POST /api/v1/internal/appointments/validate`; `POST /api/v1/internal/reservations`; `GET /api/v1/internal/reservations/{reference}` |

Assignment/ownership checks remain additional to the displayed Business role.
No route in either inventory is silently moved to the other host.

## 3. Exact removed and foreign-route negative inventories

### 3.1 Customer host removals

These exact operations intentionally supersede Step 15 retention scenarios
`STEP15-LEGACY-BOOKINGS-118`, `STEP15-LEGACY-COMPANIES-119`,
`STEP15-LEGACY-SERVICES-120`, and
`STEP15-LEGACY-SERVICEOPTIONS-121` in full. Every verb/path below is absent
from endpoint metadata and Customer Swagger:

- all six `/api/Companies` operations: `GET /api/Companies`,
  `GET /api/Companies/{id}`, `GET /api/Companies/area/{area}`,
  `POST /api/Companies/create`, `PUT /api/Companies/{id}`,
  `DELETE /api/Companies/{id}`;
- all six `/api/Services` operations: `GET /api/Services`,
  `GET /api/Services/{id}`, `GET /api/Services/{id}/with-options`,
  `POST /api/Services`, `PUT /api/Services/{id}`,
  `DELETE /api/Services/{id}`;
- all seven `/api/ServiceOptions` operations: `GET /api/ServiceOptions`,
  `GET /api/ServiceOptions/{id}`,
  `GET /api/ServiceOptions/service/{serviceId}`,
  `GET /api/ServiceOptions/company/{companyId}`,
  `POST /api/ServiceOptions`, `PUT /api/ServiceOptions/{id}`,
  `DELETE /api/ServiceOptions/{id}`;
- all twelve `/api/Bookings` operations:
  `GET /api/Bookings/my-bookings`,
  `GET /api/Bookings/my-bookings/upcoming`,
  `GET /api/Bookings/my-bookings/history`,
  `GET /api/Bookings/company/{companyId}`, `GET /api/Bookings/{id}`,
  `POST /api/Bookings`, `PUT /api/Bookings/{id}`,
  `PUT /api/Bookings/{id}/cancel`, `PUT /api/Bookings/{id}/confirm`,
  `PUT /api/Bookings/{id}/start`, `PUT /api/Bookings/{id}/complete`,
  `GET /api/Bookings/check-availability`;
- every legacy Payments operation remains intentionally
  undiscoverable/non-controller as already frozen by Step 14:
  `GET /api/Payments`, `GET /api/Payments/{id}`,
  `GET /api/Payments/my-payments`,
  `GET /api/Payments/booking/{bookingId}`, `POST /api/Payments`,
  `PUT /api/Payments/{id}/status`, `POST /api/Payments/{id}/refund`;
- aliases for Business auth/profile/company/branch/catalog/availability,
  work-order/outbox, and Business internal routes are absent;
- Customer registration/admin updates cannot assign `Company`, `Owner`, or
  `Employee`, and no `CompanyPolicy` or company-owner bearer path remains;
- wallet/notification persistence and presentation surfaces are absent:
  no `Wallet`, `WalletTransaction`, or `Notification` EF entity/DbSet/model
  navigation or configuration; no wallet/notification repository, handler, or
  DI registration; no wallet balance in Customer user HTTP DTOs or Swagger;
  no `/api/Wallet`, `/api/Wallets`, `/api/WalletTransactions`,
  `/api/Notification`, or `/api/Notifications` endpoint/alias. The unsupported
  `Wallet` payment-method value described in §1.5 is the sole neutral contract
  reference and never causes wallet persistence.

### 3.2 Business host exclusions

Business endpoint metadata and Swagger contain no Customer route from §2.1 and
no alias under another casing/prefix. Explicit forbidden families are:

`/api/Auth*`, `/api/Users*`, `/api/Addresses*`, `/api/Vehicles*`,
`/api/Health*`, `/api/v1/devices*`, `/api/v1/configuration`,
`/api/v1/catalog*`, `/api/v1/checkout*`, `/api/v1/pricing*`,
`/api/v1/bookings*`, `/api/v1/payments*`, `/api/stripe*`, and Customer
`/api/v1/internal/bookings*`. Wallet and notification paths named in §3.1 are
also absent.

The shared `/api/v1/internal` prefix is not shared authority: only each host's
explicit internal operations exist, and each HMAC client grant is
operation-specific.

For avoidance of doubt, Business wrong-host contract tests expand every
Customer operation in §2.1 into a concrete `(method,path)` case; family labels
below are only reporting groups and never permit sampling.

## 4. Execution levels, fixtures, and invariants

| Level | Required use |
|---|---|
| **Live-local** | Real SQL Server empty databases, `dotnet ef database update`, real Kestrel start/stop, Development/Production Swagger, health, wrong host/connection, DB outage/recovery, HTTPS/HMAC, independent process lifecycle, and reset/recreate/repeat migration. |
| **TestServer** | Every retained and removed route, auth/policy/assignment, cross-token rejection, exact Step 15 problems/headers/languages, side-effect checks, injected DB/schema/dependency failures. |
| **Contract** | Endpoint metadata, complete route/verb/auth inventory, OpenAPI inclusion/exclusion/security, project references, DbContext entity/table/FK ownership, migration operations and model snapshots. |
| **Automated-only** | SQL catalog inspection, idempotent scripts, migration bundle/script determinism, seed counts, compiled dependency graph, log redaction, concurrency, and teardown/process/connection assertions. |

Required disposable fixtures:

- two unique empty local SQL databases and two SQL principals restricted to
  only their owning database; a third wrong/foreign database; unreachable,
  login-denied, current, empty-unmigrated, partially migrated, missing-table,
  extra-table, and newer/incompatible-schema variants;
- Customer User/Admin and Business Owner/Employee/Admin identities; opposite
  issuer/audience tokens; expired/malformed tokens; device tokens; HMAC clients
  with per-operation grants; Stripe test signature only;
- minimal owned Customer and Business domain fixtures added after the
  zero-domain-seed assertion, including ownership/assignment counterparts;
- fixed UTC clock, unique correlation/idempotency/nonce/order/reference values,
  and captured safe logs; never real credentials, PII, or production hosts.

Global invariants on every scenario:

1. only the expected owned database changes;
2. rejection means zero domain/Identity/nonce/idempotency/outbox/webhook/provider
   side effects unless an earlier HMAC replay contract explicitly consumes a
   valid nonce before a later domain rejection;
3. foreign/removed route probes never invoke the other host or its database;
4. Step 15 exact content type, language applicability, correlation, cache and
   security headers, redaction, and full Problem Details apply;
5. teardown is idempotent and proves both processes stopped, pools released,
   disposable databases droppable, and no test listener remains.

A new live manifest is required during implementation:
`scripts/http-tests/plans/step-16-clean-schema-separation.manifest.json`.
It must include every scenario below tagged `live-local`, use only disposable
local databases, and chain no secret into committed output.

Compact rows inherit all required scenario-schema fields from
`HTTP_TEST_PLAN_STANDARD.md`: `featureStep=STEP_16`, exact request data in the
eventual test/manifest, unique fixture setup, stated auth, the expected status
and stable code, full response/header/side-effect assertions, narrow cleanup,
the listed automation, `status=planned`, `resultEvidence=none yet`, and
`deferredRationale=none`.

## 5. Stable scenarios

### A. Migration and clean creation

| ID | Level | Request / expected result |
|---|---|---|
| `STEP16-MIGRATION-CUSTOMER-EMPTY-001` | Live-local + automated-only | Apply Customer migrations to a brand-new empty DB; success, complete exact Customer inventory, no Business/obsolete table. |
| `STEP16-MIGRATION-BUSINESS-EMPTY-002` | Live-local + automated-only | Apply Business migrations to a separate empty DB; complete exact Business inventory, no Customer table. |
| `STEP16-MIGRATION-CUSTOMER-SCRIPT-003` | Contract + automated-only | Customer idempotent SQL script starts from zero, contains only owned objects, and executes successfully. |
| `STEP16-MIGRATION-BUSINESS-SCRIPT-004` | Contract + automated-only | Equivalent Business script invariant. |
| `STEP16-MIGRATION-CUSTOMER-REPEAT-005` | Live-local | Reapply Customer update twice; both no-op, history/schema/data hashes unchanged. |
| `STEP16-MIGRATION-BUSINESS-REPEAT-006` | Live-local | Reapply Business update twice; no-op invariant. |
| `STEP16-MIGRATION-CUSTOMER-RESET-007` | Live-local | Drop/recreate only Customer DB, reapply, start Customer, and repeat; Business DB/data/host unaffected. |
| `STEP16-MIGRATION-BUSINESS-RESET-008` | Live-local | Drop/recreate only Business DB; Customer unaffected. |
| `STEP16-MIGRATION-ORDER-CUSTOMER-FIRST-009` | Live-local | Customer migrate/start before Business DB exists succeeds independently. |
| `STEP16-MIGRATION-ORDER-BUSINESS-FIRST-010` | Live-local | Business migrate/start before Customer DB exists succeeds independently. |
| `STEP16-MIGRATION-PARALLEL-011` | Live-local | Both fresh migrations run concurrently against separate DBs without lock/name/history collision. |
| `STEP16-MIGRATION-HISTORY-SEPARATE-012` | Automated-only | Each history contains only its owning migration IDs; no shared/cross-applied ID. |
| `STEP16-MIGRATION-MODEL-CURRENT-013` | Automated-only | `has-pending-model-changes` is false for each context. |
| `STEP16-MIGRATION-DOWN-CUSTOMER-014` | Automated-only | Disposable Customer down/up round-trip is valid or explicitly baseline-only; never touches Business. |
| `STEP16-MIGRATION-DOWN-BUSINESS-015` | Automated-only | Equivalent Business down/up behavior. |
| `STEP16-MIGRATION-EMPTY-UNMIGRATED-016` | Live-local | Starting/using host on empty unmigrated owned DB fails closed; no EnsureCreated/partial tables. |
| `STEP16-MIGRATION-PARTIAL-CUSTOMER-017` | TestServer + automated-only | Customer partially migrated schema is rejected safely; no opportunistic foreign/obsolete recreation. |
| `STEP16-MIGRATION-PARTIAL-BUSINESS-018` | TestServer + automated-only | Equivalent Business partial schema rejection. |
| `STEP16-MIGRATION-NEWER-CUSTOMER-019` | Automated-only | Unknown/newer Customer history/schema mismatch fails deployment validation safely. |
| `STEP16-MIGRATION-NEWER-BUSINESS-020` | Automated-only | Equivalent Business mismatch. |
| `STEP16-MIGRATION-NAME-COLLISION-021` | Automated-only | Same logical table names in separate DBs remain independent; no three-part names or cross-FKs. |
| `STEP16-MIGRATION-DETERMINISTIC-022` | Automated-only | Two clean creations have equivalent normalized SQL catalog definitions. |
| `STEP16-MIGRATION-NO-LEGACY-MYSQL-023` | Contract | No Pomelo/MySQL annotations, provider history, or deleted migration chain is required. |
| `STEP16-MIGRATION-NO-AUTO-PRODUCTION-024` | Contract + live-local | Production startup does not destructively reset or silently migrate an incompatible DB. |

### B. Table, key, relationship, and seed ownership

| ID | Level | Request / expected result |
|---|---|---|
| `STEP16-SCHEMA-CUSTOMER-POSITIVE-025` | Automated-only | SQL catalog contains every and only Customer table listed in §1.2 plus history. |
| `STEP16-SCHEMA-CUSTOMER-NEGATIVE-026` | Automated-only | All nine removed tables (`Companies`, `CompanyAvailabilities`, `Services`, `ServiceOptions`, `Bookings`, `Payments`, `Wallets`, `WalletTransactions`, `Notifications`) and all Business-only tables are absent; no singular/renamed compatibility variant exists. |
| `STEP16-SCHEMA-BUSINESS-POSITIVE-027` | Automated-only | SQL catalog contains every and only Business table listed in §1.2 plus history. |
| `STEP16-SCHEMA-BUSINESS-NEGATIVE-028` | Automated-only | Every Customer-only/legacy table listed in §1.2 is absent. |
| `STEP16-SCHEMA-CUSTOMER-FKS-029` | Contract + automated-only | Every Customer FK resolves inside Customer DB and matches owned relationship/delete rules. |
| `STEP16-SCHEMA-BUSINESS-FKS-030` | Contract + automated-only | Every Business FK resolves inside Business DB. |
| `STEP16-SCHEMA-NO-CROSS-DATABASE-031` | Automated-only | No view/synonym/trigger/procedure/FK/query text contains foreign DB or three/four-part names. |
| `STEP16-SCHEMA-CUSTOMER-CONSTRAINTS-032` | Automated-only | Customer PK/unique/index/filter/rowversion/precision/default/length inventory matches EF model. |
| `STEP16-SCHEMA-BUSINESS-CONSTRAINTS-033` | Automated-only | Business constraint inventory matches EF model. |
| `STEP16-SCHEMA-IDENTITY-SEPARATE-034` | Automated-only | Same email/user/role GUID can exist independently; no shared Identity row or FK. |
| `STEP16-SEED-CUSTOMER-ROLES-035` | Live-local + automated-only | Fresh Customer role set becomes exactly `User`,`Admin`, one normalized row each. |
| `STEP16-SEED-BUSINESS-ROLES-036` | Live-local + automated-only | Fresh Business role set becomes exactly `Owner`,`Employee`,`Admin`. |
| `STEP16-SEED-NO-COMPANY-ROLE-037` | Contract + TestServer | Customer `Company` role/policy/registration assignment is absent and rejected. |
| `STEP16-SEED-IDEMPOTENT-038` | Live-local | Repeated and concurrent initialization does not duplicate/change roles. |
| `STEP16-SEED-ZERO-DOMAIN-CUSTOMER-039` | Automated-only | Before fixture setup, every Customer domain/read-model/security table has zero rows. |
| `STEP16-SEED-ZERO-DOMAIN-BUSINESS-040` | Automated-only | Before owner registration, every Business domain/security/outbox table has zero rows. |
| `STEP16-SEED-OWNER-TRANSACTION-041` | TestServer + repository | First Business owner registration creates only Business user/company/assignment/Owner role atomically. |
| `STEP16-SEED-OWNER-ROLLBACK-042` | TestServer + repository | Injected company/assignment/role failure rolls back partial owner domain state safely. |
| `STEP16-SCHEMA-EXTRA-TABLE-GATE-043` | Automated-only | Unexpected unowned table causes ownership verification failure; it is not silently ignored/dropped. |
| `STEP16-SCHEMA-NO-DOMAIN-SHARING-044` | Contract | IntegrationContracts assembly scan finds no EF/Identity/domain/application dependency. |

### C. Startup, health, configuration, and deployment independence

| ID | Level | Request / expected result |
|---|---|---|
| `STEP16-START-CUSTOMER-ONLY-045` | Live-local | Customer starts/serves health and owned routes with Business host/DB absent. |
| `STEP16-START-BUSINESS-ONLY-046` | Live-local | Business starts/serves health and owned routes with Customer host/DB absent. |
| `STEP16-START-BOTH-047` | Live-local | Both start concurrently on distinct ports/DBs with no service/DI/controller collision. |
| `STEP16-START-STOP-CUSTOMER-048` | Live-local | Stop/restart Customer while Business requests continue; clean cancellation and port/DB release. |
| `STEP16-START-STOP-BUSINESS-049` | Live-local | Stop/restart Business while Customer nonintegration requests continue. |
| `STEP16-DEPLOY-CUSTOMER-INDEPENDENT-050` | Automated-only + live-local | Customer build/publish artifact has no Business implementation binary/config/migration. |
| `STEP16-DEPLOY-BUSINESS-INDEPENDENT-051` | Automated-only + live-local | Business artifact has no Customer implementation binary/config/migration. |
| `STEP16-START-MISSING-CONNECTION-CUSTOMER-052` | Live-local | Missing Customer connection fails startup/readiness safely; never tries Business/default foreign value. |
| `STEP16-START-MISSING-CONNECTION-BUSINESS-053` | Live-local | Equivalent Business failure. |
| `STEP16-START-WRONG-CONNECTION-CUSTOMER-054` | Live-local | Customer pointed at Business DB detects schema mismatch, changes nothing, safe failure/logs. |
| `STEP16-START-WRONG-CONNECTION-BUSINESS-055` | Live-local | Business pointed at Customer DB detects mismatch and changes nothing. |
| `STEP16-DB-OUTAGE-CUSTOMER-056` | Live-local + TestServer | Customer SQL unavailable: liveness remains bounded, DB health 503, DB route safe 503/500, no foreign fallback. |
| `STEP16-DB-OUTAGE-BUSINESS-057` | Live-local + TestServer | Equivalent Business outage behavior; outbox worker contains/retries safely. |
| `STEP16-DB-RECOVERY-CUSTOMER-058` | Live-local | Restore Customer DB; health/owned route recover without Business restart or duplicate seed. |
| `STEP16-DB-RECOVERY-BUSINESS-059` | Live-local | Restore Business DB; worker/routes recover without duplicate reservation/outbox effects. |
| `STEP16-HEALTH-CUSTOMER-060` | TestServer + live-local | GET/HEAD Customer health and DB health have exact status/body/header/language-neutral contract. |
| `STEP16-HEALTH-BUSINESS-061` | TestServer + live-local | GET/HEAD Business health exact contract; no foreign dependency probe. |
| `STEP16-HEALTH-WRONG-SCHEMA-062` | Live-local | DB reachable but required owned table/history incompatible: DB health is safe non-healthy, never false 200. |
| `STEP16-SWAGGER-ENVIRONMENTS-063` | Live-local | Each Development/explicit host exposes own JSON/UI; Production/default returns hardened 404. |
| `STEP16-ROOT-INDEPENDENT-064` | Live-local | Enabled root redirects only to same-host Swagger; disabled root is safe 404, never cross-host. |

### D. Route and authorization inventory contracts

| ID | Level | Request / expected result |
|---|---|---|
| `STEP16-ROUTES-CUSTOMER-EXACT-065` | Contract | Runtime endpoint metadata equals every concrete Customer operation in §2.1, no extra controller operation. |
| `STEP16-ROUTES-BUSINESS-EXACT-066` | Contract | Runtime endpoint metadata equals every concrete Business operation in §2.2. |
| `STEP16-SWAGGER-CUSTOMER-EXACT-067` | Contract + live-local | Customer OpenAPI contains retained operations only, exact verbs/security/statuses, no removed/Business paths. |
| `STEP16-SWAGGER-BUSINESS-EXACT-068` | Contract + live-local | Business OpenAPI contains retained operations only and no Customer/Stripe path. |
| `STEP16-ROUTES-NO-DUPLICATES-069` | Contract | No duplicate/case-only/ambiguous route, operation ID, controller discovery, or compatibility alias. |
| `STEP16-AUTH-CUSTOMER-TOKEN-070` | TestServer + live-local | Customer token succeeds only on its retained allowed Customer route and is 401 on Business. |
| `STEP16-AUTH-BUSINESS-TOKEN-071` | TestServer + live-local | Business token succeeds only on assigned Business route and is 401 on Customer. |
| `STEP16-AUTH-CROSS-ROLE-NAMES-072` | TestServer | Same textual Admin role across issuers never crosses host; Company/Owner/Employee never map. |
| `STEP16-AUTH-DEVICE-CROSS-HOST-073` | TestServer + live-local | Customer device token on Business route confers nothing and is not persisted there. |
| `STEP16-AUTH-HMAC-ONLY-074` | TestServer + live-local | Valid per-operation HMAC authorizes only intended internal operation/host. |
| `STEP16-AUTH-JWT-NOT-HMAC-075` | TestServer | Either bearer plus device on internal route still gets exact English HMAC 401. |
| `STEP16-AUTH-HMAC-NOT-USER-076` | TestServer | HMAC quartet on Customer/Business user route confers no bearer/device authorization. |

### E. Retained Customer behavior

| ID | Level | Request / expected result |
|---|---|---|
| `STEP16-CUSTOMER-AUTH-077` | TestServer + live-local | Execute all ten retained `/api/Auth` operations with exact anonymous/bearer policies; User issuance only. |
| `STEP16-CUSTOMER-USERS-078` | TestServer | Execute all twelve `/api/Users` operations; self/Admin ownership and role protections unchanged. |
| `STEP16-CUSTOMER-ADDRESSES-079` | TestServer + live-local | Execute all six address operations, owner masking and persistence in Customer DB only. |
| `STEP16-CUSTOMER-VEHICLES-080` | TestServer + live-local | Execute all five vehicle operations, owner masking and Customer-only persistence. |
| `STEP16-CUSTOMER-DEVICES-081` | TestServer + live-local | Device issue/rotation/old-token rejection works on clean Customer schema. |
| `STEP16-CUSTOMER-CONFIGURATION-082` | TestServer | Device-auth configuration read/empty-unavailable behavior remains localized. |
| `STEP16-CUSTOMER-CATALOG-083` | TestServer | Execute all five Customer catalog reads from local read model; no direct Business DB access. |
| `STEP16-CUSTOMER-DRAFTS-084` | TestServer | Execute create/read/update with device ownership, versioning, expiry and Customer-only rows. |
| `STEP16-CUSTOMER-PRICING-DIRECT-085` | TestServer | Direct reprice uses HMAC Business HTTP only, persists no draft/domain row. |
| `STEP16-CUSTOMER-PRICING-DRAFT-086` | TestServer | Draft reprice atomically updates Customer snapshot; Business DB changes only per internal idempotency contract. |
| `STEP16-CUSTOMER-BOOKING-CONFIRM-087` | TestServer | Device+User confirmation preserves Customer booking/Business reservation split and references, no cross-DB transaction. |
| `STEP16-CUSTOMER-PAYMENT-INTENT-088` | TestServer | Intent persists only modern Customer payment/idempotency rows from immutable booking money. |
| `STEP16-CUSTOMER-PAYMENT-READ-089` | TestServer | Owned modern payment read remains device+User scoped and client-safe. |
| `STEP16-CUSTOMER-INTERNAL-STATUS-090` | TestServer | HMAC callback persists only Customer inbox/booking state, exact idempotency. |
| `STEP16-CUSTOMER-INTERNAL-RECONCILE-091` | TestServer | HMAC reconcile route and Customer outbound HMAC lookup remain isolated and monotonic. |
| `STEP16-CUSTOMER-INTERNAL-READ-092` | TestServer | HMAC Customer booking read exact schema/not-found; no Business table lookup. |
| `STEP16-CUSTOMER-STRIPE-093` | TestServer | Stripe webhook remains only on Customer, signature-only, modern payment/webhook tables only. |
| `STEP16-CUSTOMER-ADMIN-NO-COMPANY-094` | TestServer | Admin user updates reject Company/Owner/Employee roles with exact problem and no role mutation. |
| `STEP16-CUSTOMER-EMPTY-CATALOG-095` | TestServer + live-local | Fresh schema with no provider returns documented safe empty/unavailable behavior, never queries a fake seed. |
| `STEP16-CUSTOMER-REGRESSION-096` | Automated-only | All retained Customer Steps 7–15 tests, including every tiny OAuth callback, email-confirmation, set-primary, filter/read and health HEAD/DB-health operation, pass against clean schema; superseded Step 15 Companies/Services/ServiceOptions/Bookings retention assertions are replaced by 404 tests, never silently deleted. |

### F. Retained Business behavior

| ID | Level | Request / expected result |
|---|---|---|
| `STEP16-BUSINESS-AUTH-097` | TestServer + live-local | Owner register/login on clean schema issue Business token/roles only. |
| `STEP16-BUSINESS-COMPANY-098` | TestServer | Execute all four company/branch operations with Owner/Admin assignment and Business-only rows. |
| `STEP16-BUSINESS-CATEGORIES-099` | TestServer | Execute all five category operations, ownership/concurrency/localization unchanged. |
| `STEP16-BUSINESS-OFFERINGS-100` | TestServer | Execute all five offering operations and owned relationships. |
| `STEP16-BUSINESS-ADDON-GROUPS-101` | TestServer | Execute all five group operations and read-after-delete/conflict rules. |
| `STEP16-BUSINESS-ADDON-CHOICES-102` | TestServer | Execute all five choice operations and selection/default invariants. |
| `STEP16-BUSINESS-AVAILABILITY-SETTINGS-103` | TestServer | Execute GET/PUT settings on Business DB only. |
| `STEP16-BUSINESS-AVAILABILITY-SCHEDULES-104` | TestServer | Execute all five recurring schedule operations. |
| `STEP16-BUSINESS-AVAILABILITY-OVERRIDES-105` | TestServer | Execute all five date-override operations. |
| `STEP16-BUSINESS-SERVICE-AREA-106` | TestServer | Execute GET/PUT/DELETE service area operations. |
| `STEP16-BUSINESS-WORKORDER-107` | TestServer | Member transition updates reservation/work order and outbox atomically in Business DB. |
| `STEP16-BUSINESS-REQUEUE-108` | TestServer | Admin dead-letter requeue persists only Business audit/generation rows. |
| `STEP16-BUSINESS-INTERNAL-CATALOG-109` | TestServer | HMAC snapshot reads Business catalog only. |
| `STEP16-BUSINESS-INTERNAL-VALIDATE-110` | TestServer | HMAC validation reads authoritative Business availability/pricing only. |
| `STEP16-BUSINESS-INTERNAL-RESERVE-111` | TestServer | Execute reserve/read routes with Business idempotency/reservation/work-order rows only. |
| `STEP16-BUSINESS-REGRESSION-112` | Automated-only | All retained Business Steps 2–15 tests pass against clean Business schema. |

### G. Customer intentional responsibility removals

| ID | Level | Request / expected result |
|---|---|---|
| `STEP16-REMOVED-COMPANIES-READS-113` | Contract + TestServer + live-local | Probe all three removed Companies GET routes: hardened 404, absent Swagger/metadata, no DB/provider call. |
| `STEP16-REMOVED-COMPANIES-WRITES-114` | Contract + TestServer + live-local | Probe POST/PUT/DELETE Companies with anonymous/User/Admin/old Company token: 404 before auth/domain, no side effect. |
| `STEP16-REMOVED-SERVICES-READS-115` | Contract + TestServer | Probe all three removed Services GET routes with existing/unknown GUIDs: indistinguishable 404. |
| `STEP16-REMOVED-SERVICES-WRITES-116` | Contract + TestServer + live-local | Probe POST/PUT/DELETE Services under every former role: safe 404/no side effect. |
| `STEP16-REMOVED-OPTIONS-READS-117` | Contract + TestServer | Probe all four ServiceOptions GET routes: safe 404 and no legacy table lookup. |
| `STEP16-REMOVED-OPTIONS-WRITES-118` | Contract + TestServer + live-local | Probe all three ServiceOptions writes: safe 404/no side effect. |
| `STEP16-REMOVED-BOOKINGS-CUSTOMER-119` | Contract + TestServer + live-local | Probe the six formerly preserved Customer compatibility operations (my/upcoming/history/id/cancel/check-availability) with anonymous, Customer User/Admin, obsolete Company and Business tokens: route-level 404 (never 401/403), absent metadata/Swagger, no handler/repository/DbContext/outbound/payment call and no row/version side effect. This explicitly supersedes their Step 15 retention. |
| `STEP16-REMOVED-BOOKINGS-BUSINESS-120` | Contract + TestServer + live-local | Probe the other six legacy booking operations (company list/create/update/confirm/start/complete) with the same credential matrix: route-level 404 (never 401/403), absent metadata/Swagger, no handler/repository/DbContext/outbound/payment call, no Customer/Business transition and no row/version side effect. |
| `STEP16-REMOVED-PAYMENTS-121` | Contract + TestServer | Probe all seven exact former legacy Payments operations in §3.1: route-level 404 before auth, no handler/DB/provider call; Step 14 modern `/api/v1/payments` and Stripe guarantees remain green. |
| `STEP16-REMOVED-COMPANY-ROLE-122` | TestServer + live-local | Register/login/admin-role payloads naming `Company` cannot create/issue/use it; exact localized rejection. |
| `STEP16-REMOVED-BUSINESS-ALIASES-123` | Contract + TestServer | Every concrete Business management operation from §2.2 on Customer host returns route-level 404 (never 401/403), even with valid Business JWT; no handler/DB call or side effect. |
| `STEP16-REMOVED-BUSINESS-INTERNAL-124` | Contract + TestServer | Every concrete Business catalog/appointment/reservation internal operation from §2.2 on Customer host returns HMAC-safe route-level 404 and invokes no handler/DB; no Customer nonce/idempotency row is consumed. |
| `STEP16-REMOVED-CASE-SLASH-125` | TestServer | Case, trailing slash, encoded segment, query, and path-base variants cannot rediscover a removed controller. |
| `STEP16-REMOVED-NO-405-LEAK-126` | TestServer | Alternate verbs on removed paths remain route-level 404 (not 405/Allow revealing an old endpoint). |

### H. Business foreign Customer-route exclusions

| ID | Level | Request / expected result |
|---|---|---|
| `STEP16-FOREIGN-CUSTOMER-AUTH-USERS-127` | Contract + TestServer + live-local | Every concrete Customer Auth/Users operation in §2.1 on Business host is route-level 404 before auth (never 401/403); OAuth callbacks/email flows invoke no handler and cause no Business Identity mutation. |
| `STEP16-FOREIGN-ADDRESS-VEHICLE-128` | Contract + TestServer | Every concrete address/vehicle operation, including set-primary, on Business host is route-level 404 for all tokens with no handler/DB side effect. |
| `STEP16-FOREIGN-DEVICE-CONFIG-129` | Contract + TestServer + live-local | Device registration/configuration paths are 404; no Business token hash/config table. |
| `STEP16-FOREIGN-CUSTOMER-CATALOG-130` | Contract + TestServer | All five concrete Customer catalog/filter/read operations are route-level 404 with no handler/DB call and cannot alias Business catalog. |
| `STEP16-FOREIGN-DRAFT-PRICING-131` | Contract + TestServer + live-local | All draft/direct/draft-reprice paths are 404; no Business draft/idempotency mutation. |
| `STEP16-FOREIGN-CUSTOMER-BOOKING-132` | Contract + TestServer | Modern from-draft plus every intentionally removed legacy Customer booking operation is route-level 404 on Business with no handler/DB call; reservation remains HMAC-only. |
| `STEP16-FOREIGN-PAYMENTS-STRIPE-133` | Contract + TestServer + live-local | Modern/legacy payment and Stripe paths are 404; signed Stripe body has no effect. |
| `STEP16-FOREIGN-CUSTOMER-INTERNAL-134` | Contract + TestServer | Customer callback/reconcile/read internal paths are 404 with valid Business-side HMAC; no nonce/outbox change. |
| `STEP16-FOREIGN-HEALTH-NAMES-135` | Contract + live-local | Customer `/api/Health` and `/api/Health/db` do not alias Business `/api/health`; noncanonical paths are 404. |
| `STEP16-FOREIGN-CREDENTIAL-MATRIX-136` | TestServer | Customer JWT/device/Stripe/HMAC credentials on every Business foreign family never change 404 or authorize. |
| `STEP16-FOREIGN-CASE-SLASH-137` | TestServer | Case/trailing/encoded/query variants cannot discover Customer controllers on Business. |
| `STEP16-FOREIGN-NO-405-LEAK-138` | TestServer | Alternate verbs on absent Customer paths remain 404 without `Allow` or controller detail. |

### I. HMAC, failures, legacy-path safety, and Step 15 inheritance

| ID | Level | Request / expected result |
|---|---|---|
| `STEP16-INTEGRATION-HMAC-SUCCESS-139` | TestServer + live-local | Customer→Business and Business→Customer calls use HTTPS, operation grant, timestamp, fresh nonce, signature, and required idempotency exactly. |
| `STEP16-INTEGRATION-NO-DIRECT-DB-140` | Contract + automated-only | Runtime tracing and SQL permissions prove each integration flow opens only owned DB plus HTTPS, never foreign SQL. |
| `STEP16-INTEGRATION-OPPOSITE-DOWN-141` | TestServer + live-local | Integration-required Customer operation fails closed with localized safe 503 when Business is stopped; unrelated Customer routes remain healthy. |
| `STEP16-INTEGRATION-CUSTOMER-DOWN-142` | TestServer + live-local | Business outbox contains/retries safely while Customer is stopped; Business management remains available. |
| `STEP16-INTEGRATION-HMAC-MISSING-143` | TestServer | Missing/duplicate/malformed HMAC quartet yields exact English error, no domain mutation. |
| `STEP16-INTEGRATION-HMAC-REPLAY-144` | TestServer | Replay is rejected in owning nonce store only; same nonce text on opposite host does not imply shared row. |
| `STEP16-INTEGRATION-WRONG-SECRET-145` | TestServer | Secret configured for opposite direction/operation is rejected without fallback. |
| `STEP16-INTEGRATION-HTTP-146` | Live-local | Plain HTTP internal call fails exact `https_required` unless explicit local test override; no redirect-signature ambiguity. |
| `STEP16-LEGACY-PROBLEM-AR-147` | TestServer + live-local | Removed Customer user-facing path defaults to exact Arabic 404 Problem Details and Step 15 headers. |
| `STEP16-LEGACY-PROBLEM-HE-148` | TestServer | Same removed path with Hebrew selection differs only in presentation. |
| `STEP16-LEGACY-PROBLEM-INTERNAL-149` | TestServer | Removed internal path is exact English/language-neutral safe 404. |
| `STEP16-LEGACY-MALFORMED-ID-150` | TestServer | Invalid GUID on retained route is exact 400; on removed/foreign route stays safe 404 without model-binding leak. |
| `STEP16-LEGACY-WRONG-METHOD-151` | TestServer + live-local | Every retained route family wrong verb is exact 405/Allow; removed family stays 404; no mutation. |
| `STEP16-LEGACY-HEAD-OPTIONS-152` | Live-local | HEAD/OPTIONS cannot invoke mutations or reveal removed operations; retained health HEAD remains exact. |
| `STEP16-LEGACY-HEADER-REDACTION-153` | Automated-only | Problems/logs omit tokens, signatures, connection/schema/table/server names, SQL, migration IDs, stack, PII. |
| `STEP16-LEGACY-CACHE-SECURITY-154` | TestServer + live-local | Success, auth, 404, 405, 500/503, health, Swagger-disabled and redirect classes inherit exact Step 15 headers once. |

### J. Adverse schema, repeatability, cleanup, and final gates

| ID | Level | Request / expected result |
|---|---|---|
| `STEP16-ADVERSE-CUSTOMER-TABLE-MISSING-155` | TestServer + live-local | Drop/rename one owned Customer table in disposable DB; affected route/DB health fail safely, no legacy recreation/fallback. |
| `STEP16-ADVERSE-BUSINESS-TABLE-MISSING-156` | TestServer + live-local | Equivalent Business missing-table behavior; worker remains contained. |
| `STEP16-ADVERSE-OBSOLETE-TABLE-PRESENT-157` | Automated-only | Inject obsolete table after migration; ownership gate detects it and no route begins using it. |
| `STEP16-ADVERSE-FOREIGN-TABLE-PRESENT-158` | Automated-only | Copy a foreign-named empty table into either DB; endpoint discovery/EF model do not gain responsibility and gate fails. |
| `STEP16-ADVERSE-LOGIN-DENIED-159` | Live-local | Each SQL principal denied: bounded safe failure, no alternate credential/connection or secret log. |
| `STEP16-ADVERSE-CONCURRENT-HOSTS-160` | Live-local | Two instances of same host against owned DB initialize roles/workers safely and expose identical route inventory. |
| `STEP16-ADVERSE-RESTART-IDEMPOTENCY-161` | Live-local | Repeated start/stop does not mutate schema/history/domain seeds, duplicate roles, or replay work. |
| `STEP16-ADVERSE-RESET-SEQUENCE-162` | Live-local | Reset Customer then Business then both; migrate/start/health/Swagger/retained smoke and negative inventories repeat identically. |
| `STEP16-CLEANUP-ISOLATION-163` | Live-local + automated-only | Stop processes, clear pools, drop only disposable DBs/users, remove runtime files; verify no port/process/database/artifact leak. |
| `STEP16-FINAL-REGRESSION-164` | Automated-only + contract + live-local | Full Release build/tests, both clean migration gates, exact runtime+Swagger route/schema inventories, manifest, complete Step 13 callback/outbox and Step 14 payment/webhook suites, plus Step 15 localization/header/Swagger and retained-route suites with all four superseded legacy-family assertions replaced by Step 16 removal assertions, pass with zero deferral. |
| `STEP16-REMOVED-WALLET-MODEL-165` | Contract + automated-only | Compiled Customer model/source/assembly/DI scan and clean SQL catalog contain no `Wallet`/`WalletTransaction` entity, DbSet, navigation, configuration, repository, handler, registration, migration operation, model-snapshot node, table, FK, index, balance DTO/OpenAPI field, or active implementation dependency; retained Auth/Users/payment capability contracts compile and work without them. |
| `STEP16-REMOVED-NOTIFICATION-MODEL-166` | Contract + automated-only | Equivalent exact absence gate for `Notification`: no entity, DbSet, User navigation, configuration, repository/handler/DI, migration/snapshot/table/FK/index, DTO/OpenAPI field, controller/endpoint, or implementation dependency. |
| `STEP16-REMOVED-WALLET-NOTIFICATION-ROUTES-167` | Contract + TestServer + live-local | Probe GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS against `/api/Wallet`, `/api/Wallets`, `/api/WalletTransactions`, `/api/Notification`, `/api/Notifications` and `{id}` variants using anonymous, Customer User/Admin, Business JWT, device and HMAC credentials: route-level 404 before auth (never 401/403/405), absent runtime/OpenAPI, zero handler/repository/DbContext/outbound/payment/row/version side effect. |
| `STEP16-REMOVED-WALLET-NOTIFICATION-STARTUP-168` | Live-local + automated-only | Fresh migration, repeated migration, Customer startup, role seed, Auth/Users reads, modern payment unsupported-Wallet response, restart, reset/recreate and schema invariant checks never create, query, seed, deserialize, resolve DI for, or otherwise resurrect Wallet/WalletTransactions/Notifications; missing former tables cannot break startup or retained routes. |

## 6. Coverage and fixture mapping

| Requirement | Scenarios |
|---|---|
| Fresh databases, migrations, repeat/reset | 001–024, 162 |
| Exact table/schema positive and negative inventories | 025–034, 043–044, 155–158 |
| Roles and absence of fake seeds | 035–042, 094–095, 122 |
| Startup/health/Swagger/outage/wrong connection | 045–064, 141–142, 155–161 |
| Exact retained route inventories and correct auth | 065–112 |
| Customer intentional legacy removals | 113–126 |
| Business Customer-surface exclusions | 127–138 |
| HMAC-only integration/no implementation or DB dependency | 139–146 |
| Legacy 404/405, languages, errors, headers, no side effects | 113–154 |
| Wallet/notification exact removal | 026, 039, 043, 157–158, 165–168 |
| Independent deploy/start/stop and clean teardown | 045–051, 160–168 |

Coverage-category disposition:

- **Happy path:** every retained concrete route is executed through 077–112;
  lifecycle happy paths are 001–013 and 045–051.
- **Malformed/model binding:** 143, 150–152 plus inherited Step 15 matrices.
- **Authn/authz/ownership:** 070–076, every retained family, 122, 136,
  139–146.
- **Validation boundaries:** retained Step 15 suites through 096/112/164;
  clean-schema scope adds role and connection/schema validation.
- **State/version/idempotency/concurrency:** 005–012, 038, 041–042, 084–093,
  107–111, 139–145, 160–162.
- **Failure/unavailability:** 016–020, 052–059, 141–146, 155–159.
- **Deletion/read-after-delete:** retained Business delete cases 099–106;
  intentional removals 113–138 and 165–168 prove permanent absence.
- **Contract/Swagger:** 003–004, 012–015, 023–024, 025–044, 063–069,
  113–138, 165–168.
- **Localization:** 147–150 and the full inherited Step 15 oracle on every
  retained user-facing family.
- **Security/no leakage:** global invariants, 031, 044, 050–055, 070–076,
  113–154, 159, 163, 165–168.

## 7. Code test plan

- Add contract tests that enumerate endpoint metadata and OpenAPI and compare
  exact `(host, method, canonical route, auth/security)` sets to §2, plus exact
  negative sets in §3.
- Add migration/schema tests against disposable SQL Server databases that query
  `sys.tables`, `sys.columns`, keys, indexes, FKs, defaults, precision,
  rowversion, permissions, and migration history.
- Add project/assembly dependency scans and SQL command interception proving no
  implementation or cross-database dependency.
- Add TestServer parameterized matrices for every concrete retained and removed
  route, every relevant credential, Arabic/Hebrew/English exemption, wrong
  verb, malformed ID, exact problem/header, and zero side effects. Removed and
  wrong-host routes must assert endpoint selection never occurred (404 rather
  than auth 401/403), handler/repository/DbContext/outbound-client call counts
  are zero, and both databases' row/version counts are unchanged.
- Keep all Step 15 tests and all retained Steps 2–14 domain/integration tests
  green after replacing legacy-schema fixtures. Replace, rather than delete,
  Step 15 retention assertions for Companies, Services, ServiceOptions, and
  Bookings with the exact route-level 404/no-call/no-side-effect assertions.
- Add source/compiled-model/DI/OpenAPI/migration-snapshot and SQL-catalog
  absence tests for Wallet, WalletTransaction, and Notification, including
  User navigation/DTO removal and retained modern payment capability behavior.
- Add live fixture verification before the manifest and hardened post-run
  schema/row-count/cleanup verification afterward.

## 8. Completion gate

This planning task freezes **168 unique contiguous scenarios**:

- 24 migration/clean-creation;
- 20 schema/seed ownership;
- 20 startup/health/deployment;
- 12 route-inventory/authorization;
- 20 retained Customer;
- 16 retained Business;
- 14 intentional Customer legacy-removal groups;
- 12 Business exclusions;
- 16 HMAC/legacy/Step 15 inheritance;
- 10 adverse/cleanup/final gates plus 4 wallet/notification removal gates;

The arithmetic category view is: A 24, B 20, C 20, D 12, E 20, F 16, G 14,
H 12, I 16, J 14 = **168**.

No Step 16 implementation is complete until:

1. all 168 applicable scenarios are automated and passed;
2. every concrete retained/removed route and every positive/negative table
   inventory member is checked, not sampled;
3. both clean SQL databases are created, repeatedly migrated, started,
   stopped, reset, and independently recreated;
4. full tests and Step 15 inherited HTTP behavior are green;
5. the live-local manifest and sanitized results record exact totals;
6. no scenario is merely planned/automated/failed and no shipping requirement
   is deferred.

## 9. HTTP test results

Planning status: **not executed — implementation has not begun**.

All scenarios currently have `status: planned`, `resultEvidence: none yet`,
and `deferredRationale: none`. This is correct only for the pre-implementation
freeze and does not satisfy the completion gate.
