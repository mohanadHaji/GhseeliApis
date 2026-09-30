# Agent handoff

This file is the working handoff for future coding agents. For the public
service overview, database tables, and relationship diagrams, start with the
repository root [`README.md`](../README.md).

## Current state

- Customer API: <https://ghseelicustomer.runasp.net>
- Business API: <https://ghseelibusiness.runasp.net>
- Both APIs use trusted HTTPS and separate SQL Server databases.
- Production deployment is manual through
  `.github/workflows/deploy-monsterasp.yml`.
- The deployed Customer and Business schemas are current through the
  2026-09-30 main-category taxonomy and Customer projection migrations.
- The deterministic frontend dataset is deployed in a trusted `IsDemo=true`
  partition inside the hosted databases.
- Production and Demo records are mutually filtered.
- The current hosted environment is intentionally operated as Development:
  `DemoData:PublicApisOnly=true` makes configuration, direct pricing, and
  public catalog discovery/reviews/availability credential-free and Demo-only.
  Customer-owned mutations and transactional routes remain authenticated.
- Current verified automated baseline: **2,355 passed, 0 failed, 0 skipped**.
- Customer tests: 1,641.
- Business tests: 688.
- Demo-data tests: 26.
- Release build: 0 warnings and 0 errors.
- Frontend-notes hosted smoke: **66 passed, 0 failed, 1 safely skipped**.
- Development deployment run `36688174618` deployed commit `a8626d2` and passed
  validation, migrations, publishing, and both API health checks.
- Hosted Demo seed run `36687741892` reconciled the canonical Demo dataset,
  complete Customer availability projections, and deterministic Development
  configuration.
- Global Business Vertical main categories, business-owned Service Category
  subcategories, precise availability validation errors, and 25-minute
  services on 30-minute start intervals are deployed and live-tested.

## Production-readiness ledger

Future agents must use this section when asked what remains for Production.
They should verify it against the current repository and hosted environment,
then update it as work is completed.

### Done

| Area | Status |
|---|---|
| Independent Customer and Business APIs/databases | Done |
| Trusted HTTPS and HTTP-to-HTTPS redirects | Done |
| Customer/Business JWT separation and internal HMAC | Done |
| Migration/deployment pipeline and previously released migrations | Done |
| Email/password and live email OTP | Done |
| Device authentication and device recovery | Done |
| Catalog, availability, drafts, pricing, booking confirmation, and status callbacks | Done |
| Lahza test-mode POC and security boundaries | Done for test mode |
| Production/Demo isolation and hosted frontend fixture | Done |
| Automated, relational, local HTTP, and hosted smoke coverage | Done |

The offering-presentation metadata slice is deployed and covered by unit,
TestServer, relational SQL Server, migration, Demo parity, Customer live HTTP,
and hosted Demo tests.
The category image/color presentation slice is deployed with
Business and Customer migrations, unit/TestServer/schema/Swagger/contract
coverage, catalog hash/version refresh coverage, and deterministic Demo parity.
The catalog favourites/search/top-ranking slice is deployed with
partitioned Customer-owned favourites, idempotent authenticated mutations,
anonymous/authenticated favourite and rating projections, bilingual normalized
business-name search, deterministic top-5/10 ranking, migration/schema,
Swagger/TestServer, service/repository, Demo fixture, and regression coverage.
The final coverage includes matching/mismatched JWT-device mutation partitions,
PUT/DELETE non-disclosure parity, category/offering projection assertions,
per-service batched projection dependency checks, search-before-top ordering,
and relational Demo favourite reconciliation/cleanup/reseed behavior. The
advisory availability-discovery slice is deployed with a Customer
public search route, one batched Customer-to-Business HMAC request,
Business-authoritative scheduling/capacity evaluation, deterministic
nearest-slot ranking, optional-device partition selection, explicit upstream
failure mapping, and preservation of the offering-aware detailed-slots
authority. Its final gate passed the Customer, Business, and DemoData suites
(2,338 tests), Release build with no warnings or errors, both disposable API
health checks, 31/31 HTTP harness self-tests, 12/12 live-local availability
scenarios, and 78/78 traced frontend-note scenarios.
The completed-booking customer review slice is deployed with
Customer-owned partitioned persistence, optimistic concurrency, public
privacy-safe pagination and aggregates, Demo fixtures, Swagger, TestServer,
SQL Server relational, executable up/down migration, concurrent HTTP
create/update/delete/update-vs-delete conflict handling, ownership
non-disclosure, authentication/device boundaries, localization, masking,
paging, catalog-resolution, aggregate, and partition-isolation coverage. The
banner slice is deployed with Customer-owned partitioned
persistence, required credential-free absolute HTTPS image URLs, deterministic
public active-only reads, Customer Admin CRUD, optimistic SQL rowversion
concurrency, localized problems, Swagger, Demo fixtures, executable migration,
and unit/TestServer/relational coverage. Final banner QA additionally covers
disposable migration up/down verification, SQL-backed CRUD and three race
shapes, request/body boundaries, optional-device states, partition
non-disclosure, safe induced failures, and deterministic Demo reconciliation.
The comprehensive hosted smoke verified authentication boundaries, public
Demo-only behavior, vehicle CRUD, catalog metadata, search/top, favourites,
reviews, banners, advisory and authoritative availability, direct pricing,
and Business owner reads. Production has no active customer configuration by
product decision; the Demo seeder now reconciles a deterministic Development
configuration returned by the credential-free configuration endpoint.
The complete Step 17 local gate passed on 2026-09-22, including all 50 active
provider-neutral Step 17 scenarios, 1,167 inherited scenarios, Step 16
database/schema isolation, adverse-schema checks, and final process/database
cleanup. The eight retired Stripe scenarios remain preserved as historical
coverage and are intentionally excluded from execution after the Lahza
migration.

Deployment workflow run `35740412333` applied both migrations, deployed both
APIs, and passed its health checks on commit `ce669c7`. Hosted Demo seed run
`35742511947` then reconciled the deterministic Demo partition. Hosted
verification passed for all 5 businesses and 20 offerings: fresh catalogs,
Arabic and Hebrew qualifiers, Hebrew-to-Arabic fallback, `MostRequested`,
null metadata fixtures, list/detail parity, Customer and Business Demo login,
deterministic Demo-only source IDs, and both deployed Swagger contracts.

Latest frontend-notes deployment run
[`36400551972`](https://github.com/mohanadHaji/GhseeliApis/actions/runs/36400551972)
deployed commit `7a859d2`. Hosted Demo seed run
[`36401665318`](https://github.com/mohanadHaji/GhseeliApis/actions/runs/36401665318)
reconciled configuration, reviews, add-on defaults, and favourites. The final
62-check hosted suite passed 61, failed 0, and safely skipped only banner Admin
mutation because the fixture has no Demo Customer Admin.

### Required before a real public production launch

These are release blockers unless the product scope is explicitly reduced:

| Requirement | What remains |
|---|---|
| Real-money payments | Replace/confirm test credentials with approved production Lahza credentials; repeat provider certification; verify real webhook delivery; add refund operations, reconciliation, settlement monitoring, alerting, and support runbooks |
| Customer booking history | Add authenticated booking list/detail/status endpoints so customers can reopen the app and retrieve current bookings |
| Business operations API | Add authorized work-order queue/list/detail/search/pagination APIs if the Business frontend is part of the launch |
| Operational readiness | Document and verify database backup/restore, deployment rollback, monitoring, alerting, log retention, incident response, and secret/key rotation |
| Security release review | Perform a final production configuration review and independent security assessment of authentication, authorization, payments, HMAC, rate limits, uploaded configuration, and public routes |
| Production acceptance | Run a final end-to-end acceptance pass with release configuration and approved non-fabricated test accounts/data |

### Conditional requirements

These become blockers only when the chosen frontend/product requires them:

| Feature | Current state | Required when |
|---|---|---|
| SMS OTP | Not implemented; email OTP works | The product requires phone-number authentication or SMS recovery |
| FCM push delivery | Tokens are stored; sending is not implemented | Booking/payment status must arrive as push notifications |
| Browser CORS | No CORS policy | A browser SPA calls the APIs directly instead of using a same-origin BFF |
| OAuth | Existing redirect flow is unapproved | Google/Facebook login is included at launch |
| Wallet/Cash/ThirdParty payments | Disabled | Any of these methods are included in product scope |
| Separate Development/Staging hosting | Two additional databases are now available, but the environment split is deferred; see [`DEV_ENVIRONMENT_FOLLOWUP.md`](DEV_ENVIRONMENT_FOLLOWUP.md) | Before moving seeded Demo data out of Production and enabling isolated pre-production promotion |
| Remote Demo cleanup | Intentionally unavailable | Hosted Demo data must be removed before separate test infrastructure exists |

### Optional later improvements

- Customer and business analytics/reporting.
- Wallet and notification domains.
- Automated Development-to-Staging-to-Production promotion.
- Performance/load dashboards and long-running soak environments.
- Administrative support portals and self-service operational tooling.

## Completed capabilities

### Customer API

- Email/password registration and login.
- Six-digit email OTP authentication.
- Rotating refresh tokens with reuse detection and family revocation.
- Device registration, opaque token hashing, rotation, expiry, ownership, and
  authenticated recovery after a lost/reformatted phone.
- Optional FCM token registration and updating.
- Customer profile, vehicle, and address management, including stable vehicle
  type strings and optional HTTPS image snapshots propagated through checkout,
  booking, reservations, and work orders.
- Arabic/Hebrew localization and stable Problem Details.
- Customer catalog read model synchronized from Business.
- Company, branch, category, offering, add-on, and available-slot browsing.
- Device-owned checkout drafts.
- Authoritative repricing and immutable price snapshots.
- Authenticated booking confirmation.
- Completed-booking customer reviews and public partition-safe review pages
  with masked customer names and rating aggregates.
- Partition-safe business favourites, bilingual business-name search, top-5/10
  rating ranking, and favourite/rating projections across catalog list, detail,
  and offering contexts.
- Signed booking-status callback processing and reconciliation.
- Lahza hosted checkout initialization, verification, webhook processing,
  idempotency, and refund-event convergence.
- Production/Demo request partitioning.
- Explicit optional-device public discovery for configuration, all current
  public catalog routes (including detailed slots and reviews), direct
  stateless pricing, and public banners. Anonymous requests select Production;
  supplied device tokens are validated strictly; Customer JWTs may select the
  account partition; dual credentials must match.

### Business API

- Independent Owner, Employee, and Admin identity system.
- Company and branch management.
- Business vertical assignments.
- Arabic-required and optional Hebrew catalog management.
- Categories, offerings, add-on groups, and choices.
- Branch schedules, date overrides, service areas, capacity, and slot
  validation.
- Appointment reservations and work-order creation.
- Work-order status transitions.
- Durable booking-status outbox, retries, dead-letter handling, requeue
  history, and signed Customer callbacks.
- Production/Demo request partitioning.

### Integration and operations

- Neutral contracts in `Ghseeli.IntegrationContracts`.
- Separate HMAC secrets and canonical signing in each API direction.
- Nonces, timestamps, replay prevention, and internal idempotency.
- Independent migrations and clean Customer/Business initial schemas.
- Manual GitHub Actions deployment with tests, migrations, Web Deploy, and
  database health checks.
- Guarded hosted Demo seeding with exact confirmation.
- Deterministic cleanup for local Demo databases.
- Frontend Demo handoff and canonical JSON fixture.
- Global Business Vertical main categories with business-owned Service
  Category subcategories. Only the canonical Car Washing vertical is currently
  seeded.
- Deterministic reconciliation of complete Customer branch-availability
  snapshots so repeated Development seeding repairs stale projections without
  duplicates.

## Known gaps and future improvements

These items are not implemented or are intentionally restricted. Do not infer
that they exist from old models, plans, or comments.

### Authentication and communication

- **SMS OTP is not implemented.** OTP delivery currently uses email only.
- **Push notification delivery is not implemented.** FCM tokens are stored and
  rotated, but the backend does not currently send FCM notifications.
- OAuth controller routes exist for Google/Facebook, but the redirect contract
  is not approved for frontend use. Return URLs require strict allowlisting,
  and bearer tokens must not be returned in redirect query strings.
- Demo identities are tested through password and email OTP, not OAuth.
- Add account abuse controls beyond the existing API rate limits if public
  launch risk requires CAPTCHA, reputation, or fraud checks.

### Customer product surface

- There is no public customer-authenticated booking list/detail endpoint.
  Booking confirmation returns the initial snapshot, while later status is
  maintained internally. A customer booking-history/status API is still
  needed for a complete app experience.
- Wallet, wallet transactions, and notification domains have no active runtime
  or database owner.
- `Wallet`, `CashOnArrival`, and `ThirdParty` payment methods are intentionally
  disabled with stable reason codes.
- There is no customer-facing refund-initiation workflow. Current payment code
  safely processes provider refund events.
- Only Car Washing is currently seeded as a global main category. Additional
  verticals require real catalog, availability, pricing, and fixture behavior
  before the frontend should expose them.

### Business product surface

- Business work-order list and detail endpoints are not implemented. The
  current public surface supports transitions when the caller already knows a
  permitted work-order ID.
- A complete business operations dashboard therefore needs authorized queue,
  search, filtering, pagination, and detail APIs.
- Business reporting, analytics, settlement, payout, and reconciliation
  dashboards are not implemented.

### Frontend and platform

- Neither API currently enables browser CORS. Native mobile clients can call
  the APIs directly; browser applications need a same-origin BFF/reverse proxy
  or a tightly allowlisted CORS policy with documented CSRF behavior.
- There is no separately hosted Development environment because the current
  free MonsterASP allocation is exhausted. Frontend Demo records share the
  hosted databases but remain isolated by `IsDemo`.
- Remote Demo cleanup is intentionally unavailable. Add it only with stronger
  approvals, exact manifest checks, backups, and target verification.
- Production deployment is manual rather than automatically promoted through
  Development, Staging, and Production environments.

### Payments

- Lahza is configured as a **test-mode proof of concept**, not approved for
  real-money production charging.
- Provider initialization, verification, hosted checkout, and Production
  security smoke tests passed.
- Some provider-controlled decline/issuer simulations were historically
  inconsistent. Do not claim every card-network outcome without fresh
  provider evidence.
- A complete provider-originated webhook test tied to a safe owned Production
  booking should be rerun when an appropriate non-fabricated fixture exists.
- Before real-money launch, add operational refund initiation, reconciliation,
  support tooling, settlement monitoring, alerting, and key-rotation runbooks.

## Testing completed

- Unit tests for controllers, handlers/services, validators, repositories, and
  state transitions.
- TestServer coverage for authentication, authorization, model binding,
  localization, Swagger security, request limits, and Problem Details.
- Relational SQL Server tests for migrations, constraints, concurrency,
  transactions, idempotency, outbox leasing, and cleanup.
- Clean and populated migration upgrade/downgrade coverage.
- Manifest-driven local HTTP tests for device registration, configuration,
  catalog, checkout, pricing, booking, status integration, payments,
  localization, schema separation, and available slots.
- Real Lahza test-mode initialization, hosted checkout, and verification.
- Hosted Customer and Business health/security smoke tests.
- All 17 Demo partition scenarios passed locally.
- Safe hosted Demo tests passed for authentication, OTP, catalog, refresh,
  ownership, unsigned partition rejection, payment suppression, and
  idempotent seeding.

## Testing improvements still valuable

- Add end-to-end mobile UI tests against the hosted Demo environment.
- Add browser/BFF tests after an approved CORS or same-origin architecture is
  selected.
- Add load, soak, and failure-injection tests for catalog refresh, reservation
  capacity, outbox delivery, and payment webhooks.
- Add real FCM delivery tests after push notifications are implemented.
- Add SMS provider contract, delivery, retry, abuse, and cost-control tests if
  SMS OTP is implemented.
- Add hardened OAuth end-to-end tests only after the redirect/token contract is
  redesigned.
- Add public booking-history and business work-order-query HTTP plans before
  implementing those endpoints.
- Repeat live payment-provider certification before enabling real money.

## Demo environment

Frontend documentation:

- `demo-data/FRONTEND_DEMO_HANDOFF.md`
- `demo-data/frontend-demo-data.json`

Quick credentials:

```text
Customer: maya.demo@example.test
Business: owner.sparkle@example.test
Password: Demo123!
OTP: 111111
```

Never accept `isDemo`, `dataPartition`, or an equivalent public client flag.
The partition must continue to come only from trusted seeded devices/accounts,
JWT claims, or HMAC-signed internal requests.

## Required engineering process

For every meaningful behavior change:

1. Define observable behavior and failure cases.
2. Update the applicable HTTP plan before implementation.
3. Write tests first and verify the expected failure.
4. Implement the smallest correct behavior.
5. Run targeted and affected regression tests.
6. Run required local/live HTTP scenarios.
7. Run the complete tests and Release build.
8. Review partitioning, ownership, side effects, security, and API boundaries.
9. Update documentation and sanitized evidence.

Do not weaken tests to fit an implementation.

## Important files

| File | Purpose |
|---|---|
| `../README.md` | Service overview, database tables, relationships, configuration, and deployment |
| `.github/copilot-instructions.md` | Repository-wide implementation rules |
| `API_BOUNDARIES.md` | Detailed ownership, routes, authentication, and integration invariants |
| `HTTP_TEST_PLAN_STANDARD.md` | Required HTTP planning and evidence standard |
| `docs/FRONTEND_AI_INTEGRATION_GUIDE.md` | Frontend contract and UX expectations |
| `demo-data/FRONTEND_DEMO_HANDOFF.md` | Hosted Demo usage |
| `Ghseeli.CustomerApi/Persistence/ApplicationDbContext.cs` | Customer model and partition filters |
| `Ghseeli.BusinessApi/Persistence/BusinessDbContext.cs` | Business model and partition filters |
| `.github/workflows/deploy-monsterasp.yml` | Production deployment |
| `.github/workflows/seed-hosted-demo.yml` | Guarded hosted Demo seeding |

## Commands

Run from this directory:

```powershell
dotnet build
dotnet test
dotnet run --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet run --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
```

Never commit secrets, tokens, credentials, local overrides, generated HTTP
artifacts, or connection strings.
