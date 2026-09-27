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
  and Business SQL Server databases.
- Production execution: prohibited and not performed.

## HTTP Test Results

| Validation | Result |
|---|---|
| Customer tests | 1,626 passed, 0 failed, 0 skipped |
| Business tests | 686 passed, 0 failed, 0 skipped |
| DemoData tests | 25 passed, 0 failed, 0 skipped |
| Total automated tests | 2,337 passed, 0 failed, 0 skipped |
| Release build | Passed, 0 warnings, 0 errors |
| Customer disposable database health | HTTP 200 |
| Business disposable database health | HTTP 200 |
| HTTP harness self-tests | 31 passed, 0 failed |
| Availability live-local manifest | 12 passed, 0 failed |
| Scoped availability/optional scenario tests | 76 passed, 0 failed |
| Frontend-note scenario traceability | 78/78 mapped, no semantic mismatch |
| Cleanup | 0 listeners, 0 disposable databases, 0 generated gate artifacts |

The live availability set covered valid HMAC discovery, missing
authentication, stale timestamp, replay prime/rejection, operation denial,
HTTP transport rejection, both Swagger documents, and independent Customer
and Business database health. The public advisory response includes both
`configuredCapacity` and `remainingCapacity`; the offering-aware detailed-slot
operation remains authoritative and can return different current capacity.

All live values were generated for disposable local fixtures. No credentials,
signatures, tokens, connection strings, PII, raw harness artifacts, Production
requests, or Production mutations are included in this result.
