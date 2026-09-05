# Step 21 Lahza Endpoint Gating HTTP Test Plan

Date: 2026-09-05

Status: Completed locally; production deployment pending

## Goal

Keep the deployed Lahza implementation and database migration intact while
making all Lahza-facing mutation and verification routes unavailable until the
payment release gate is complete.

## Configuration

`Lahza:EndpointsEnabled`

- defaults to `false` in Production;
- defaults to `true` outside Production so deterministic test environments
  remain usable;
- can be explicitly set to `false` in any environment;
- is injected as `false` by the production deployment workflow.

## Scenarios

| ID | Scenario | Expected result |
|---|---|---|
| `STEP21-LAHZA-GATE-001` | Disabled payment initialization route | `POST /api/v1/payments/intents` returns `404` |
| `STEP21-LAHZA-GATE-002` | Disabled payment verification route | `POST /api/v1/payments/{id}/verify` returns `404` |
| `STEP21-LAHZA-GATE-003` | Disabled webhook route | `POST /api/lahza/webhook` returns `404` |
| `STEP21-LAHZA-GATE-004` | Disabled Swagger document | The three disabled operations are absent |
| `STEP21-LAHZA-GATE-005` | Existing payment read route | `GET /api/v1/payments/{id}` remains mapped |
| `STEP21-LAHZA-GATE-006` | Enabled non-production test host | Existing Lahza deterministic HTTP scenarios remain available |
| `STEP21-LAHZA-GATE-007` | Production deployment configuration | Workflow injects `Lahza__EndpointsEnabled=false` |

## Execution level

- TestServer integration tests for route mapping and Swagger visibility.
- Existing Step 18 deterministic live-local HTTP suite with endpoints enabled.
- Static deployment-workflow assertion for the production-disabled value.

No production request is used as test evidence.

## Results

- Production-default gate tests: 2 passed, 0 failed.
- Deployment configuration tests: 2 passed, 0 failed.
- Complete solution: 1,847 passed, 0 failed, 0 skipped.
- Release build: 0 warnings, 0 errors.
- Existing Lahza-enabled disposable HTTP suite: 40 passed, 0 failed.
- Step 17, Step 18, and Step 20 asset validators passed.
