# Step 14 server-authoritative payment HTTP results

Execution date: 2026-08-23
Environment: a fresh, explicitly named SQL Server LocalDB Customer database,
cloned from a clean safe local source and migrated before execution. The exact
Customer API host ran with `--no-launch-profile` on one dynamically selected,
unused localhost HTTPS port. Configured and deliberately unconfigured host
processes were stopped after their groups. Secrets, tokens, database names,
runtime substitutions, logs, backups, and raw results remained in ignored local
artifacts and are not reproduced here.

## Step 14 completion gate

**The safe non-network gate passed. Scenario
`STEP14-INTENT-REAL-STRIPE-057` is explicitly scheduled for Roadmap Step 18,
the final pre-deployment release gate. Step 14 may complete and Steps 15–17 may
continue, but production deployment remains blocked until scenario 057
passes.**

- Plan: **146 stable scenario IDs**.
- Committed manifest: **130 unique entries representing 117 IDs**. Repeated
  entries under an ID are intentional transport or localization variants.
- Automated-only plan coverage: **29 IDs** implemented in deterministic
  TestServer, relational, failpoint, or concurrency tests rather than padded
  manifest rows.
- Manifest disposition: **125 live-local passed + 4 TestServer-only transport
  entries passed in integration tests + 1 deferred real-Stripe entry = 130**.
- PowerShell parse checks: **4/4 passed**.
- Manifest static expansion/validation: **130/130 passed**.
- Hardened HTTP harness self-tests: **26/26 passed**.
- Fixture/verifier positive and negative self-tests: **7/7 passed**.
- Full Release tests: **1,442/1,442 passed**:
  - Customer: **1,110/1,110**.
  - Business: **332/332**.
- Customer Release build: **passed, 0 warnings / 0 errors**.
- EF pending-model check: **no pending model changes**.
- EF idempotent migration script: **generated, non-empty, then removed**.
- Fresh isolated database migration: **applied successfully**; repeat update
  reported the database already current.
- Safe live HTTPS: **125/125 passed**:
  - `live-common`: **85/85**.
  - `live-seeded`: **8/8**.
  - `live-signed`: **30/30**.
  - `live-unconfigured`: **2/2**.
- Final hardened invariants: **passed**. The isolated database retained exactly
  11 fixture payments, 2 scoped idempotency records (including scenario 053),
  and 23 signed-webhook receipts. It had zero active intent leases, money
  mismatches, duplicate booking payments, duplicate intent/provider/scoped
  identities, device-idempotency mismatches, payment/booking state mismatches,
  or invalid receipt identities. The legacy payment content and immutable
  booking snapshot were unchanged.

Scenario 057 was not attempted because valid local Stripe test-network
credentials were unavailable. No Stripe network request was made. All other
groups are independent of 057. This is an accepted scheduling deferral, not a
waiver of the real-provider verification requirement.

## Reproducible command outline

Run from the solution directory. Values in angle brackets are local,
non-production placeholders; use ignored `*.local.json` artifacts and never
commit them.

```powershell
# Static/script/harness gates
$files = @(
  '.\scripts\http-tests\HttpTestHarness.psm1',
  '.\scripts\http-tests\Initialize-Step14Fixtures.ps1',
  '.\scripts\http-tests\Verify-Step14DatabaseInvariants.ps1',
  '.\scripts\http-tests\Test-Step14InvariantHardening.ps1')
$files | ForEach-Object {
  [void][scriptblock]::Create((Get-Content -LiteralPath $_ -Raw))
}
.\scripts\http-tests\Run-SelfTests.ps1
.\scripts\http-tests\Test-Step14InvariantHardening.ps1

# Automated, compiler, and EF gates
dotnet test .\GhseeliApis.sln -c Release --no-restore
dotnet build .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj -c Release --no-restore
dotnet ef migrations has-pending-model-changes `
  --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj `
  --startup-project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet ef migrations script --idempotent `
  --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj `
  --startup-project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj `
  --output .\scripts\http-tests\artifacts\step14-idempotent.local.sql
dotnet ef database update `
  --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj `
  --startup-project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet ef database update `
  --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj `
  --startup-project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj

# Initialize a fresh target whose name differs from the source.
.\scripts\http-tests\Initialize-Step14Fixtures.ps1 `
  -SourceCustomerDatabase '<clean-source>' `
  -CustomerDatabase '<fresh-Step14-target>' `
  -VariablesPath '.\scripts\http-tests\artifacts\step-14.variables.local.json'

# Launch the exact configured host with ASPNETCORE_URLS=https://127.0.0.1:<port>,
# the fresh target connection, ignored JWT values, and --no-launch-profile.
dotnet run --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj -c Release `
  --no-build --no-launch-profile

$baseUrl = 'https://127.0.0.1:<port>'
$manifest = '.\scripts\http-tests\artifacts\step-14-payment-rebuild.runtime.local.json'
$variables = '.\scripts\http-tests\artifacts\step-14.variables.local.json'
.\scripts\http-tests\Invoke-HttpTests.ps1 -ManifestPath $manifest `
  -VariablesPath $variables -BaseUrl $baseUrl -Tags live-common
.\scripts\http-tests\Invoke-HttpTests.ps1 -ManifestPath $manifest `
  -VariablesPath $variables -BaseUrl $baseUrl -Tags live-seeded
.\scripts\http-tests\Invoke-HttpTests.ps1 -ManifestPath $manifest `
  -VariablesPath $variables -BaseUrl $baseUrl -Tags live-signed

# Stop that exact process, launch the same target/port with empty Stripe
# configuration, then run the two fail-closed cases.
.\scripts\http-tests\Invoke-HttpTests.ps1 -ManifestPath $manifest `
  -VariablesPath $variables -BaseUrl $baseUrl -Tags live-unconfigured

# Stop the exact unconfigured process and verify durable state.
.\scripts\http-tests\Verify-Step14DatabaseInvariants.ps1 `
  -CustomerDatabase '<fresh-Step14-target>' `
  -SourceCustomerDatabase '<clean-source>' `
  -VariablesPath $variables -SignedWebhooksExecuted
```

The actual run also generated the signed webhook streams through the committed
local signer path selected by the runtime manifest. It did not use production
Stripe or the generic sender for those streaming/signature boundary cases.

## Findings repaired during final reconciliation

1. Scenario 001 now proves the create schema is limited to `bookingId` and
   `method`, Customer payment operations document Bearer security, the
   anonymous webhook does not, and legacy payment write routes are absent.
2. Scenarios 101 and 102 no longer claim unobserved provider or logger behavior.
   Scenario 102 has TestServer proof for missing, empty, CRLF, and overlong
   correlation values, generated safe replacements, unsafe-value absence, and
   no payment-service call.
3. Scenario 103 now captures the application logger. A red test exposed generic
   webhook exception details; production logging was narrowed to exception
   type, and sentinel SQL, Stripe key, webhook secret, and PII are proven absent
   from that captured path and the enumerated responses.
4. Scenario 083 uses an independent initially Completed
   payment/booking/charge fixture. Its receipt identity and
   `partial_refund_ignored` disposition are exact, while the payment remains
   Completed and the booking remains paid/Completed.
5. Scenario 057 naming is consistent. Scenarios 051–053 and 062 use seeded
   fixtures and have no dependency on it.
6. The manifest/plan distinction is explicit: 130 entries do not mean 130
   scenario IDs or 146 executable Kestrel requests.
7. A zero-byte `bodyFile` live regression was added to the harness self-tests
   and fixed without treating an explicitly empty body as no body.
8. The invariant verifier now recognizes scenario 053 against the correct
   existing-payment fixture; a verifier self-test protects the extra scoped
   idempotency row.

The source-target guard, exact Pending/Failed payment-to-booking mappings,
expected webhook identities, negative harness/verifier tests, and all valid
concurrent hardening changes were retained. Ignored raw evidence remains local
and untracked.
