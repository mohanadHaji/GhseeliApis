# Step 18 Lahza Payment Migration HTTP Results

Date: 2026-09-03

Status: **deterministic automated and safe-local gates passed; external Lahza
test mode and public HTTPS remain deferred**

The release scenario catalog has been expanded in
`STEP_18_LAHZA_EXHAUSTIVE_TEST_SCENARIOS.md` with **227 unique scenarios**.
Those additional external,
mobile, resilience, and provider-contract cases are planned evidence, not
retrospectively counted as passed by the 40-scenario deterministic run.

No real Lahza credentials, customer credentials, card data, or externally
reachable callback/webhook endpoint were used.

## Assets

- `scripts/http-tests/plans/step-18-lahza-payment-migration.manifest.json`
  contains 40 scenarios, including 19 Lahza webhook requests and 16 requests
  signed locally over the exact transmitted UTF-8 body.
- `Initialize-Step18Fixtures.ps1` copies only a named LocalDB source into an
  isolated run database, applies migrations, creates random local JWT/HMAC
  material and device tokens, and writes secrets only to ignored
  `artifacts/*.local.json`.
- `Invoke-Step18LiveLocal.ps1` requires `-Execute`, enables only the
  Development-only deterministic gateway, binds to loopback, never calls the
  Lahza API, runs configured and unconfigured phases separately, and drops the
  isolated database by default.
- `Verify-Step18DatabaseInvariants.ps1` verifies four logical payments, four
  idempotency records, eleven Lahza webhook receipts, exact replay
  deduplication, quarantine, partial-refund disposition, and atomic
  payment/booking convergence.
- The shared harness generates lowercase hexadecimal HMAC-SHA256 in
  `X-Lahza-Signature` from the exact raw UTF-8 request body. A `signBody`
  override supports deterministic raw-body-change rejection tests.

Historical Step 14 Stripe manifest/results remain unchanged. The active Step 17
runner and validator now exclude eight retained Stripe-era scenarios and the
Step 14 manifest from execution; Step 18 owns current payment HTTP execution.

## Validation executed

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\GhseeliApis\scripts\http-tests\Run-SelfTests.ps1
31/31 passed

powershell -NoProfile -ExecutionPolicy Bypass -File .\GhseeliApis\scripts\http-tests\Test-Step18LiveAssets.ps1
Passed: 40 scenarios, 19 Lahza webhooks, 16 locally signed webhooks

powershell -NoProfile -ExecutionPolicy Bypass -File .\GhseeliApis\scripts\http-tests\Test-Step17LiveAssets.ps1
Passed: 50 active provider-neutral entries, 8 retained historical Stripe entries,
1,165 inherited provider-neutral selections

powershell -NoProfile -ExecutionPolicy Bypass -File .\GhseeliApis\scripts\http-tests\Test-Step17LocalPrerequisites.ps1
Passed: generated once and safely reused
```

Live-local manifest result: **40 passed / 0 failed / 0 skipped**:

- 39 configured-provider scenarios passed.
- 1 deliberately unconfigured-provider scenario passed.
- Database verification passed with 4 payments, 4 idempotency records, and 11
  provider-scoped Lahza webhook receipts.
- No real Lahza endpoint, credential, or card was used.

The full solution regression passed **1,799/1,799** automated tests:
**1,242 Customer** and **557 Business**. The payment-focused regression passed
**194/194**, including relational concurrency and exact-byte webhook coverage.

The final migration passed clean creation, no-pending-model validation,
upgrade/downgrade round trips, and a populated pre-Lahza upgrade containing
legacy Stripe EUR, nullable reference, payment identifier, transaction
identifier, and webhook history rows.

## Scenarios not executable by the deterministic local gateway

The following remain automated/relational, real-test-mode, or public-HTTPS-only
and must not be represented as live-local successes:

- `STEP18-LAHZA-CONFIG-003` - non-HTTPS configuration startup validation.
- `STEP18-LAHZA-INIT-007` - concurrent initialization race.
- `STEP18-LAHZA-INIT-008` - invalid provider reference/checkout URL.
- `STEP18-LAHZA-INIT-009` - ambiguous transport acceptance.
- `STEP18-LAHZA-INIT-010` and `011` - ambiguous verification reconciliation.
- `STEP18-LAHZA-VERIFY-012`, `013`, `015`, and `016` - scripted successful,
  pending/failed, currency-mismatch, and reference-mismatch verification. The
  current Development deterministic gateway intentionally has one fixed verify
  response, which the local manifest uses only for safe mismatch behavior.
- `STEP18-LAHZA-MIGRATION-031` - passed through automated and direct populated
  LocalDB migration verification rather than the HTTP manifest.
- `STEP18-LAHZA-TEST-032` and `033` - real Lahza test-mode hosted checkout/card
  outcomes.
- `STEP18-LAHZA-HTTPS-034` - publicly trusted HTTPS webhook/callback activation.

Real test-mode and public HTTPS scenarios remain deferred. They require
separately supplied test secrets and hosting; no production endpoint may be
used.

## Exhaustive-suite execution started

Execution resumed on 2026-09-03 after the 227-scenario catalog was created.

- Lahza/payment-focused automated selection: **188 passed / 0 failed**.
- Complete solution regression: **1,799 passed / 0 failed**
  (**1,242 Customer**, **557 Business**).
- HTTP harness self-tests: **31 passed / 0 failed**.
- Step 18 manifest validation: **40 valid scenarios**, including
  **19 Lahza webhook** and **16 exact-body locally signed** requests.
- The live-local rerun was not counted as a new pass because no local database
  with the current post-reset Customer migration history was available as the
  runner's disposable source. The retained older databases use the retired
  migration history and correctly fail rather than being silently accepted.
- Real Lahza test-mode execution did not start because neither
  `LAHZA_SECRET_KEY` nor `Lahza:SecretKey` is configured.
- Public callback/webhook delivery remains blocked by the absence of trusted
  HTTPS.

During this execution, the standalone HTTP harness self-test exposed and fixed
an explicit .NET assembly-reference incompatibility in its embedded transport
capture handler. The corrected harness uses the runtime's default references
and passes all 31 tests.

## Real Lahza test-mode execution

Real test-mode execution started on 2026-09-03 using a temporary credential
stored only in .NET user secrets. No credential or card value was written to
the repository or results.

| Scenario | Result | Evidence |
|---|---|---|
| `STEP18-EXT-REAL-001` | Passed | ILS initialization returned an HTTPS `checkout.lahza.io` URL; immediate verification matched the exact reference, amount, and currency. |
| `STEP18-EXT-REAL-002` | Passed | JOD initialization and verification matched the exact reference, 100 minor units, and JOD. |
| `STEP18-EXT-REAL-003` | Passed | USD initialization and verification matched the exact reference, 100 minor units, and USD. |
| `STEP18-EXT-REAL-004` | Blocked by observed provider behavior | The documented successful Visa reached Lahza's simulated issuer stage, but remained on “Waiting for response from your card issuer” for three minutes. Closing the browser caused verification to report `abandoned`; success was not claimed. |
| `STEP18-EXT-REAL-006` | Inconclusive | The documented insufficient-funds card reached the issuer-authentication prompt. Without a terminal issuer response, verification reported `abandoned`, not the documented failure. |
| `STEP18-EXT-REAL-007` | Inconclusive | The documented do-not-honour card reached the issuer-authentication prompt. Without a terminal issuer response, verification reported `abandoned`. |
| `STEP18-EXT-REAL-008` | Inconclusive | The documented authentication-failed card reached the issuer-authentication prompt. Without a terminal issuer response, verification reported `abandoned`. |
| `STEP18-EXT-REAL-009` | Inconclusive | An altered CVV was accepted through the initial hosted form and reached issuer authentication; no terminal invalid-CVV result was returned. |
| `STEP18-EXT-REAL-010` | Passed at hosted-form boundary | An altered expiry was rejected by the Lahza checkout with `Invalid card expiry`; no payment success occurred. |
| `STEP18-EXT-REAL-011` | Passed | Uncompleted checkouts verified as `abandoned` and never produced a successful transaction. |
| `STEP18-EXT-REAL-013` | Passed | Repeated JOD and USD verification calls returned stable reference and transaction status values. |
| `STEP18-EXT-REAL-014` | Passed for executed transactions | Lahza dashboard inspection confirmed the generated Ghseeli references, ILS/JOD/USD amounts, card channel, test account association, timestamps, and `abandoned` final status. No transaction reference or account value is retained in this file. |

Headless Chromium was rejected by the checkout firewall with HTTP 403.
Installed full Chrome in headed mode loaded the same checkout with HTTP 200,
so hosted-checkout automation must use a normal browser. The remaining card
outcomes require resolving or understanding Lahza's non-terminating simulated
issuer step. Public webhook, callback, retry, and refund cases remain blocked
by trusted HTTPS.

### Simulated issuer root cause

A headed-Chrome network trace identified the concrete failure after the
checkout's `continue` action:

- Lahza attempted to embed
  `https://lahza.io/public/test/cookie-support/start.html`.
- That Lahza-owned endpoint returned HTTP 403.
- The response also supplied `X-Frame-Options: sameorigin`.
- The browser therefore refused to display the `lahza.io` page inside the
  `checkout.lahza.io` frame and reported
  `net::ERR_BLOCKED_BY_RESPONSE`.
- The checkout remained on “Waiting for response from your card issuer”.
- Server verification and the Lahza dashboard then reported `abandoned`.

This is a Lahza test-checkout framing/configuration defect, not an application
amount, currency, reference, callback, or verification failure. Browser
settings and Ghseeli code cannot override a provider-owned HTTP 403 or
`X-Frame-Options` response. Successful and documented decline outcomes remain
blocked until Lahza corrects the test issuer endpoint or provides a supported
alternative.

The dashboard evidence independently confirms that the non-successful outcome
was recorded by Lahza itself rather than being inferred only from the local
browser automation or verification response.

A separate successful-card diagnostic supplied a valid HTTPS callback URL.
The checkout still terminated as `abandoned`, so the missing callback URL is
not the cause of the observed test-card behavior.

## Successful hosted-checkout retest

Execution resumed on 2026-09-07 with a fresh ILS 1.00 test transaction using
the same initialization contract as the Customer API.

| Check | Result |
|---|---|
| Initialization | Passed; Lahza returned an HTTPS `checkout.lahza.io` URL |
| Hosted checkout | Passed in an interactive browser using Lahza's documented successful Visa |
| Customer-visible result | `Payment Successful` for ILS 1.00 |
| Server verification | Passed with nested transaction status `success` |
| Money verification | Passed: 100 minor units and exact `ILS` currency |
| Identity verification | Passed: exact stored reference and a provider transaction ID |
| Repeated verification | Passed; the provider status and transaction identity remained stable |

The previously observed
`https://lahza.io/public/test/cookie-support/start.html` resource still
returned HTTP 403 with `X-Frame-Options: SAMEORIGIN` when probed directly.
It did not prevent this fresh interactive checkout from completing, so the
successful-card scenario is no longer blocked by that resource.

This retest validates Lahza initialization, interactive hosted checkout, and
owned server-side verification. It does not validate Ghseeli's public
callback or webhook delivery because no publicly trusted HTTPS Customer API
endpoint is currently available.

## Post-project-rename regression

After renaming the Customer projects to `Ghseeli.CustomerApi` and
`Ghseeli.CustomerApi.Tests`, the complete solution passed **1,847/1,847**
automated tests: **1,263 Customer** and **584 Business**.

The disposable local Lahza suite was also rerun:

- configured-provider scenarios: **39 passed / 0 failed**;
- unconfigured-provider scenario: **1 passed / 0 failed**;
- database invariants: **4 payments, 4 idempotency rows, 11 Lahza receipts**.

Production now defaults the Lahza create, verify, and webhook routes to
unmapped until `Lahza:EndpointsEnabled` is explicitly enabled.
