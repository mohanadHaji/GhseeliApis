# Step 10 checkout drafts results

- Execution window (local): 2026-08-21T23:27:58.3492727+03:00 -> 2026-08-21T23:35:00.1042475+03:00
- Live manifest window (artifact UTC): 2026-08-21T20:33:47.6764368Z -> 2026-08-21T20:33:49.1555207Z
- Completion status: **done**
- Environment: local-only, fresh dedicated LocalDB databases, no production/remote access
- Business API LocalDB: `GhseeliBusiness_Step10_20260821_232758` on `(localdb)\MSSQLLocalDB`
- Customer API LocalDB: `GhseeliCustomer_Step10_20260821_232758` on `(localdb)\MSSQLLocalDB`
- Business API URL: `https://localhost:50591`
- Customer API URL: `https://localhost:50592`
- Manifest file: `.\scripts\http-tests\plans\step-10-checkout-drafts.manifest.json`
- Live artifact: `.\scripts\http-tests\artifacts\step-10-checkout-drafts.live.results.json`
- Internal HMAC credentials and app secrets: loaded from user secrets in-process only; never echoed
- Stopped exact shells after execution: `step10-business-final`, `step10-customer-final`
- Port verification after stop: `50591` free, `50592` free

## Manifest-only fixes applied

- Kept the prior live-contract fix for `STEP10-METHOD-014`: send a valid `X-Device-Token` so device middleware authenticates the matched `/api/v1` route before ASP.NET returns `405`.
- Kept the prior field-error path fixes:
  - `STEP10-DUPLICATE-SELECTIONS-024` now asserts `$.fieldErrors.items[0]` and `$.fieldErrors['items[0].selections'][0]`.
  - `STEP10-FOREIGN-ADDON-026` now asserts `$.fieldErrors['items[0].selections'][0]`.
- Restored the authored seeded source IDs required by the live contract:
  - `businessSourceId`
  - `branchSourceId`
  - `offeringSourceId`
  - duplicate/foreign add-on source IDs mapped to actual seeded catalog data instead of undefined/random placeholders.
- Accepted the actual serialized UTC response form for slot round-trip checks (`+00:00` as well as `Z`).
- Re-saved the manifest as UTF-8 with BOM so the Windows PowerShell 5.1 harness preserves authored Arabic/Hebrew expectations instead of mojibake.

## Automated validation

- `dotnet build .\GhseeliApis.sln --verbosity minimal`
  - result: **succeeded**
  - compiler totals: **0 errors**, **0 warnings**
- Automated totals reconfirmed at `2026-08-21T23:35:00.1042475+03:00`
  - full solution: **960/960**
    - `Ghseeli.BusinessApi.Tests`: **212/212**
    - `GhseeliApis.Tests`: **748/748**
  - focused checkout-draft tests (`FullyQualifiedName~CheckoutDraft`): **67/67**
  - HTTP harness self-tests: **16/16**

## Fresh setup performed

- Started `MSSQLLocalDB` explicitly, then migrated both fresh timestamped databases with explicit environment overrides only:
  - Business via `ConnectionStrings__BusinessConnection`
  - Customer via `ConnectionStrings__RemoteTest`
- Seeded deterministic active Business data:
  - one active company
  - one active branch
  - one active service area
  - one active availability settings record
  - seven active recurring schedules
  - one active category
  - five active offerings covering:
    - `MultipleChoice`
    - `SingleChoice`
    - `QuantityCounter`
    - `FixedIncludedChoice`
    - `SegmentedSingleButtonChoice`
- Published source IDs used by the live setup:
  - company: `11111111-1111-4111-8111-111111111111`
  - branch: `22222222-2222-4222-8222-222222222222`
  - default offering: `44444444-4444-4444-8444-444444444444`
  - fixed-included offering: `77777777-7777-4777-8777-777777777777`
  - fixed-included choice: `77777777-7777-4777-a777-777777777777`

## Snapshot and catalog refresh evidence

- Pre-Customer Business internal snapshot check succeeded over live HMAC-authenticated HTTPS:
  - contract version: `v1`
  - catalog version: `10`
  - branch ID: `22222222-2222-4222-8222-222222222222`
  - branch availability: **non-null**
  - minimum lead minutes: `3641`
  - booking horizon days: `30`
  - recurring schedules published: `7`
- Started Customer HTTPS with explicit `BusinessApiClient` credentials/base URL and one configured `CatalogReadModel` provider.
- Issued two pre-manifest device tokens, refreshed catalog live, then verified persistence in the Customer read model:
  - refreshed provider local ID: `8ff62298-a6fe-4ea1-a6f2-a5045f33a269`
  - refreshed business count: `1`
  - persisted branch rows for the provider: `1`
  - persisted branches with non-null availability snapshot: `1`
  - persisted catalog version: `10`
- The Step 10 manifest then issued/rotated additional device tokens as part of the authored 28-scenario flow.

## Live manifest execution

- artifact: `.\scripts\http-tests\artifacts\step-10-checkout-drafts.live.results.json`
- artifact timestamp: `2026-08-21T23:33:49.1736411+03:00`
- artifact size: `132153` bytes
- totals: **28 total**, **28 passed**, **0 failed**
- Covered live categories:
  - docs
  - auth / device-token enforcement
  - create / read / update lifecycle
  - ownership parity
  - version conflict
  - localization
  - correlation ID replacement
  - routing / method handling
  - malformed JSON
  - unsupported content type
  - boundary vehicle/location payloads
  - service-area enforcement
  - slot lead-time / horizon / alignment validation
  - duplicate service / duplicate add-on validation
  - foreign branch / foreign add-on validation
  - forged quoted price omission / catalog version round-trip

### Passed live scenarios

- `STEP10-SWAGGER-001`
- `STEP10-DEVICE-ISSUE-002`
- `STEP10-DEVICE-ROTATE-003`
- `STEP10-DEVICE-ISSUE-SECOND-004`
- `STEP10-CREATE-DEFAULT-005`
- `STEP10-MISSING-TOKEN-006`
- `STEP10-MALFORMED-TOKEN-007`
- `STEP10-UNKNOWN-TOKEN-008`
- `STEP10-OLD-TOKEN-009`
- `STEP10-READ-HE-010`
- `STEP10-UPDATE-AR-011`
- `STEP10-CONFLICT-012`
- `STEP10-INVALID-LANGUAGE-013`
- `STEP10-METHOD-014`
- `STEP10-MALFORMED-ORDERGUID-015`
- `STEP10-MISSING-ORDERGUID-016`
- `STEP10-WRONG-DEVICE-GET-017`
- `STEP10-WRONG-DEVICE-PUT-018`
- `STEP10-BOUNDARY-019`
- `STEP10-OUT-OF-SERVICE-020`
- `STEP10-LEAD-TIME-021`
- `STEP10-HORIZON-022`
- `STEP10-MISALIGNED-023`
- `STEP10-DUPLICATE-SELECTIONS-024`
- `STEP10-FOREIGN-BRANCH-025`
- `STEP10-FOREIGN-ADDON-026`
- `STEP10-MALFORMED-JSON-027`
- `STEP10-UNSUPPORTED-CONTENT-TYPE-028`

## Fixed-included / null-element confirmation

- Live manifest confirmation:
  - `STEP10-CREATE-DEFAULT-005`, `STEP10-READ-HE-010`, and `STEP10-BOUNDARY-019` all passed with `selections[0]` absent for the default offering, confirming empty-selection round-trip without null placeholder elements.
- Additional ad hoc live confirmation (same fresh environment, after the manifest):
  - created a draft for fixed-included offering `77777777-7777-4777-8777-777777777777` while omitting selections
  - create response selection count: `1`
  - read response selection count: `1`
  - authoritative fixed choice `77777777-7777-4777-a777-777777777777` was applied automatically on create and round-tripped on read
- Automated-only rationale that still remains intentional:
  - exact expiry timing is wall-clock sensitive and not safely deterministic in a live local run
  - true concurrent writer races for `PUT` are not safely deterministic live
  - those remain covered by the passing automated suites, including the expired-draft and concurrent-writer checkout-draft tests

## Redaction / artifacts

- The live artifact kept request/response evidence but redacted:
  - device tokens
  - `X-Device-Token`
  - HMAC signatures / secrets / auth-sensitive headers
- No secrets or signatures were printed during setup or execution.

## Blockers

- None. Required live scenarios passed.
