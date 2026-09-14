# Step 21 Lahza Endpoint Gating HTTP Test Plan

Date: 2026-09-14

Status: Done - test-mode POC deployed and production smoke passed

## Goal

Keep the deployed Lahza implementation and database migration intact while
releasing the Lahza-facing mutation, verification, and webhook routes for a
test-mode POC only after trusted HTTPS, test credentials, and the Lahza
test-mode webhook are configured.

## Configuration

`Lahza:EndpointsEnabled`

- defaults to `false` in Production unless explicitly enabled;
- defaults to `true` outside Production so deterministic test environments
  remain usable;
- can be explicitly set to `false` in any environment;
- is injected as `true` by the production deployment workflow for the approved
  test-mode POC.

The deployment must fail before publication when the Lahza secret is missing.
The secret remains runtime-only in GitHub Actions and must never appear in
retained artifacts, logs, source, or Swagger.

## Scenarios

| ID | Scenario | Expected result |
|---|---|---|
| `STEP21-LAHZA-GATE-001` | Disabled payment initialization route | `POST /api/v1/payments/intents` returns `404` |
| `STEP21-LAHZA-GATE-002` | Disabled payment verification route | `POST /api/v1/payments/{id}/verify` returns `404` |
| `STEP21-LAHZA-GATE-003` | Disabled webhook route | `POST /api/lahza/webhook` returns `404` |
| `STEP21-LAHZA-GATE-004` | Disabled Swagger document | The three disabled operations are absent |
| `STEP21-LAHZA-GATE-005` | Existing payment read route | `GET /api/v1/payments/{id}` remains mapped |
| `STEP21-LAHZA-GATE-006` | Enabled non-production test host | Existing Lahza deterministic HTTP scenarios remain available |
| `STEP21-LAHZA-GATE-007` | Production deployment configuration | Workflow injects `Lahza__EndpointsEnabled=true` for the Customer API |
| `STEP21-LAHZA-GATE-008` | Disabled routes with trailing slashes | Canonical and trailing-slash variants return the same localized `404 resource_not_found` problem |
| `STEP21-LAHZA-POC-009` | Explicit disabled override | A Production host with `Lahza:EndpointsEnabled=false` still hides all three operations |
| `STEP21-LAHZA-POC-010` | HTTPS deployment URLs | Runtime cross-API URLs and deployment health probes use HTTPS |
| `STEP21-LAHZA-POC-011` | Secret release gate | Customer deployment fails when `LAHZA_SECRET_KEY` is absent |
| `STEP21-LAHZA-POC-012` | Enabled Production Swagger | Initialization, verification, and webhook operations are present |
| `STEP21-LAHZA-POC-013` | Initialization auth boundary | Anonymous initialization is rejected without calling Lahza |
| `STEP21-LAHZA-POC-014` | Verification auth boundary | Anonymous verification is rejected without calling Lahza |
| `STEP21-LAHZA-POC-015` | Webhook signature boundary | Missing, malformed, or incorrect signatures are rejected without state mutation |
| `STEP21-LAHZA-POC-016` | Correctly signed webhook | Exact-body HMAC is accepted and duplicate delivery is idempotent |
| `STEP21-LAHZA-POC-017` | Local payment flow | Booking-owned initialization, hosted URL response, verification, and payment/booking convergence pass locally |
| `STEP21-LAHZA-POC-018` | Production route exposure | Deployed Swagger contains all three operations over HTTPS |
| `STEP21-LAHZA-POC-019` | Production security smoke | Anonymous initialization/verification and unsigned webhook calls are rejected safely |
| `STEP21-LAHZA-POC-020` | Lahza test API smoke | Initialization and repeated verification against Lahza test mode preserve reference, amount, currency, and provider status |
| `STEP21-LAHZA-POC-021` | Provider webhook delivery | A fresh hosted test payment produces a signed Lahza webhook accepted by the deployed Customer API and converges once |

## Execution level

- TestServer integration tests for route mapping and Swagger visibility.
- Existing Step 18 deterministic live-local HTTP suite with endpoints enabled.
- Static deployment-workflow assertions for enabled Customer-only POC
  configuration, HTTPS URLs, and the required secret.
- Post-deployment HTTPS Swagger and unauthenticated security smoke tests.
- Real Lahza test-mode initialization and verification without exposing
  credentials.
- Provider webhook delivery requires a fresh owned booking/payment and may be
  completed manually if no safe production fixture exists.

No production database fixture may be inserted merely to satisfy this plan.
If a fresh owned production booking is unavailable, scenario 021 remains
explicitly deferred rather than fabricating customer data.

## Results

- Production-default, explicit-disable, and enabled-environment gate tests
  passed.
- Deployment configuration and production smoke regression tests: 3 passed,
  0 failed.
- Complete solution: 1,853 passed, 0 failed, 0 skipped.
- Release build: 0 warnings, 0 errors.
- Existing Lahza-enabled disposable HTTP suite: 40 passed, 0 failed.
- Step 15 and Step 18 asset validators passed.
- POC release commit: `3258580`.
- Production smoke correction commit: `3c534d3`.
- Deployment workflow run:
  `34877519661`, succeeded.
- Customer and Business database migrations, publishes, Web Deploy operations,
  and HTTPS health checks passed.
- Production Swagger publishes payment initialization, verification, read, and
  Lahza webhook operations for the approved test-mode POC.
- The workflow and an independent post-deployment rerun verified Customer
  database health, Swagger publication, `401` rejection for anonymous payment
  initialization and verification, and `400` rejection with
  `lahza_signature_missing` for an unsigned webhook.
- A fresh provider checkout tied to an owned production booking remains
  deferred because no safe production fixture was available. No production
  customer or booking data was fabricated for this check.
