# Frontend API Notes HTTP Test Results

Date: 2026-09-30

## Code Test Plan

- Run the Customer, Business, and DemoData test projects serially.
- Build the solution in Release configuration.
- Independently audit vehicle/category presentation, favourites, reviews,
  banners, optional-device access, availability discovery, persistence,
  migrations, partitioning, localization, HMAC security, Swagger, deployment
  configuration, and scenario traceability.

## HTTP Test Plan

- HTTP required: Yes - the work changes public and internal routes, DTOs,
  validation, authentication, partition selection, failure mapping, Swagger,
  and HMAC transport behavior.
- Scenario source: `FRONTEND_API_NOTES_HTTP_TEST_PLAN.md`.
- Live harness:
  `scripts/http-tests/plans/frontend-api-notes.manifest.json`.
- Execution level: TestServer plus live local HTTP against disposable Customer
  and Business SQL Server databases, followed by the safe hosted smoke suite
  against the deployed Customer and Business APIs.
- Production execution: read-only Production checks plus reversible Demo
  mutations were performed. No fabricated Production records were created.

## HTTP Test Results

| Validation | Result |
|---|---|
| Customer tests | 1,641 passed, 0 failed, 0 skipped |
| Business tests | 688 passed, 0 failed, 0 skipped |
| DemoData tests | 26 passed, 0 failed, 0 skipped |
| Total automated tests | 2,355 passed, 0 failed, 0 skipped |
| Release build | Passed, 0 warnings, 0 errors |
| Fresh Customer disposable database | Migrated, seeded, HTTP health 200 |
| Fresh Business disposable database | Migrated, seeded, HTTP health 200 |
| HTTP harness self-tests | 31 passed, 0 failed |
| Availability live-local manifest | 12 passed, 0 failed |
| Taxonomy live-local extension | 12 passed, 0 failed |
| Scoped availability/optional scenario tests | 76 passed, 0 failed |
| Frontend-note scenario traceability | 78/78 mapped, no semantic mismatch |
| Taxonomy deployment | Final run `36688174618`, commit `a8626d2`, passed |
| Taxonomy hosted Demo reconciliation | Run `36687741892`, passed |
| Taxonomy hosted frontend-notes smoke | Final post-deployment run: 66 passed, 0 failed, 1 safely skipped |
| Cleanup | Disposable Customer and Business LocalDB databases and temporary state removed |

The live availability set covered valid HMAC discovery, missing
authentication, stale timestamp, replay prime/rejection, operation denial,
HTTP transport rejection, both Swagger documents, and independent Customer
and Business database health. The public advisory response includes both
`configuredCapacity` and `remainingCapacity`; the offering-aware detailed-slot
operation remains authoritative and can return different current capacity.

The taxonomy extension additionally covered the public Car Washing
main-category list, vertical-filtered businesses, business-owned subcategories,
the offering hierarchy, main-category and subcategory availability,
field-specific malformed-time and invalid-vertical errors, true
vertical/subcategory mismatch, authoritative 25-minute slots on 30-minute start
intervals, and anonymous direct pricing with the same 25-minute duration.

Fresh disposable databases were migrated through
`AddBusinessVerticalPresentationMetadata` and
`AddCustomerBusinessVerticalProjection`. The deterministic seeder populated
both databases, then repaired a deliberately stale Customer branch
availability projection idempotently without changing stable fixture IDs or
creating duplicates.

The hosted suite covered database health, Swagger, Demo authentication,
optional-device handling, catalog metadata, search/top ranking, favourites,
reviews, banners, advisory availability, authoritative detailed slots, direct
pricing, vehicle CRUD, role separation, and Business owner reads.

The hosted environment is now intentionally treated as Development.
`DemoData:PublicApisOnly=true` makes configuration, direct pricing, and public
catalog discovery/reviews/availability always select seeded Demo data and
ignore supplied device/JWT credentials. Live checks verified anonymous,
malformed-device, invalid-bearer, and valid-bearer/device calls. Customer-owned
favourites/reviews, vehicles, checkout, bookings, and payments remain protected.

The only skipped hosted scenario was banner Admin mutation because the
canonical fixture intentionally contains no Demo Customer Admin.

The first expanded hosted run found a smoke-only assumption that unfiltered
availability must expose the specific 25-minute fixture. The application paths
for seeded 25-minute authoritative slots and pricing both passed. The smoke was
corrected to price the first valid unfiltered offering while retaining the
dedicated exact 25-minute checks, then the complete 67-check suite was rerun
successfully.

Taxonomy Development deployment:
<https://github.com/mohanadHaji/GhseeliApis/actions/runs/36688174618>

Taxonomy hosted Demo seed:
<https://github.com/mohanadHaji/GhseeliApis/actions/runs/36687741892>

Previous Development deployment:
<https://github.com/mohanadHaji/GhseeliApis/actions/runs/36400551972>

Previous hosted Demo seed:
<https://github.com/mohanadHaji/GhseeliApis/actions/runs/36401665318>

No credentials, signatures, tokens, connection strings, PII, or raw harness
artifacts are included in this result.
