# HTTP test harness

PowerShell-first, manifest-driven HTTP API checks for local/test Ghseeli API runs.

## Files

- `Invoke-HttpTests.ps1` - entry point
- `HttpTestHarness.psm1` - reusable functions
- `Run-SelfTests.ps1` - no-Pester self-tests
- `plans\` - committed feature manifests and concise sanitized results summaries
- `sample.manifest.json` - public smoke + auth chaining sample

## Per-change workflow

- Keep human plan/result docs aligned with `HTTP_TEST_PLAN_STANDARD.md`.
- If the chosen HTTP execution level includes **live local/dev/test HTTP**, add a committed feature manifest under `.\scripts\http-tests\plans\`, for example `.\scripts\http-tests\plans\step6-internal-hmac.manifest.json`.
- If a change is **TestServer-only** or `HTTP required: No - <reason>`, do not create a live manifest just to satisfy process; record that decision in the plan/result docs instead.
- Keep per-machine URLs, IDs, secrets, and tokens in environment variables or `*.local.json` / `local.*.json` files only.
- Keep raw harness output under `.\scripts\http-tests\artifacts\` only; copy sanitized counts/evidence into `*_HTTP_TEST_RESULTS.md` instead of committing the JSON artifact.

## Safety

- Refuses non-local base URLs by default.
- Allowed without override: `localhost`, `127.0.0.1`, `::1`
- Use `-AllowNonLocal` only when you intentionally want a non-local target.
- No database commands are executed by this harness.

## Run

From the `GhseeliApis\` solution directory:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Invoke-HttpTests.ps1 `
  -ManifestPath .\scripts\http-tests\sample.manifest.json `
  -BaseUrl https://localhost:5001 `
  -Tags smoke
```

Using config + variables files:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Invoke-HttpTests.ps1 `
  -ManifestPath .\scripts\http-tests\sample.manifest.json `
  -ConfigPath .\scripts\http-tests\sample.local.json `
  -VariablesPath .\scripts\http-tests\sample.variables.local.json `
  -Feature auth
```

Self-tests:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Run-SelfTests.ps1
```

## Base URL and variables

Base URL can come from:

1. `-BaseUrl`
2. `baseUrl` in `-ConfigPath`
3. `HTTP_TEST_BASE_URL` environment variable

Non-secret variables can come from:

- `variables` inside `-ConfigPath`
- `-VariablesPath` JSON object
- `-Variables` hashtable
- `setupVariables` in the manifest

Secrets/tokens should only come from environment variables or runtime setup/extraction.
Local config/variables files should use a gitignored name such as `sample.local.json` or `sample.variables.local.json`.

Example config file shape:

```json
{
  "baseUrl": "https://localhost:5001",
  "variables": {
    "existingCompanyId": "11111111-1111-1111-1111-111111111111"
  },
  "sensitiveFields": [
    "otpCode"
  ]
}
```

## Template syntax

- `{{var:name}}` - scenario/runtime variable
- `{{env:NAME}}` - environment variable
- `{{gen:guid}}` - new GUID
- `{{gen:nonce}}` - 32-character lowercase nonce token
- `{{gen:timestamp}}` - UTC timestamp (`yyyyMMddHHmmssfff`)
- `{{gen:timestamp:yyyyMMdd}}` - UTC timestamp with format
- `{{gen:timestamp:yyyy-MM-dd:+1d}}` - UTC timestamp with format and relative offset
- `{{gen:iso8601}}` - UTC ISO-8601 timestamp
- `{{gen:iso8601:-10m}}` - UTC ISO-8601 timestamp with relative offset

If you need the same generated value more than once, assign it once in `setupVariables` or `setVariables`, then reference it with `{{var:name}}`.

## Manifest shape

```json
{
  "name": "sample",
  "sensitiveFields": ["password", "token"],
  "setupVariables": {
    "runId": "{{gen:guid}}",
    "email": "http-harness+{{var:runId}}@example.test"
  },
  "scenarios": [
    {
      "id": "health",
      "feature": "platform",
      "tags": ["smoke", "public"],
      "method": "GET",
      "url": "/api/health",
      "headers": {
        "Accept": "application/json"
      },
      "expect": {
        "status": 200,
        "contentType": "application/json",
        "headers": {
          "Content-Type": {
            "exists": true
          }
        },
        "json": [
          { "path": "$.status", "equals": "Healthy" },
          { "path": "$.service", "exists": true }
        ]
      }
    }
  ]
}
```

Supported scenario fields:

- `id`, `feature`, `tags`
- `method`, `url`, `headers`
- `jsonBody` or `bodyFile`
- `internalAuth` for runtime HMAC headers/signature using a secret environment variable
- `setVariables`
- `expect.status`
- `expect.contentType`
- `expect.headers`
- `expect.json` for JSON-path-like equality/presence checks
- `expect.errorCode` for stable error code assertions (default path `$.code`)
- `extract` for response JSON/header values used by later scenarios

`internalAuth` example:

```json
"internalAuth": {
  "serviceId": "{{env:GHSEELI_HTTP_TEST_INTERNAL_SERVICE_ID}}",
  "secretEnv": "GHSEELI_HTTP_TEST_INTERNAL_ACTIVE_SECRET",
  "timestamp": "{{gen:iso8601}}",
  "nonce": "{{gen:nonce}}"
}
```

The canonical HMAC payload is: signature version, service ID, uppercase method, normalized path/query, timestamp, nonce, the exact `Idempotency-Key` header value (or a deterministic empty line when the header is absent), then the SHA-256 body hash.

If the service, timestamp, nonce, or signature headers are already present in `headers`, the harness keeps the explicit value and only computes the missing pieces. This lets one manifest express both valid signed requests and deliberate negative cases such as stale timestamps, replayed nonces, bad signatures, and idempotency-key tampering.

## Assertions and extraction

- `204` automatically requires an empty body.
- `201` automatically requires a `Location` header.
- `extract` string values read JSON paths:

```json
"extract": {
  "authToken": "$.token",
  "userId": "$.userId"
}
```

- Header extraction is also supported:

```json
"extract": {
  "createdLocation": {
    "from": "header",
    "name": "Location"
  }
}
```

## Filtering

- `-Tags smoke` runs scenarios with any matching tag.
- `-Feature auth` runs scenarios with any matching feature.
- You can combine both filters.

## Results

Each run writes a JSON results file under `.\scripts\http-tests\artifacts\` unless `-ResultsPath` is supplied.

Results include:

- scenario id
- pass/fail
- status code
- duration
- assertion failures
- redacted request/response metadata

Sensitive headers (`Authorization`, signature/secret/token-like headers) and configured sensitive JSON fields are redacted.
Do not commit `artifacts\` results or `*.local.json` / `local.*.json` files.

## Notes

- `bodyFile` is resolved relative to the manifest directory when not absolute.
- Define dependent `setupVariables` / `setVariables` in the order they should resolve.
- The bundled sample manifest has public smoke tests and opt-in auth chaining examples. Run `-Tags smoke` if you only want non-auth public checks.
- The sample auth chain reuses the extracted token with `Authorization: Bearer {{var:authToken}}`; keep real secrets in environment variables, not in the manifest.
- The sample auth flow expects `GhseeliHarnessPassword` in the environment and creates a unique test user only when you run the `auth`-tagged scenarios.
