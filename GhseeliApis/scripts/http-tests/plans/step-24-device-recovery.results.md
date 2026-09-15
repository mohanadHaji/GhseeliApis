# Step 24 authenticated customer device recovery results

- Execution date: 2026-09-15 (local)
- Environment: disposable LocalDB and independently hosted Customer API HTTPS
- Scenario count: 10
- Passed: 10
- Failed: 0
- Deferred: 0
- Focused automated coverage: 25 passed, 0 failed
- Customer API project: 1,308 passed, 0 failed, 0 skipped
- Business API project: 584 passed, 0 failed, 0 skipped
- Complete sequential total: 1,892 passed, 0 failed, 0 skipped
- Release build: 0 warnings, 0 errors
- Local migration: all six Customer migrations applied from empty successfully
- Live HTTPS command:
  - `powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Invoke-HttpTests.ps1 -ManifestPath .\scripts\http-tests\plans\step-24-device-recovery.manifest.json -BaseUrl https://localhost:62878 -ResultsPath .\scripts\http-tests\artifacts\step-24-device-recovery.results.json`
- Live HTTPS result: 10 passed, 0 failed
- Result artifact: local only at
  `.\scripts\http-tests\artifacts\step-24-device-recovery.results.json`
- Cleanup: the dedicated `Ghseeli_Step24_DeviceRecovery_Live` LocalDB database
  was dropped after execution.
- Security: JWTs, refresh tokens, device tokens, and authorization headers were
  redacted by the harness and are not committed.
- Production deployment and smoke: pending.
