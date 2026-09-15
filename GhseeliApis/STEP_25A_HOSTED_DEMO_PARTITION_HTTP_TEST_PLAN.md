# Step 25A - Hosted demo data partition HTTP test plan

## Goal

Allow the frontend team to exercise the deployed Customer and Business APIs
against a larger deterministic demo dataset stored in the existing databases,
without exposing demo rows to production identities or allowing demo activity
to affect production workflows.

## Contract

- `IsDemo` is persisted on demo identity/device and aggregate roots.
- Existing rows and all normal registrations default to `IsDemo = false`.
- Public request DTOs never accept `isDemo`.
- Customer request partition is derived from the validated device token and
  must match an authenticated Customer token.
- Business request partition is derived from the authenticated Business token.
- Internal Customer/Business calls propagate the partition in an HMAC-signed
  query value.
- Demo identities can read/write only demo rows; production identities can
  read/write only production rows.
- Demo OTP delivery, FCM delivery, and Lahza initialization are disabled.
- The committed JSON is the canonical fixture and exact cleanup manifest.

## Dataset target

- 5 companies, 8 branches, 6 categories, 20 offerings.
- At least 8 add-on groups and 30 choices.
- 8 customers, 10 devices, 13 vehicles, and 12 addresses.
- 6 drafts and 12 correlated Customer booking/Business reservation/work-order
  scenarios with varied statuses and payment states.

## HTTP scenarios

| ID | Scenario |
|---|---|
| STEP25A-DEMO-001 | Production device browse excludes every demo company |
| STEP25A-DEMO-002 | Demo device browse returns only demo companies |
| STEP25A-DEMO-003 | Demo Customer login issues a Demo-partition token |
| STEP25A-DEMO-004 | Production Customer token with Demo device is rejected |
| STEP25A-DEMO-005 | Demo Customer token with Production device is rejected |
| STEP25A-DEMO-006 | Demo Customer can access only their demo drafts/bookings |
| STEP25A-DEMO-007 | Production Customer cannot fetch a known demo booking ID |
| STEP25A-DEMO-008 | Demo Business login issues a Demo-partition token |
| STEP25A-DEMO-009 | Demo Business user sees only demo company/work orders |
| STEP25A-DEMO-010 | Production Business user cannot fetch demo work orders |
| STEP25A-DEMO-011 | Signed Demo validation/reservation remains Demo end to end |
| STEP25A-DEMO-012 | Missing/tampered internal partition fails safely |
| STEP25A-DEMO-013 | Demo OTP request performs no SMTP delivery |
| STEP25A-DEMO-014 | Demo payment initialization performs no Lahza request |
| STEP25A-DEMO-015 | Repeat seed creates no duplicates |
| STEP25A-DEMO-016 | Cleanup deletes exactly canonical demo IDs |
| STEP25A-DEMO-017 | Cleanup leaves production sentinel rows unchanged |

## Execution level

- Unit/model tests.
- Relational migration and clean/populated upgrade tests.
- Customer and Business TestServer coverage.
- Disposable local HTTP execution covering every scenario above.
- Hosted HTTP verification after deployment and seeding.
- No real OTP, FCM, or Lahza side effects.
