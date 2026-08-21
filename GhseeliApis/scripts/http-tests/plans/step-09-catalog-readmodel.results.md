# Step 9 catalog read model results

- Execution date: 2026-08-21 (local)
- Completion status: **passed**
- Environment: local-only, dedicated LocalDB databases, no production/remote access
- Business API localdb: `GhseeliBusiness_Step9Review_20260821_203949` on `(localdb)\MSSQLLocalDB`
- Customer API preseed localdb: `GhseeliCustomer_Step9Preseed_20260821_203949` on `(localdb)\MSSQLLocalDB`
- Customer API refresh localdb: `GhseeliCustomer_Step9Refresh_20260821_203949` on `(localdb)\MSSQLLocalDB`
- Business API URL: `https://localhost:50591`
- Customer API URL: `https://localhost:50592`
- Internal service credentials and JWT secrets: injected in-process for the run and never echoed
- Data stamp: `20260821-203949`
- Source company IDs:
  - provider 1: `b4614de0-6daf-46bd-a6d8-962fce0a2bbf`
  - provider 2: `ca8ab641-9020-4b18-acdb-45a61dd3ec99`
- Actual refresh local provider IDs:
  - provider 1: `4a0b31de-7a43-469a-8982-68ab0a904d69`
  - provider 2: `d7df2c65-e354-4377-80b6-0406f64b7602`
- Published upstream versions:
  - initial provider 1: `9`
  - initial provider 2: `9`
  - provider 1 after mutation: `10`

## Automated totals

- full solution: **891/891**
- focused Step 9 filter: **144/144**
- harness self-tests: **16/16**

## Manifest totals

- unique manifest scenarios: `21`
- live-passing scenarios: `21`
- live-failing scenarios: `0`

## Setup performed

- Migrated the dedicated Business API LocalDB with `dotnet ef database update`.
- Seeded two active Business companies, branches, service areas, categories, offerings, and add-ons directly into the dedicated Business database for deterministic local HTTP refresh coverage.
- Migrated the dedicated Customer preseed LocalDB and seeded the Step 9 read-model tables with stable public/local IDs for browse-contract coverage.
- Migrated a separate dedicated Customer refresh LocalDB with an empty read model for actual Business→Customer refresh coverage.
- Started Business API and Customer API locally over HTTPS, verified both swagger endpoints, and stopped the exact processes after execution.

## Executed live manifest runs

- Preseeded browse/auth contract run:
  - tags: `preseeded`
  - result: **18/18 passed**
  - artifact: `.\scripts\http-tests\artifacts\step-09-catalog-readmodel.preseeded.results.json`
- Initial Business→Customer refresh run:
  - tags: `blocked-initial-refresh`
  - result: **1/1 passed**
  - artifact: `.\scripts\http-tests\artifacts\step-09-catalog-readmodel.initial-refresh.results.json`
- Same-version unchanged refresh verification run:
  - tags: `unchanged`
  - result: **1/1 passed**
  - artifact: `.\scripts\http-tests\artifacts\step-09-catalog-readmodel.unchanged-refresh.results.json`
- Published version-change refresh run:
  - tags: `blocked-force-refresh`
  - result: **1/1 passed**
  - artifact: `.\scripts\http-tests\artifacts\step-09-catalog-readmodel.version-refresh.results.json`

## Covered live behavior

- Swagger contract for all five public catalog routes.
- Device issuance plus catalog auth failures for missing, malformed, and unknown `X-Device-Token`.
- Browse routes with preseeded contract data:
  - `GET /api/v1/catalog/businesses`
  - `GET /api/v1/catalog/categories`
  - `GET /api/v1/catalog/businesses/{id}`
  - `GET /api/v1/catalog/businesses/{id}/offerings`
  - `GET /api/v1/catalog/offerings/{id}`
- Localization and fallback coverage:
  - default Arabic
  - `Accept-Language: he`
  - explicit query override back to Arabic
  - unsupported header fallback to Arabic
  - Hebrew fallback to Arabic for provider-1 values with no Hebrew content
- Negative browse coverage:
  - unknown business `404 catalog_business_not_found`
  - unknown offering `404 catalog_offering_not_found`
  - cross-provider filter mismatch `400 catalog_filter_mismatch`
  - invalid query `language=en` with localized problem details and sanitized correlation ID
- Actual Business→Customer refresh coverage:
  - empty read model initial refresh populated both configured providers
  - same-version unchanged `refresh=true` advanced freshness metadata without changing provider IDs
  - published upstream provider-1 mutation refreshed name/version from `9` to `10` while preserving provider-1 local ID

## Review-fix outcomes now verified

- Empty safe defaults no longer break startup or controller/service resolution.
- Empty provider browse returns the intentional empty contract in automated integration coverage.
- Catalog refresh transactions now run under SQL Server retry execution strategy without the previous user-transaction failure.
- Provider synchronization is concurrency-safe under SQL Server relational tests using migrated dedicated LocalDB databases.
- Graph reads no longer use split queries; regression coverage measures a single relational select for the full provider graph.
- Lease handoff/contention paths are covered with stale/503 behavior tests and live refresh no longer leaks raw `InvalidOperationException` or `500`.
- Relational Step 9 tests migrate dedicated LocalDB databases with `Migrate()` and clean those exact database names in test support.

## Blockers

- None.

## Commands

- `dotnet test .\GhseeliApis.sln --filter "FullyQualifiedName~Catalog|FullyQualifiedName~AvailabilitySettings_UpsertThenGet_RoundTripsForOwnedBranch|FullyQualifiedName~CatalogVersion_|FullyQualifiedName~CreateOffering_WhenBasePriceExceedsOperationalLimit|FullyQualifiedName~CreateOffering_WhenValuesMatchExactBoundaries" --verbosity minimal`
- `dotnet test .\GhseeliApis.sln --verbosity minimal`
- `powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Run-SelfTests.ps1`
- `dotnet ef database update --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj --startup-project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj`
- `dotnet ef database update --project .\GhseeliApis\GhseeliApis.csproj --startup-project .\GhseeliApis\GhseeliApis.csproj`
- `Import-Module .\scripts\http-tests\HttpTestHarness.psm1; Invoke-HttpTestHarness -ManifestPath .\scripts\http-tests\plans\step-09-catalog-readmodel.manifest.json -BaseUrl https://localhost:50592 -Tags preseeded -ResultsPath .\scripts\http-tests\artifacts\step-09-catalog-readmodel.preseeded.results.json -Variables <local hashtable>`
- `Import-Module .\scripts\http-tests\HttpTestHarness.psm1; Invoke-HttpTestHarness -ManifestPath .\scripts\http-tests\plans\step-09-catalog-readmodel.manifest.json -BaseUrl https://localhost:50592 -Tags blocked-initial-refresh -ResultsPath .\scripts\http-tests\artifacts\step-09-catalog-readmodel.initial-refresh.results.json -Variables <local hashtable>`
- `Import-Module .\scripts\http-tests\HttpTestHarness.psm1; Invoke-HttpTestHarness -ManifestPath .\scripts\http-tests\plans\step-09-catalog-readmodel.manifest.json -BaseUrl https://localhost:50592 -Tags unchanged -ResultsPath .\scripts\http-tests\artifacts\step-09-catalog-readmodel.unchanged-refresh.results.json -Variables <local hashtable>`
- `Import-Module .\scripts\http-tests\HttpTestHarness.psm1; Invoke-HttpTestHarness -ManifestPath .\scripts\http-tests\plans\step-09-catalog-readmodel.manifest.json -BaseUrl https://localhost:50592 -Tags blocked-force-refresh -ResultsPath .\scripts\http-tests\artifacts\step-09-catalog-readmodel.version-refresh.results.json -Variables <local hashtable>`

## Notes

- The persisted live summary is local-only at `.\scripts\http-tests\artifacts\step-09-review-fixes.live-summary.json`.
- The legacy manifest tags `blocked-initial-refresh` and `blocked-force-refresh` were retained only for continuity with earlier artifacts; both now pass.
