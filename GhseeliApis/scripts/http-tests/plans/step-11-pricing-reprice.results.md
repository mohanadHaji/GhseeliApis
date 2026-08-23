# Step 11 pricing and repricing results

- Execution window (artifact UTC): 2026-08-21T22:49:11.9119214Z -> 2026-08-21T22:49:14.6884619Z
- Completion status: **done**
- Environment: local-only HTTPS, reused dedicated Step 10 LocalDB databases; no production or remote HTTP execution
- Business API URL: `https://localhost:50591`
- Customer API URL: `https://localhost:50592`
- Manifest: `.\scripts\http-tests\plans\step-11-pricing-reprice.manifest.json`
- Live artifact: `.\scripts\http-tests\artifacts\step-11-pricing-reprice.live.results.json`
- Raw artifacts: local/gitignored under `.\scripts\http-tests\artifacts\`
- Secrets: loaded only from user secrets or process environment and never recorded

## Scenario inventory

- Live manifest: **27 scenarios**
- Automated controller/service/TestServer/relational coverage: **63 focused tests**
- Direct route: auth, localization, malformed JSON, unsupported content type, 64 KB limit, authoritative totals, ignored forged money, stateless repeat, payment capabilities.
- Selection types: `MultipleChoice`, `SingleChoice`, `QuantityCounter`, `FixedIncludedChoice`, and `SegmentedSingleButtonChoice`.
- Draft route: auth, localization, malformed JSON, unsupported content type, 64 KB limit, persistence/re-read, invalidation, replacement, ownership parity, missing/malformed order GUID, stale versions.
- Automated-only deterministic coverage: stale catalog refresh once; Business transport/contract failures without extra retries; Business validation mapping; expiry; true concurrent repricing; deterministic fee/tax rounding; numeric overflow; configured Stripe capability.

## Commands and current evidence

- `dotnet test .\GhseeliApis.Tests\GhseeliApis.Tests.csproj --filter "FullyQualifiedName~PricingControllerTests|FullyQualifiedName~CheckoutPricingServiceTests|FullyQualifiedName~CheckoutDraftApiIntegrationTests|FullyQualifiedName~CheckoutDraftRelationalIntegrationTests" --verbosity minimal`
  - **passed: 63/63**
- `powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Run-SelfTests.ps1`
  - **passed: 17/17**
- `powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Invoke-HttpTests.ps1 -ManifestPath .\scripts\http-tests\plans\step-11-pricing-reprice.manifest.json -BaseUrl https://localhost:50592 -ResultsPath .\scripts\http-tests\artifacts\step-11-pricing-reprice.live.results.json`
  - **27 total: 27 passed, 0 failed**
  - duration: **2776.54 ms**
  - raw artifact size: **158607 bytes**
  - Both exact server processes were stopped after the run.

## Exact live seed prerequisites

- LocalDB instance: `(localdb)\MSSQLLocalDB`
- Business database: `GhseeliBusiness_Step10_20260821_232758`
- Customer database: `GhseeliCustomer_Step10_20260821_232758`
- Customer database includes migration `20260821220615_AddCheckoutDraftPricingSnapshots`.
- Business company/source ID: `11111111-1111-4111-8111-111111111111`
- Branch/source ID: `22222222-2222-4222-8222-222222222222`
- Customer provider ID: `8ff62298-a6fe-4ea1-a6f2-a5045f33a269`
- Business and Customer catalog version: `10`
- Branch coordinates: `32.1, 34.8`; active branch-coordinate service area radius: `12.5 km`
- Availability: `UTC`, minimum lead `3641` minutes, horizon `30` days, daily `08:00-18:00`, `30`-minute slots, capacity `4`
- Manifest slots: `2026-08-24T12:00:00Z` and `2026-08-24T13:00:00Z`
- Pricing configuration: currency `ILS`, tax `0%`, fee mode `None`, flat fee `0`, percentage fee `0`
- Stripe keys remain unconfigured, so `CreditCard` is disabled with `payment_method_provider_unavailable`; the other methods use `payment_method_not_yet_supported`.

Seeded pricing values used by the assertions:

| Selection type | Offering | Base | Selected/default choice | Adjustment | Duration |
|---|---|---:|---|---:|---:|
| MultipleChoice | `44444444-4444-4444-8444-444444444444` | 79.50 | `44444444-4444-4444-b444-444444444444` | 4.00 | 30 |
| SingleChoice | `55555555-5555-4555-8555-555555555555` | 55.00 | `55555555-5555-4555-b555-555555555555` | 2.00 | 30 |
| QuantityCounter | `66666666-6666-4666-8666-666666666666` | 65.00 | `66666666-6666-4666-a666-666666666666` x2 | 2.00 | 30 |
| FixedIncludedChoice | `77777777-7777-4777-8777-777777777777` | 45.00 | `77777777-7777-4777-a777-777777777777` default x1 | 0.00 | 30 |
| SegmentedSingleButtonChoice | `88888888-8888-4888-8888-888888888888` | 60.00 | `88888888-8888-4888-b888-888888888888` | 3.00 | 30 |

- Default direct/draft quote: base/grand total `79.50`, duration `30`.
- All-selection quote: base `304.50`, add-ons `11.00`, grand total `315.50`, duration `150`.

## Coverage notes

- Direct and draft equal totals are asserted by extracting the direct authoritative total and requiring the persisted draft quote to equal it.
- Direct repeat calls assert no `orderGuid` or `version`; TestServer additionally verifies zero draft/snapshot rows.
- Draft repricing increments one public version, stores the quote, survives GET re-read, and a later intent update removes pricing and restores `requiresReprice=true`.
- Wrong-owner and missing drafts intentionally return the same `checkout_draft_not_found` contract.
- Expiry and true simultaneous writer races remain automated because a live wall clock and sequential manifest runner cannot execute them deterministically.
- Stale-catalog refresh and Business failure/retry behavior remain automated because inducing them requires controlled Business client responses; the live environment must not mask those failures.
- Overflow remains automated because the live catalog cannot safely store an out-of-range SQL `decimal(18,2)` value.
- Non-zero service fee, non-zero tax, and fractional rounding remain automated-only. The live run intentionally uses the safe checked-in zero-fee/zero-tax configuration and does not mutate billing configuration or secrets.
- The manifest now verifies supplied `X-Correlation-Id` round-trip on representative success responses and every pricing error response, including matching `correlationId` in Problem Details.
- Direct pricing verifies `pricing.catalogVersion` exists before extracting it.
- Malformed JSON uses the dedicated committed-safe `step-11-malformed.request.txt` fixture.
- Both persisted-draft GET scenarios assert `Cache-Control: no-store`.

Key deterministic evidence:

- `RepriceAsync_WhenBusinessReportsStaleCatalogVersion_RefreshesOnceAndSucceeds`
- `RepriceAsync_WhenBusinessRemainsStale_RefreshesOnlyOnceAndFailsUnavailable`
- `RepriceAsync_WhenBusinessTransportOrContractFails_MapsStableUnavailableWithoutAdditionalRetries`
- `RepriceAsync_WhenBusinessValidationFails_MapsStableCustomerProblems`
- `RepriceAsync_WhenConfiguredTaxAndFee_RoundsDeterministically`
- `RepriceAsync_WhenAuthoritativeMoneyExceedsSupportedPrecision_ThrowsStableUnavailable`
- `RepriceDraftAsync_WhenDraftExpiresDuringBusinessPricing_ReturnsGoneAndPersistsNothing`
- `CheckoutDraftPricing_WhenConcurrentRepricesTargetSameDraft_OneWinsAndSnapshotPersistsOnce`
- `CheckoutDraftPricing_WhenRepricedTwice_ReplacesSnapshotWithoutDuplicateRows`

## Live result

- All scenario IDs `STEP11-SWAGGER-001` through `STEP11-DRAFT-REPRICE-AGAIN-027` passed.
- Previously observed Problem Details media-type/body failures, 64 KB response failures, and fixed-included default provenance now pass without weakening expectations.
- No remaining product defects or blockers were discovered.
