# Frontend API Notes HTTP Test Results

Date: 2026-09-27

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
| Customer tests | 1,626 passed, 0 failed, 0 skipped |
| Business tests | 686 passed, 0 failed, 0 skipped |
| DemoData tests | 26 passed, 0 failed, 0 skipped |
| Total automated tests | 2,338 passed, 0 failed, 0 skipped |
| Release build | Passed, 0 warnings, 0 errors |
| Customer disposable database health | HTTP 200 |
| Business disposable database health | HTTP 200 |
| HTTP harness self-tests | 31 passed, 0 failed |
| Availability live-local manifest | 12 passed, 0 failed |
| Scoped availability/optional scenario tests | 76 passed, 0 failed |
| Frontend-note scenario traceability | 78/78 mapped, no semantic mismatch |
| Production deployment | Run `36318402420`, commit `f579428`, passed |
| Hosted Demo reconciliation | Run `36316366658`, passed |
| Hosted frontend-notes smoke | 56 passed, 0 failed, 7 skipped |
| Cleanup | 0 listeners, 0 disposable databases, 0 generated gate artifacts |

The live availability set covered valid HMAC discovery, missing
authentication, stale timestamp, replay prime/rejection, operation denial,
HTTP transport rejection, both Swagger documents, and independent Customer
and Business database health. The public advisory response includes both
`configuredCapacity` and `remainingCapacity`; the offering-aware detailed-slot
operation remains authoritative and can return different current capacity.

The hosted suite covered database health, Swagger, Demo authentication,
optional-device handling, catalog metadata, search/top ranking, favourites,
reviews, banners, advisory availability, authoritative detailed slots, direct
pricing, vehicle CRUD, role separation, and Business owner reads.

The seven hosted skips were intentional:

- five positive anonymous business detail/offering/review/pricing scenarios
  require a Production catalog fixture, but Production currently has no
  businesses;
- the cross-partition mismatch scenario requires a deterministic Production
  identity, which the canonical fixture intentionally does not provide;
- banner Admin mutation requires a Demo Customer Admin, which the canonical
  fixture intentionally does not provide.

No active global customer configuration is provisioned by product decision.
`GET /api/v1/configuration` therefore passed its intended contract by returning
localized `503 configuration_unavailable` with a not-configured-yet detail.

Deployment:
<https://github.com/mohanadHaji/GhseeliApis/actions/runs/36318402420>

Hosted Demo seed:
<https://github.com/mohanadHaji/GhseeliApis/actions/runs/36316366658>

No credentials, signatures, tokens, connection strings, PII, or raw harness
artifacts are included in this result.
