# Step 16 live-local HTTP/migration results

Status: **passed**

The complete live-local lifecycle passed on 2026-08-24. All runtime values
were disposable and local-only. The run created, exercised, verified, and
removed its own databases and credentials overlay.

## Commands

```powershell
.\scripts\http-tests\Test-Step16AssetValidator.ps1
.\scripts\http-tests\Test-Step16FixtureLifecycle.ps1
.\scripts\http-tests\Invoke-Step16LiveLocal.ps1 -Phase All -RunId <unique-hex> -Execute
```

`-Phase All` explicitly executes and records Customer-first migration,
Customer-first cleanup, Business-first migration, Business-first cleanup,
parallel migration, migration repeat, Customer reset, Business reset, the
combined reset sequence, runtime identity/device setup, independent Kestrel
startup/restart/concurrency, Production Swagger, missing/wrong connections,
unmigrated/wrong schemas, SQL outage/recovery, opposite-host outage, denied
SQL principals, missing-table recovery, exhaustive steady HTTP verification,
final schema verification, and cleanup. Sanitized per-phase evidence is written to the
ignored `artifacts\step16-lifecycle-<run>.local.json` file.

Individual lifecycle phases can be reproduced with `-Phase CustomerFirst`,
`BusinessFirst`, `Parallel`, `Repeat`, `ResetCustomer`, or `ResetBusiness`.
Every non-validation phase requires `-Execute`.

Live execution requires runtime-only environment values
`STEP16_CUSTOMER_PASSWORD`, `STEP16_BUSINESS_PASSWORD`,
`STEP16_DENIED_SQL_PASSWORD`, both directional HMAC service IDs/secrets, and
an ignored variables overlay containing `customerAdminJwt` and
`obsoleteCompanyJwt`. Generated user/device IDs and tokens are extracted into
the ignored run state; they are never committed.

## Results

- HTTP manifest entries: `790`
- Lifecycle entries: `4`
- Mapped frozen live-local scenarios: `76/76`
- Customer database: `disposable run-scoped Customer database; removed`
- Business database: `disposable run-scoped Business database; removed`
- Wrong-schema database: `disposable run-scoped isolation database; removed`
- Customer-first order: `passed at 2026-08-24T19:29:01Z`
- Business-first order: `passed at 2026-08-24T19:29:19Z`
- Parallel migration: `passed at 2026-08-24T19:29:37Z`
- Repeat migration twice: `passed at 2026-08-24T19:30:04Z`
- Customer reset/recreate: `passed at 2026-08-24T19:30:24Z`
- Business reset/recreate: `passed at 2026-08-24T19:30:44Z`
- Combined reset sequence: `passed at 2026-08-24T19:30:44Z`
- Independent startup/restart/outage/recovery: `passed`
- HTTP manifest: `790/0/790`
- Lifecycle evidence phases: `22/0/22`
- HMAC least privilege: `catalog-secret substitution rejected 401; ungranted
  catalog operation rejected 403; callback and reconciliation credentials
  separated`
- Dependency proof: `healthy reconciliation 404 BOOKING_NOT_FOUND; Business
  outage reconciliation 503 idempotency_unavailable`
- Schema/role/zero-seed verifier: `passed`
- HTTP harness self-tests: `29/0/29`
- Step 16 asset mutation tests: `6/0/6`
- Automated .NET tests: `1,643/0/1,643`
  (`1,117` Customer and `526` Business)
- Release build: `passed with 0 warnings and 0 errors`
- Cleanup: `processes 0; listeners 0; run databases 0; state and credential overlay removed`
- Deferred prerequisites: `none`

Never paste connection strings, database credentials, JWTs, device tokens,
HMAC/Stripe signatures, PII, raw logs, or local result artifacts here.
