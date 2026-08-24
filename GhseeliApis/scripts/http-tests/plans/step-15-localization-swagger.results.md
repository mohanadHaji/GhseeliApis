# Step 15 Live-Local HTTP Results

Status: **passed — 2026-08-24**

Safety: execution used fresh, explicitly named, isolated LocalDB copies and
generated local-only JWT/HMAC/device values. Database names and ignored runtime
values are intentionally omitted. Stripe used fake local test-shaped values;
there were **0 Stripe network calls**, no production host, and no live key.

| Evidence | Planned | Passed | Failed | Deferred |
|---|---:|---:|---:|---:|
| Frozen scenario inventory/static mapping | 172 | 172 | 0 | 0 |
| Live-local scenario mappings | 42 | 42 | 0 | 0 |
| Manifest HTTP cases | 90 | 90 | 0 | 0 |
| Customer host cases | 52 | 52 | 0 | 0 |
| Business host cases | 38 | 38 | 0 | 0 |
| Harness/asset/fixture self-tests | 38 | 38 | 0 | 0 |
| Step 15 automated tests | 228 | 228 | 0 | 0 |
| Final invariant tests | 11 | 11 | 0 | 0 |
| Full Release automated tests | 1,672 | 1,672 | 0 | 0 |
| Clean Release API builds | 2 | 2 | 0 | 0 |
| Hardened database invariant gate | 1 | 1 | 0 | 0 |

## Static and automated evidence

- PowerShell AST parsing: **14/14 scripts passed**.
- `Test-Step15LiveAssets.ps1`: **172 frozen IDs**, **42/42 live
  mappings**, **90 manifest cases** (52 Customer, 38 Business), and **16**
  runtime variable names passed.
- Self-tests: harness **29/29**, asset validator **3/3**, fixture/verifier
  **6/6**; aggregate **38/38**.
- Step 15 tests: Customer **135/135**, Business **93/93**; aggregate
  **228/228**. The final invariant classes account for **11/11** of those
  tests (Customer 7; Business 4).
- `dotnet test .\GhseeliApis.sln -c Release --no-restore`: Customer
  **1,246/1,246**, Business **426/426**; total **1,672/1,672**.
- Two prior full-suite runs exposed a load-sensitive lease-test timing
  failure that did not reproduce in 10/10 isolated runs. The test still proves
  renewal beyond the original lease and exactly-once execution, but now uses
  a realistic five-second lease/load margin. The targeted regression and final
  parallel full-solution run passed. No behavioral assertion was weakened.
- Clean `Release` builds of `GhseeliApis.csproj` and
  `Ghseeli.BusinessApi.csproj`: **2/2**, each with **0 warnings, 0 errors**.
- Both independently owned DbContexts: **0 pending model changes**,
  **2/2 idempotent scripts generated**, and **4/4 repeated database updates**
  completed as no-ops.

## Live-local evidence

Six exact Kestrel processes were launched and readiness-checked on unused
ports: Customer Development HTTP/HTTPS and Production HTTPS, plus Business
Development HTTP/HTTPS and Production HTTPS. The 90-case manifest passed:

| Group | Passed |
|---|---:|
| Language | 8/8 |
| Problem details | 9/9 |
| Authentication/isolation/HMAC | 8/8 |
| Customer domain | 2/2 |
| Business domain | 2/2 |
| Legacy regression | 1/1 |
| Transport/environment | 26/26 |
| Swagger JSON/UI/environment | 15/15 |
| Security/cache/language/HSTS headers | 19/19 |

The run covered both Swagger inventories and UI assets, localization and
stable errors, correlation/cache/security headers, cross-host JWT rejection,
device and HMAC failures, HTTP transport rejection, domain regressions,
production/development HSTS behavior, and credential/PII leakage scans.

`Verify-Step15DatabaseInvariants.ps1` passed its hardened ownership/evidence
gate after all 90 requests. It verifies the expected Customer draft, booking,
payment and stable device security evidence plus Business company, work-order,
outbox, catalog, pricing and availability evidence. Expected device
`LastSeenAt`/row-version churn is allowed; unexpected identity/security or
Business-domain mutation fails closed.

## Defects found and fixed

- Made harness dynamic compilation portable and preserved ISO JSON timestamps
  as strings; added a regression self-test.
- Preserved Business Bearer challenges, hid raw HMAC header names, emitted the
  canonical Business `ProblemDetails` schema, and omitted `missingHeaders`
  outside the missing-header failure.
- Emitted invariant ISO health timestamps, added bodyless HEAD health support
  without changing Swagger inventory, and enabled explicit localhost HSTS
  verification.
- Reconciled manifest assertions with frozen Steps 3–14 text contracts and
  legitimate OpenAPI field/header names while retaining value-leakage checks.
- Split Business HTTPS and HTTP validation into separate exact processes and
  explicitly disabled the development insecure-HMAC override.

## Commands and cleanup

Commands were run from the solution directory:

```powershell
.\scripts\http-tests\Test-Step15LiveAssets.ps1
.\scripts\http-tests\Run-SelfTests.ps1
dotnet test .\GhseeliApis.sln -c Release --no-restore
dotnet clean <API project> -c Release
dotnet build <API project> -c Release --no-restore
dotnet ef migrations has-pending-model-changes <owned context options>
dotnet ef migrations script --idempotent <owned context options>
dotnet ef database update --connection <isolated-local-connection> <owned context options>
.\scripts\http-tests\Invoke-HttpTests.ps1 <ignored local runtime options>
.\scripts\http-tests\Verify-Step15DatabaseInvariants.ps1 <isolated fixture options>
```

All six exact host processes stopped. Only the two explicit Step 15 databases,
their backups, logs, generated SQL, ignored variables/results, and runtime
files were removed. Final proof: **0 databases, 0 Step 15 final-gate runtime
artifacts, and 0 listeners on ports 5010–5015**. Genuine deferrals: **none**.

The committed evidence is this sanitized aggregate record and the deterministic
plan/manifest/tests. Raw HTTP output, generated credentials, explicit database
names, logs, backups, and ignored local runtime files were never committed and
are intentionally unavailable after cleanup.
