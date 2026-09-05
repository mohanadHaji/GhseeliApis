# Step 20 Customer Available Slots HTTP Test Plan

Date: 2026-09-04

Status: Completed

Execution evidence:
`STEP_20_AVAILABLE_SLOTS_HTTP_TEST_RESULTS.md`

## Observable behavior

Customers can select one catalog business and branch, submit a branch-local
calendar date plus one or more offerings and add-on selections, and receive a
sorted list of appointment starts with authoritative configured and remaining
capacity.

Customer route:

```text
POST /api/v1/catalog/businesses/{businessId}/branches/{branchId}/available-slots
```

The route requires `X-Device-Token`, uses the selected Customer catalog
business and branch IDs, and never accepts a client-supplied duration or
capacity. The response is an advisory snapshot with `Cache-Control: no-store`;
booking confirmation continues to revalidate price, availability, and capacity
atomically.

Business internal route:

```text
POST /api/v1/internal/appointments/available-slots
```

The internal route requires the existing HMAC service authentication and the
new `appointment_available_slots` operation. Business API owns offering
selection validation, duration, branch timezone, schedules, overrides, lead
time, horizon, daylight-saving handling, and remaining reservation capacity.

## Request contract

```json
{
  "date": "2026-09-07",
  "expectedCatalogVersion": 42,
  "currency": "ILS",
  "customerLocation": {
    "latitude": 32.0853,
    "longitude": 34.7818
  },
  "items": [
    {
      "offeringId": "00000000-0000-0000-0000-000000000001",
      "selectedAddons": [
        {
          "addonChoiceId": "00000000-0000-0000-0000-000000000002",
          "quantity": 1
        }
      ]
    }
  ],
  "includeUnavailable": false
}
```

`date` is interpreted in the branch timezone. At least one item is required.
The server derives total duration from authoritative offerings and selections.

## Response contract

```json
{
  "businessId": "00000000-0000-0000-0000-000000000010",
  "branchId": "00000000-0000-0000-0000-000000000011",
  "date": "2026-09-07",
  "timeZoneId": "Asia/Jerusalem",
  "catalogVersion": 42,
  "currency": "ILS",
  "totalDurationMinutes": 60,
  "generatedAtUtc": "2026-09-04T10:00:00Z",
  "slots": [
    {
      "startUtc": "2026-09-06T22:00:00Z",
      "endUtc": "2026-09-06T23:00:00Z",
      "startLocal": "2026-09-07T01:00:00",
      "endLocal": "2026-09-07T02:00:00",
      "configuredCapacity": 3,
      "remainingCapacity": 2,
      "isAvailable": true
    }
  ]
}
```

No reservation IDs, customer identities, work-order data, or other PII are
returned.

## Scenario matrix

| ID | Surface | Scenario | Expected |
|---|---|---|---|
| `STEP20-SLOTS-CUSTOMER-001` | Customer | Valid device, business, branch, date, offering | 200; ordered slots, authoritative duration and remaining capacity; no-store. |
| `STEP20-SLOTS-CUSTOMER-002` | Customer | Multiple compatible offerings/add-ons | 200; combined authoritative duration controls slot fit. |
| `STEP20-SLOTS-CUSTOMER-003` | Customer | Multiple catalog businesses | Selected business/branch is isolated; no cross-provider data. |
| `STEP20-SLOTS-CUSTOMER-004` | Customer | Missing device token | 401; no Business request. |
| `STEP20-SLOTS-CUSTOMER-005` | Customer | Invalid, expired, inactive, or rotated device token | 401; no Business request. |
| `STEP20-SLOTS-CUSTOMER-006` | Customer | Empty/malformed business or branch route GUID | 404/400 route-safe response. |
| `STEP20-SLOTS-CUSTOMER-007` | Customer | Unknown business or branch | Non-disclosing 404. |
| `STEP20-SLOTS-CUSTOMER-008` | Customer | Branch belongs to another business/provider | Stable 400 scope mismatch; no Business request. |
| `STEP20-SLOTS-CUSTOMER-009` | Customer | Offering belongs to another business/branch | Stable 400 scope mismatch. |
| `STEP20-SLOTS-CUSTOMER-010` | Customer | Missing/wrong content type | 415 before processing. |
| `STEP20-SLOTS-CUSTOMER-011` | Customer | Empty, null, malformed, or oversized JSON | Stable 400/413; no upstream call. |
| `STEP20-SLOTS-CUSTOMER-012` | Customer | Missing/default/impossible/past date | Field-level 400. |
| `STEP20-SLOTS-CUSTOMER-013` | Customer | Date beyond supported search bound | Field-level 400. |
| `STEP20-SLOTS-CUSTOMER-014` | Customer | Empty items, null item, too many items, duplicate offering | Field-level 400. |
| `STEP20-SLOTS-CUSTOMER-015` | Customer | Null/duplicate/unknown add-on or negative/over-limit quantity | Field-level or authoritative 400. |
| `STEP20-SLOTS-CUSTOMER-016` | Customer | Invalid latitude/longitude or partial required location | Field-level 400. |
| `STEP20-SLOTS-CUSTOMER-017` | Customer | Stale catalog version | 409 with stable stale-catalog code. |
| `STEP20-SLOTS-CUSTOMER-018` | Customer | Business timeout/5xx/invalid/oversized response | Safe 503/502; no provider body or internals leaked. |
| `STEP20-SLOTS-CUSTOMER-019` | Customer | Arabic/Hebrew language selection | Stable code and localized message; slot data unchanged. |
| `STEP20-SLOTS-CUSTOMER-020` | Customer | Unexpected JSON properties | Ignored safely; no authority granted to client fields. |
| `STEP20-SLOTS-INTERNAL-001` | Business internal | Valid HMAC request | 200 with authoritative slots and capacity. |
| `STEP20-SLOTS-INTERNAL-002` | Business internal | Missing/invalid/stale/replayed signature or wrong operation | 401/409 according to internal security contract. |
| `STEP20-SLOTS-INTERNAL-003` | Business internal | Company/branch mismatch or inactive company/branch | Stable validation error; no slot data leak. |
| `STEP20-SLOTS-INTERNAL-004` | Business internal | No settings, inactive settings, no schedule, or closed date | 200 empty slots with authoritative metadata or stable validation result. |
| `STEP20-SLOTS-INTERNAL-005` | Business internal | Recurring schedule and capacity | Generates aligned starts and exact remaining counts. |
| `STEP20-SLOTS-INTERNAL-006` | Business internal | Active date override | Override replaces recurring schedule and capacity. |
| `STEP20-SLOTS-INTERNAL-007` | Business internal | Overnight schedule/carry-over | Only starts belonging to requested local date are returned. |
| `STEP20-SLOTS-INTERNAL-008` | Business internal | Lead-time boundary | Earlier slots omitted; exact eligible boundary retained. |
| `STEP20-SLOTS-INTERNAL-009` | Business internal | Booking-horizon boundary | Last allowed local date works; later date rejected. |
| `STEP20-SLOTS-INTERNAL-010` | Business internal | DST invalid or ambiguous local time | Unsupported start omitted; no duplicate/invalid UTC slots. |
| `STEP20-SLOTS-INTERNAL-011` | Business internal | Service duration spans multiple slot units | Only starts whose full duration fits the window are returned. |
| `STEP20-SLOTS-INTERNAL-012` | Business internal | Capacity occupied by Pending/Confirmed/InProgress/legacy Reserved | Remaining capacity decreases for every overlapping reservation. |
| `STEP20-SLOTS-INTERNAL-013` | Business internal | Completed/Cancelled/NoShow reservations | Terminal reservations do not consume capacity. |
| `STEP20-SLOTS-INTERNAL-014` | Business internal | Exact end/start boundary | Adjacent non-overlapping reservation does not consume capacity. |
| `STEP20-SLOTS-INTERNAL-015` | Business internal | `includeUnavailable=false/true` | Full slots omitted by default; included with zero remaining when requested. |
| `STEP20-SLOTS-INTERNAL-016` | Business internal | Concurrent reservation after search | Search remains advisory; final reservation atomically allows only capacity. |
| `STEP20-SLOTS-CONTRACT-001` | Swagger/contracts | Customer and internal schemas/routes/statuses | OpenAPI and serialized neutral contracts remain aligned. |
| `STEP20-SLOTS-REGRESSION-001` | Regression | Existing pricing and booking validation | Existing proposed-time and final-capacity behavior remains unchanged. |

## Code Test Plan

- Business request validator tests.
- Pure slot generation and timezone/DST tests.
- Relational capacity-query tests including overlapping and terminal statuses.
- Internal Business TestServer authentication and contract tests.
- Customer catalog scope/mapping service tests.
- `BusinessApiClient` signing, serialization, retry, and error-mapping tests.
- Customer TestServer device, binding, localization, and no-store tests.
- Existing pricing, reservation, catalog, route-boundary, Swagger, and security
  regressions.

## HTTP Test Plan

- HTTP required: Yes.
- Why: this adds public and internal routes, DTOs, authentication,
  serialization, validation, capacity output, and upstream failure mapping.
- Execution level: TestServer and repeatable live local HTTP against disposable
  Customer and Business databases.
- Production execution is prohibited.

## Completion gate

Every required scenario must pass. Results must record exact automated and
live-local counts. Deployment cannot begin while a relevant scenario is
planned, failed, or unexplained.
