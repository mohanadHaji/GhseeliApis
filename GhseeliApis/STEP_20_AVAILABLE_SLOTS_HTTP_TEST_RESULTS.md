# Step 20 Available Slots Test Results

Date: 2026-09-05

Status: Passed

## Delivered behavior

- Customer endpoint:
  `POST /api/v1/catalog/businesses/{businessId}/branches/{branchId}/available-slots`
- Internal Business endpoint:
  `POST /api/v1/internal/appointments/available-slots`
- Customer requests require a valid device token, JSON content type, and a
  bounded 64 KiB body.
- Customer catalog IDs are validated and mapped to Business source IDs.
- Business API owns service/add-on validation, combined duration, timezone,
  schedules, date overrides, lead time, booking horizon, and current capacity.
- Responses are non-cacheable and are validated before source data is mapped
  back to public Customer IDs.
- Slot search is advisory. Existing serializable reservation logic remains the
  authoritative concurrency guard.

## Automated results

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Customer API | 1,261 | 0 | 0 |
| Business API | 583 | 0 | 0 |
| Complete solution | 1,844 | 0 | 0 |

Focused available-slot, Swagger, HMAC, and reservation-concurrency selections
also passed before the complete solution run.

## Live-local HTTP results

Disposable SQL Server databases and independently hosted HTTPS Customer and
Business processes were used.

| Result | Count |
|---|---:|
| Passed | 7 |
| Failed | 0 |
| Deferred | 0 |

Covered:

- successful Customer-to-Business slot lookup;
- device-token rejection;
- unsupported media type;
- malformed empty item selection;
- stale catalog version;
- direct signed Business request;
- missing Business HMAC credentials.

The final hardened run produced
`scripts/http-tests/artifacts/step20-available-slots-a20e0905abcf.results.local.json`.
Generated artifacts and local credentials remain ignored.

## Additional evidence

- Capacity-consuming and terminal reservation statuses were checked.
- Exact adjacent reservation boundaries do not reduce capacity.
- Multiple offerings use their combined authoritative duration.
- Active date overrides replace recurring hours and capacity.
- A service credential allowed only `appointment_available_slots` can call the
  endpoint; it does not require `appointment_validate`.
- Foreign or malformed upstream response identity, catalog version, currency,
  intervals, ordering, and capacities fail closed.
- Existing SQL-backed capacity-one reservation concurrency coverage passed.
