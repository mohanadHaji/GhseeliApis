# Step 6 secure integration results

- Execution date: 2026-08-21 (local)
- Environment: local
- Scenario count: 26
- Passed: 26
- Failed: 0
- Deferred: 0
- Automated test totals: 752/752 solution tests passed; 16/16 harness self-tests passed; 26/26 manifest scenarios passed
- Deferred rationale: none
- Commands:
  - `powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Run-SelfTests.ps1`
  - `powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Invoke-HttpTests.ps1 -ManifestPath .\scripts\http-tests\plans\step-06-secure-integration.manifest.json -BaseUrl https://localhost:7267 -ResultsPath .\scripts\http-tests\artifacts\step-06-secure-integration.results.json`
- Final manifest window: started `2026-08-20T21:17:05Z`, completed `2026-08-20T21:17:10Z`
- Result artifact: local only at `.\scripts\http-tests\artifacts\step-06-secure-integration.results.json`
- Notes: the harness signer now matches the application canonical request, including the deterministic `Idempotency-Key` line; local environment/user-secret values were loaded in-process without echoing secret values, and no tokens, secrets, or raw signatures are committed here.
