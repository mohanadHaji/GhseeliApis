# Step 23 - Customer OTP, Refresh Token, Booking, and Device Contract Plan

Date: 2026-09-14

Status: Done - deployed and live-tested

## Goal

Add email OTP authentication alongside the existing password flow, issue
one-time rotating refresh tokens, expose whether OTP confirmation created a
new user, rename the booking confirmation response field from `reference` to
`referenceId`, and accept an optional FCM token during device registration.

## Confirmed contract

- `POST /api/auth/otp/request` accepts an email address, generates a
  cryptographically random six-digit code, stores only a salted hash, expires
  it after five minutes, invalidates older unused codes for that email, sends
  it through configured SMTP, and returns the same accepted response whether
  the account exists or not.
- The OTP email subject and plain-text body contain Arabic and English only.
  Hebrew email content is deferred until explicitly reintroduced.
- `POST /api/auth/otp/confirm` accepts the same email and six-digit code. A
  valid unused code is scoped to that normalized email, can be consumed once,
  and signs in an existing active user or creates a minimal active `User`
  account. The response includes `isNewUser`, an access token, and a refresh
  token.
- OTP confirmation rejects malformed, expired, consumed, wrong-email, and
  repeatedly incorrect codes without exposing the valid code or account
  existence. Incorrect attempts are bounded.
- Existing password registration/login remain active and also issue refresh
  tokens.
- `POST /api/auth/refresh` accepts a refresh token, rotates it exactly once,
  and returns a new access/refresh pair. Tokens are random, stored only as
  hashes, expire after 30 days, and reuse revokes the token family.
- `POST /api/v1/bookings/from-draft` returns `referenceId`; the old
  `reference` JSON property is removed.
- `POST /api/v1/devices/register` accepts optional `fcmToken`. Blank values
  normalize to `null`; registration and rotation persist the latest value.

## Code Test Plan

- OTP generator and salted-hash verification tests.
- OTP request/confirmation service tests for scoping, expiry, one-time use,
  attempt limits, existing/new/inactive users, SMTP failure, and no code
  leakage.
- Refresh-token tests for issuance, hashed persistence, expiry, one-time
  rotation, family-reuse revocation, and inactive users.
- Device registration service and HTTP tests for optional FCM persistence,
  normalization, update, and length validation.
- Booking service and HTTP serialization regressions for `referenceId`.
- Relational model and migration checks for OTP, refresh-token, and FCM
  persistence.
- Swagger contract tests for all new and changed routes and schemas.

## HTTP Test Plan

- HTTP required: Yes.
- Why: this change adds authentication routes and changes request/response
  DTOs, serialization, persistence, security behavior, and Swagger.
- Execution level: automated TestServer plus mocked SMTP, followed by
  production HTTPS deployment and one user-approved live Gmail SMTP flow.

| Scenario ID | Request | Expected result |
|---|---|---|
| `STEP23-OTP-REQUEST-001` | Valid email to `POST /api/auth/otp/request` | `202`; code sent through SMTP and never returned |
| `STEP23-OTP-REQUEST-002` | Invalid email | `400` stable validation problem |
| `STEP23-OTP-REQUEST-003` | Repeated request | `202`; previous unused code becomes invalid |
| `STEP23-OTP-REQUEST-004` | SMTP unavailable | `503`; challenge is not left usable |
| `STEP23-OTP-CONFIRM-005` | Existing active user and valid code | `200`; `isNewUser=false` and token pair returned |
| `STEP23-OTP-CONFIRM-006` | Unknown email and valid code | `200`; minimal user created, `isNewUser=true`, token pair returned |
| `STEP23-OTP-CONFIRM-007` | Code issued for another email | `401`; no authentication |
| `STEP23-OTP-CONFIRM-008` | Expired, consumed, or incorrect code | `401`; stable error and no token leakage |
| `STEP23-OTP-CONFIRM-009` | Attempt limit reached | `429`; challenge cannot subsequently authenticate |
| `STEP23-REFRESH-010` | Valid refresh token | `200`; old token consumed and new pair returned |
| `STEP23-REFRESH-011` | Expired or unknown refresh token | `401`; no new tokens |
| `STEP23-REFRESH-012` | Reuse rotated refresh token | `401`; active family tokens revoked |
| `STEP23-REFRESH-013` | Refresh token for inactive user | `401`; no new tokens |
| `STEP23-BOOKING-014` | Successful booking from draft | `200`; contains `referenceId` and excludes `reference` |
| `STEP23-DEVICE-015` | Register with FCM token | `200`; token persisted and never echoed |
| `STEP23-DEVICE-016` | Register without/blank FCM token | `200`; persisted as `null` |
| `STEP23-DEVICE-017` | Rotate with a changed FCM token | `200`; latest token replaces prior value |
| `STEP23-DEVICE-018` | Oversized FCM token | `400`; no device mutation |
| `STEP23-SWAGGER-019` | Read Customer Swagger | New auth routes and changed DTO fields are documented |
| `STEP23-OTP-EMAIL-020` | Compose the SMTP OTP message | Subject and body contain Arabic and English, include the code and lifetime, and contain no Hebrew |

## Security and privacy requirements

- OTPs, refresh tokens, access tokens, SMTP credentials, and FCM tokens are
  never logged.
- OTP and refresh-token database rows contain hashes, not recoverable token
  values.
- OTP request responses do not reveal account existence.
- All successful token responses use `Cache-Control: no-store`.
- SMTP credentials are supplied only through user secrets/environment
  configuration.

## HTTP Test Results

- The original 19 stable Step 23 scenario IDs passed through automated unit,
  relational, controller, and TestServer coverage.
- Targeted OTP, refresh-token, FCM, booking serialization, schema, route, and
  Swagger regressions: 188 passed, 0 failed.
- Customer API project: 1,297 passed, 0 failed, 0 skipped.
- Business API project: 584 passed, 0 failed, 0 skipped.
- Complete solution: 1,881 passed, 0 failed, 0 skipped. The solution was run
  sequentially with `-m:1` to prevent the existing cross-project reflection
  test from racing the Business test build over the same assembly file.
- Release build: 0 warnings, 0 errors.
- Regression coverage verifies that failed initial refresh-token persistence
  rolls back password registration, failed access-token generation does not
  consume a refresh token, and concurrent OTP deliveries leave only the
  last-delivered code usable.
- Customer migration-from-empty, repeated migration, compiled model, exact
  route inventory, and Swagger ownership tests passed.
- Step 15 and Step 18 HTTP asset validators passed.
- The Production workflow is covered by a static regression test and injects
  Gmail SMTP settings only from `CUSTOMER_SMTP_USERNAME`,
  `CUSTOMER_SMTP_PASSWORD`, and `CUSTOMER_SMTP_FROM_ADDRESS` GitHub secrets.
- Production workflow run `34973642460` deployed commit `1ec1d99` successfully.
  Customer and Business database-backed HTTPS health checks returned `200`,
  and Customer Swagger exposed the OTP request, confirmation, and refresh
  routes.
- A user-approved live OTP request returned `202` with `accepted=true` and
  `Cache-Control: no-store`. Confirmation returned `200`, created a new user,
  issued a 60-minute access token and 30-day refresh token, and returned
  `isNewUser=true`. Refresh rotation returned `200`, changed the refresh token,
  and reuse of the old token returned `401`; all token responses were
  `no-store`.
- The first live email exposed a content defect: its subject and body contained
  Hebrew instead of English. Scenario `STEP23-OTP-EMAIL-020` requires Arabic
  and English only and rejects Hebrew Unicode characters.
- Production workflow run `34980075278` deployed the corrected template from
  commit `992d8cd`; validation, Customer deployment, Business deployment, and
  both database-backed HTTPS health checks passed.
- A second user-approved OTP request returned `202` with `accepted=true` and
  `Cache-Control: no-store`. The user verified that the delivered subject and
  body contain Arabic and English only, with no Hebrew. No OTP value was
  required or recorded for this content check.
