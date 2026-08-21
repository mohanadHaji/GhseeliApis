# Step 7 customer device registration results

- Execution date: 2026-08-21 (local)
- Environment: local
- Scenario count: 10
- Passed: 10
- Failed: 0
- Deferred: 0
- Automated test totals: 781/781 solution tests passed; 29/29 focused device tests passed; 16/16 harness self-tests passed; 10/10 live HTTPS scenarios passed
- Deferred rationale: none
- Command:
  - `powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Invoke-HttpTests.ps1 -ManifestPath .\scripts\http-tests\plans\step-07-device-registration.manifest.json -BaseUrl https://localhost:7168 -ResultsPath .\scripts\http-tests\artifacts\step-07-device-registration.results.json`
- Final manifest window: started `2026-08-21T15:34:18Z`, completed `2026-08-21T15:34:20Z`
- Result artifact: local only at `.\scripts\http-tests\artifacts\step-07-device-registration.results.json`
- Notes: the harness redacted issued and rotated tokens plus `X-Device-Token` headers; the migration was applied only to `GhseeliCustomer_Dev` localdb.
