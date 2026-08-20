# HTTP Test Plan Standard

Purpose: define the repository standard for planning, executing, and recording HTTP-visible behavior changes in `GhseeliApis`, `Ghseeli.BusinessApi`, internal service routes, and webhooks. This complements `API_BOUNDARIES.md` and `.github/copilot-instructions.md`.

## 1. Mandatory close-out for every meaningful change

Every meaningful behavior change must end with a clear code/HTTP test plan in the step doc, PR description, or completion note.

Use this closing shape:

```md
## Code Test Plan
- Unit/repository/TestServer/live tests to add or run

## HTTP Test Plan
- HTTP required: Yes/No
- Why
- Scenario IDs
- Execution level: TestServer, live, or both

## HTTP Test Results
- <scenario-id> - <status> - <evidence>
```

If HTTP testing is not required, say so explicitly: `HTTP required: No - <reason>`.

## 2. When HTTP tests are required

HTTP scenarios are required when a change affects caller-observable HTTP behavior.

| Change type | HTTP scenario required? | Minimum expectation |
|---|---|---|
| New/changed route, verb, DTO, serialization, header, status code, or `ProblemDetails` shape | Yes | TestServer scenario; add live execution if host/network behavior matters |
| Authn/authz/ownership/policy/middleware changes | Yes | HTTP success + rejection coverage |
| Model binding, validation, localization, Swagger/OpenAPI contract changes | Yes | HTTP scenario for bound input/output plus contract verification |
| Idempotency, versioning, concurrency, callbacks, webhooks, internal-service HMAC | Yes | HTTP scenario for happy path and at least one conflict/rejection path |
| Bug reported from real HTTP usage | Yes | Add a regression HTTP scenario plus the cheapest lower-level reproduction |
| Pure refactor/internal algorithm change with no route/output/auth/side-effect change | Usually no | Unit/repository/TestServer-only as appropriate, plus a written reason HTTP is unchanged |
| Repository/query optimization | Only if caller-visible output changes | Lower-level tests unless filtering/order/status/paging/shape changes |

If unsure, add at least one TestServer HTTP scenario.

## 3. Test levels and when each is enough

| Level | Use it for | Examples in this repo | Not enough by itself when |
|---|---|---|---|
| Unit | Single class/rule with mocks or pure helpers | validators, handlers, auth helpers, canonical request hashing, error mapping | the caller-visible HTTP contract changed |
| Repository | EF Core mappings, queries, persisted idempotency/version behavior | nonce/idempotency stores, catalog queries, delete semantics | routing, headers, middleware, auth, serialization, or `ProblemDetails` changed |
| TestServer integration | Full ASP.NET Core pipeline in-process | controller routes, auth policies, model binding, correlation headers, `ProblemDetails`, JSON contract | behavior depends on real HTTPS, Kestrel, reverse proxy, external callbacks, or environment wiring |
| Live HTTP | Real HTTP over local/dev/test host | HTTPS/HMAC, Stripe webhook delivery, host config, request-size limits, proxy/header behavior | use this only in non-production; still keep lower-level automated coverage |

Rule: start with unit/repository coverage, add TestServer coverage for HTTP-visible behavior, and add live HTTP only when transport, HTTPS, host configuration, or external integration is part of the risk.

## 4. Required lifecycle

1. **Behavior** - Write the observable rule first: route, caller, success result, stable error codes, and side effects.
2. **Planned HTTP calls** - Add scenario rows before implementation: exact method, route, auth identity, headers, body, expected status/code, and assertions. If the selected execution level includes live local/dev/test HTTP, add or update the committed `scripts\http-tests\plans\<feature>.manifest.json` at the same time so docs and harness keep the same scenario IDs.
3. **Implementation** - Add/adjust the lowest useful automated tests first, then implement the minimum correct behavior.
4. **Execute** - Run the smallest targeted set that covers the change: unit, repository, TestServer, then live only when the chosen execution level or transport/host/integration risk requires it.
5. **Edge expansion** - Add scenarios discovered during implementation before declaring done.
6. **Completion evidence** - Record scenario IDs, command or test names, status, correlation IDs, and sanitized result excerpts. If the harness was used, keep the raw JSON artifact local and copy only sanitized counts/evidence into the committed results doc.

Do not mark work complete while required scenarios are still only planned.

## 5. Scenario schema

### Stable ID rules

- Every scenario gets a stable ID and keeps it across reruns.
- Never renumber old IDs; append new ones.
- Prefer `<FEATURE>-<STEP>-<SURFACE>-<CATEGORY>-<NNN>`, for example `STEP6-HMAC-APPOINTMENTS-VALIDATE-AUTH-001`.

### Required fields

| Field | Rule |
|---|---|
| `id` | Stable scenario ID |
| `featureStep` | Change label such as `STEP_5`, `STRIPE_STEP_7`, `INTERNAL_HMAC_STEP_6` |
| `route` | Concrete route template including important query keys |
| `method` | HTTP verb |
| `authIdentity` | `anonymous`, `user-jwt`, `company-jwt`, `admin-jwt`, `internal-hmac`, `stripe-signature`, etc. |
| `prerequisitesSetup` | Seed data, user/token acquisition, clock setup, HTTPS requirement, feature flags |
| `requestHeaders` | Only headers relevant to behavior; redact or placeholder secrets |
| `requestBody` | Exact request body or `none` |
| `expectedStatus` | Numeric HTTP status |
| `expectedStableErrorCode` | Use the stable code for failures; use `none` for success |
| `responseAssertions` | JSON fields, list membership, nullability, version values, or no-leakage checks |
| `headerAssertions` | Correlation ID, content type, localization, pagination, cache/version headers |
| `dataVersionSideEffects` | Created/updated/deleted rows, version bumps, idempotency records, no-duplicate guarantees |
| `cleanup` | `none` or exact cleanup/reset action |
| `automation` | `manual`, `automated-testserver`, or `automated-live` |
| `status` | `planned`, `automated`, `passed`, `failed`, or `deferred` |
| `resultEvidence` | Test name, command, timestamp, response excerpt, correlation ID, or results doc reference |
| `deferredRationale` | Mandatory when `status: deferred` |

### Status values

| Status | Meaning | Completion effect |
|---|---|---|
| `planned` | Designed but not yet automated or executed | Not done |
| `automated` | Wired into a repeatable test/harness but no current-run evidence recorded yet | Not done |
| `passed` | Executed for this change and assertions passed | Acceptable |
| `failed` | Executed and failed | Blocks completion |
| `deferred` | Intentionally not executed now | Blocks unless rationale, owner/unblock condition, and compensating coverage are recorded and the remaining risk is explicitly non-shipping |

## 6. Coverage categories

For each changed HTTP surface, mark every category as covered or not applicable with a reason. Do not silently skip categories.

- **Happy path** - at least one success case per changed route.
- **Malformed payload/model binding** - invalid JSON, wrong content type, missing route/query/body values, enum parsing, GUID/date parsing.
- **Authn/authz/ownership** - missing token, wrong role, wrong owner/company, anonymous vs protected route behavior.
- **Validation boundaries** - min/max length, required fields, numeric/date ranges, invalid state combinations, field-level errors.
- **State/version/idempotency/concurrency** - duplicate submissions, stale version, replayed callback, concurrent update, retry with same vs different body.
- **Failure/unavailability** - upstream API, Stripe, database, missing snapshot, timeouts, rejected dependencies.
- **Deletion/read-after-delete** - hard delete, soft delete, disable/archive, subsequent GET/list behavior.
- **Contract/Swagger** - route presence, verb, request/response schema, declared statuses, header parameters, intended inclusion/exclusion from Swagger.
- **Localization** - `Accept-Language`, explicit language override, stable error code across languages, Arabic default fallback.
- **Security/no leakage** - no stack traces, SQL, secrets, signatures, raw tokens, or PII in errors or captured evidence.

## 7. Execution rules

### Secrets and PII

- Never commit bearer tokens, JWTs, OAuth codes, Stripe secrets, webhook secrets, HMAC secrets, raw signatures, connection strings, or user-secret dumps.
- Use placeholders such as `<from-user-secrets>` or `<computed-at-runtime>`.
- Redact customer emails, phone numbers, addresses, license plates, and payment identifiers in committed evidence.

### Unique test data

- Scenario IDs stay stable; runtime data does not.
- Use unique emails, order GUIDs, idempotency keys, nonces, and entity names per run to avoid collisions.
- Only reuse data deliberately when testing duplicate or idempotent behavior.

### Deterministic clocks

- Automated tests should use fixed UTC timestamps or a controlled clock abstraction where feasible.
- Live tests should record a base UTC time and assert normalized invariants, not fragile `DateTime.Now` equality.
- HMAC, replay-window, and booking-slot scenarios must state the time assumption explicitly.

### Database isolation

- Prefer isolated `WebApplicationFactory`/TestServer state for automated HTTP tests.
- For live HTTP, use a dedicated local/dev/test database only.
- If a remote shared test database is unavoidable, prefix created data uniquely and clean it up.

### Safe cleanup

- Every mutating scenario must declare cleanup.
- Cleanup must be idempotent and narrower than setup.
- Prefer disposable test data over destructive cleanup of shared records.

### No production execution

- Never execute live HTTP scenarios against production.
- Stripe, OAuth, and HMAC live tests must use local/dev/test endpoints and test-mode credentials only.
- If an issue was seen only in production, reproduce it in a safe environment before closing it.

## 8. Definition of done

A meaningful change is done only when:

- the observable behavior is written down;
- lower-level tests appropriate to the change were added or updated;
- every required HTTP scenario is present with a stable ID;
- every required scenario is `passed`;
- no required scenario remains only `planned` or `automated`;
- no required scenario is `failed`;
- any `deferred` scenario has a mandatory rationale, unblock condition, and compensating coverage, and does not hide a must-ship risk;
- sanitized evidence is recorded.

Changing the expected result to match a broken implementation is not completion; update the behavior contract first if the requirement truly changed.

## 9. Regression rule for live HTTP bugs

Any bug found through live HTTP usage must add regression coverage before closure:

1. Record the failing live scenario with a stable ID.
2. Add the cheapest automated reproduction that proves the bug (`unit`, `repository`, or `TestServer`).
3. Add or keep an HTTP boundary regression scenario (`TestServer` minimum; `live` too if the bug is transport/config/external-integration specific).
4. Re-run the scenario after the fix and store sanitized evidence.

Manual retest alone is not enough for a recurring live HTTP bug.

## 10. Naming and retention

- Reuse the existing change prefix when possible:
  - `STEP_5_HTTP_TEST_PLAN.md`
  - `STEP_5_HTTP_TEST_RESULTS.md`
  - `STRIPE_STEP_7_HTTP_TEST_PLAN.md`
  - `INTERNAL_HMAC_STEP_6_HTTP_TEST_RESULTS.md`
- If a change already has a completion doc, end that document with `Code Test Plan`, `HTTP Test Plan`, and `HTTP Test Results` sections instead of scattering notes elsewhere.
- When live local/dev/test HTTP is part of the execution level, keep the committed harness input manifest under `scripts\http-tests\plans\` (for example `scripts\http-tests\plans\step6-internal-hmac.manifest.json`).
- Keep committed plan/results docs for shipped HTTP changes, security work, webhooks, internal-service contracts, and bug regressions.
- Raw harness output belongs under `scripts\http-tests\artifacts\results-*.json` and stays local/uncommitted; summarize it in `*_HTTP_TEST_RESULTS.md`.
- Local harness config/variables must use gitignored names such as `*.local.json` or `local.*.json` and stay uncommitted.
- If `HTTP required: No - <reason>`, do not create an empty manifest or results artifact.
- Keep evidence sanitized and concise in committed docs; large raw captures stay local and should be deleted after the summary is recorded.
- Never commit ephemeral credentials, raw tokens, raw signatures, or user-secret values.

## 11. Harness manifest schema (current)

Use a committed JSON manifest under `scripts\http-tests\plans\` only when the chosen execution level includes repeatable live local/dev/test HTTP. The harness manifest complements the human plan/results docs; it does not replace them.

Recommended per-change file set:

- `STEP_6_HTTP_TEST_PLAN.md` - human-readable scenarios, coverage categories, and status
- `scripts\http-tests\plans\step6-internal-hmac.manifest.json` - repeatable live local/dev/test harness input
- `STEP_6_HTTP_TEST_RESULTS.md` - committed sanitized execution summary
- `scripts\http-tests\artifacts\results-*.json` - local machine-readable run output only; never commit
- `scripts\http-tests\*.local.json` or `scripts\http-tests\local.*.json` - local-only base URLs, IDs, and secrets; never commit

If the plan says `Execution level: TestServer` only, or `HTTP required: No - <reason>`, do not create a live manifest just to satisfy process.

```json
{
  "name": "Internal HMAC Step 6",
  "sensitiveFields": ["signature"],
  "setupVariables": {
    "branchId": "{{var:existingBranchId}}",
    "offeringId": "{{var:existingOfferingId}}",
    "correlationId": "corr-step6-{{gen:timestamp:yyyyMMddHHmmss}}"
  },
  "scenarios": [
    {
      "id": "STEP6-HMAC-APPOINTMENTS-AUTH-001",
      "feature": "step6",
      "tags": ["auth", "negative", "live"],
      "method": "POST",
      "url": "/api/v1/internal/appointments/validate",
      "headers": {
        "Accept": "application/json",
        "Content-Type": "application/json",
        "X-Correlation-Id": "{{var:correlationId}}",
        "X-Ghseeli-Signature": "0000000000000000000000000000000000000000000000000000000000000001",
        "Idempotency-Key": "step6-validate-auth-001"
      },
      "internalAuth": {
        "serviceId": "{{env:GHSEELI_HTTP_TEST_INTERNAL_SERVICE_ID}}",
        "secretEnv": "GHSEELI_HTTP_TEST_INTERNAL_ACTIVE_SECRET",
        "timestamp": "2026-08-20T20:15:00.0000000+00:00",
        "nonce": "step6validate00000000000000000001"
      },
      "jsonBody": {
        "branchId": "{{var:branchId}}",
        "offeringId": "{{var:offeringId}}",
        "requestedSlotStartUtc": "2026-08-22T09:00:00Z",
        "currency": "ILS"
      },
      "expect": {
        "status": 401,
        "contentType": "application/problem+json",
        "headers": {
          "X-Correlation-Id": {
            "equals": "{{var:correlationId}}"
          }
        },
        "json": [
          { "path": "$.type", "exists": true },
          { "path": "$.detail", "exists": true }
        ],
        "errorCode": {
          "path": "$.code",
          "equals": "internal_auth_invalid_signature"
        }
      }
    }
  ]
}
```

Harness field mapping:

- `scenario.id` - stable scenario ID that must match the plan/results docs
- `scenario.feature` / `scenario.tags` - filterable groups for targeted runs
- `headers`, `jsonBody`, `bodyFile` - request inputs
- `internalAuth` - optional runtime HMAC signing block that computes the canonical signature from a secret environment variable using service ID, method, normalized path/query, timestamp, nonce, deterministic `Idempotency-Key` line, and body hash
- `expect.status`, `expect.contentType`, `expect.headers`, `expect.json`, `expect.errorCode` - assertions
- `extract` - response values chained into later scenarios
- `setupVariables`, `setVariables` - generated or shared runtime values

Keep real secrets in environment variables or local-only config files, never in the committed manifest.
