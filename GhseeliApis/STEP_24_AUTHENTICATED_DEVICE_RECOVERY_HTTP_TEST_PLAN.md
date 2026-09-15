# Step 24 - Authenticated Customer Device Recovery

Date: 2026-09-15

Status: Implemented and passed locally - production deployment pending

## Goal

Allow a customer who authenticated through OTP or another Customer JWT flow to
register a new phone or recover an existing installation after its device token
was lost, while preventing one customer from taking over another customer's
device.

## Confirmed contract

- `POST /api/v1/devices/register` remains available anonymously for first-time
  device registration.
- A valid optional Customer Bearer token binds a newly registered device to the
  authenticated customer.
- A valid current `X-Device-Token` continues to rotate an existing
  installation. If the installation is not yet owned and a Customer JWT is
  supplied, the rotation also binds it to that customer.
- An authenticated customer may rotate an existing active installation without
  its old device token when the installation is unowned or already belongs to
  that customer. This supports a formatted phone and migration of devices
  registered before ownership existed.
- A customer cannot recover or rebind an installation owned by another
  customer, even if the caller supplies a bearer token. The endpoint returns
  `403` with `device_owner_conflict`.
- Anonymous duplicate registration without the current device token remains a
  `409 device_registration_conflict`.
- Administrative inactivity remains authoritative: authentication does not
  reactivate an inactive device.
- Recovery issues a new random device token, stores only its hash, updates FCM
  and application metadata, refreshes expiry, and invalidates the old token.
- Existing response JSON remains unchanged and never exposes customer
  ownership or the FCM token.

## Code Test Plan

- Unit tests for anonymous registration, authenticated ownership binding,
  same-owner recovery, legacy unowned recovery, cross-owner rejection,
  inactive-device rejection, current-token rotation, and token invalidation.
- Controller tests proving an optional valid Customer identity is forwarded and
  malformed or absent identities are treated as anonymous.
- TestServer functional tests covering OTP/JWT-authenticated recovery,
  cross-user rejection, anonymous compatibility, persistence, and Swagger.
- EF model and migration tests for the nullable Customer-device ownership
  relationship, index, foreign key, clean migration, and populated upgrade.
- Full Customer and Business regression suites plus Release build.

## HTTP Test Plan

- HTTP required: Yes.
- Why: device registration authentication, ownership, persistence, status
  codes, stable problems, and Swagger behavior change.
- Execution level: automated TestServer plus live local HTTPS against a
  disposable/local Customer database. After all local gates pass, deploy and
  run a user-approved production smoke without recording credentials or token
  values.

| Scenario ID | Caller and request | Expected result |
|---|---|---|
| `STEP24-DEVICE-ANON-001` | Anonymous first registration | `200`; unowned device and token issued |
| `STEP24-DEVICE-BIND-002` | Customer JWT registers a new installation | `200`; device owned by that customer |
| `STEP24-DEVICE-LEGACY-003` | Customer JWT recovers an unowned existing installation without old token | `200`; ownership assigned and token rotated |
| `STEP24-DEVICE-OWNER-004` | Owning customer recovers without old token | `200`; token rotated and metadata updated |
| `STEP24-DEVICE-CROSS-005` | Different customer attempts recovery | `403 device_owner_conflict`; no mutation |
| `STEP24-DEVICE-ANON-CONFLICT-006` | Anonymous duplicate without current token | `409 device_registration_conflict`; no mutation |
| `STEP24-DEVICE-INACTIVE-007` | Owner attempts recovery of inactive device | `401 device_token_inactive`; no mutation |
| `STEP24-DEVICE-ROTATE-BIND-008` | Valid current device token plus Customer JWT on unowned device | `200`; rotates and binds ownership |
| `STEP24-DEVICE-OLD-TOKEN-009` | Old device token after recovery | `401 device_rotation_unauthorized` |
| `STEP24-DEVICE-SWAGGER-010` | Read Customer Swagger | Description documents optional Customer Bearer recovery; optional device-token header and `403` are present |
| `STEP24-DEVICE-MIGRATION-011` | Upgrade populated database | Existing devices remain unowned and recoverable; FK/index created |

## HTTP Test Results

- Focused unit/controller/TestServer/model/migration/Swagger tests: 25 passed,
  0 failed.
- Customer API project: 1,308 passed, 0 failed, 0 skipped.
- Business API project: 584 passed, 0 failed, 0 skipped.
- Complete sequential total: 1,892 passed, 0 failed, 0 skipped.
- Release build: 0 warnings, 0 errors.
- All six Customer migrations applied from an empty disposable LocalDB.
- Live local HTTPS manifest: 10 passed, 0 failed, 0 deferred.
- The dedicated local database was removed after the run.
- Production deployment and a user-approved live OTP/device recovery smoke are
  pending.
- Detailed local evidence:
  `scripts/http-tests/plans/step-24-device-recovery.results.md`.
