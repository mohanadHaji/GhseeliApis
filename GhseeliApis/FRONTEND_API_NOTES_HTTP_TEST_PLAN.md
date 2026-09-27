# Frontend API Notes HTTP Test Plan

Date: 2026-09-24

Status: **Implemented and validated locally.** The final serial regression
passed 2,337 tests, the Release build passed with no warnings or errors, both
disposable API health checks returned 200, the HTTP harness passed 31/31
self-tests, and the availability live-local manifest passed 12/12 scenarios.
All 78 scoped frontend-note scenarios have explicit test traceability. See
`FRONTEND_API_NOTES_HTTP_TEST_RESULTS.md`.

## 1. Purpose and execution level

This plan freezes the caller-visible contracts approved for the frontend notes:

- stable vehicle types and vehicle-image propagation;
- category image/color presentation;
- business search, top ranking, favourites, reviews, and rating aggregates;
- public banners plus Customer Admin management;
- optional device authentication for public discovery/configuration/direct
  pricing reads;
- advisory business availability discovery and its Business internal HMAC
  batch operation.

HTTP required: **Yes**. These changes affect routes, DTOs, serialization,
validation, authentication, partition selection, localization, Swagger,
internal HMAC, persistence, and migrations.

Execution level: **TestServer for HTTP contract behavior, plus repeatable live local HTTP only for real-host Swagger, host startup/health, and internal HMAC/HTTPS transport behavior** against
disposable Customer and Business SQL Server databases. Production execution is
prohibited. The committed live-local input is
`scripts/http-tests/plans/frontend-api-notes.manifest.json`.

Use manifest tag `current-slice` to validate the implemented
favourites/search/top Swagger contract without selecting future banner or
availability-search assertions. The `progressive-future` tag intentionally
retains those later-route assertions and is not a current-slice pass claim.

Scenario status is tracked per row. Migration-only scenarios are automated in
the test suite and are intentionally absent from the live-local manifest.
Remaining implementation work must add the stated lower-level/TestServer
coverage, progressively enable the manifest entries, execute them, and record
sanitized evidence in a separate results document.

## 2. Boundary and security decisions

1. `Ghseeli.CustomerApi` owns saved vehicles, favourites, reviews, banners, and
   customer-facing catalog/rating projections.
2. `Ghseeli.BusinessApi` owns companies, branches, category metadata,
   schedules, capacity, and advisory availability calculation.
3. `Ghseeli.IntegrationContracts` contains only neutral string enums and
   versioned HTTP contracts. Neither API references the other's implementation
   or database.
4. Anonymous public requests select **Production**. A supplied valid device
   token selects its trusted Production/Demo partition. A supplied valid
   Customer JWT selects its account partition. If both are supplied they must
   agree; otherwise return `403 data_partition_mismatch`.
5. A missing optional device token is accepted. A malformed, unknown, expired,
   inactive, rotated, or duplicated supplied token is never ignored and returns
   `401 device_token_invalid`.
6. Customer JWTs never authorize Business routes. Business JWTs never authorize
   Customer routes. HMAC credentials never satisfy a bearer or device
   requirement, and bearer or device credentials never authorize internal
   routes.
7. New partitioned roots (`BusinessFavourite`, `BusinessReview`, and `Banner`)
   use trusted automatic `IsDemo` assignment and global query filters. Public
   identifiers never select a partition.
8. All success and problem responses use `Cache-Control: no-store`, preserve
   the repository security headers, echo/sanitize `X-Correlation-Id`, and never
   expose stack traces, SQL, tokens, signatures, contact data, booking IDs in
   public reviews, or internal database IDs.

## 3. Exact proposed public contracts

### 3.1 Vehicle type and image

The wire enum is a case-sensitive JSON string with exactly:

```text
Sedan
Motorcycle
Suv5Seater
Suv7Seater
Van7Seater
```

Integer enum values and unknown strings are rejected with
`400 vehicle_type_invalid`. Legacy persisted `Car` normalizes to `Sedan`,
`SUV` to `Suv5Seater`, and any other unknown persisted value to `Sedan` during
the owning migration. Vehicle type affects neither eligibility nor price.

Existing Customer routes remain:

| Method | Route | Auth |
|---|---|---|
| GET | `/api/Vehicles/my-vehicles` | Customer JWT |
| GET | `/api/Vehicles/{id}` | Customer JWT, owner only |
| POST | `/api/Vehicles` | Customer JWT |
| PUT | `/api/Vehicles/{id}` | Customer JWT, owner only |
| DELETE | `/api/Vehicles/{id}` | Customer JWT, owner only |

Create/update request adds required `vehicleType` and optional `imageUrl`:

```json
{
  "make": "Toyota",
  "model": "Corolla",
  "year": "2025",
  "licensePlate": "LOCAL-REDACTED",
  "color": "White",
  "vehicleType": "Sedan",
  "imageUrl": "https://cdn.example.test/vehicles/sedan.png"
}
```

`imageUrl` is `null` or an absolute HTTPS URL, maximum 500 characters. HTTP,
relative, credential-bearing, malformed, and overlong URLs return
`400 vehicle_image_url_invalid`. Responses add the same `vehicleType` and
nullable `imageUrl`.

The existing checkout intent `vehicle` object and its direct-pricing, draft,
booking, internal reservation, and Business work-order snapshots become:

```json
{
  "vehicleType": "Sedan",
  "imageUrl": "https://cdn.example.test/vehicles/sedan.png",
  "licensePlate": "LOCAL-REDACTED",
  "make": "Toyota",
  "model": "Corolla",
  "color": "White"
}
```

`vehicleType` uses the stable enum; `imageUrl` is optional and copied as a
snapshot. Unknown extension properties cannot affect price.

### 3.2 Category presentation metadata

Business category create/update DTOs and category list/detail responses add:

```json
{
  "imageUrl": "https://cdn.example.test/categories/exterior.png",
  "colorHex": "#1A73E8"
}
```

Both fields are nullable. `imageUrl` follows the HTTPS/500-character rule.
`colorHex` is normalized to uppercase and must match `^#[0-9A-F]{6}$`.
Invalid values return `400 catalog_category_presentation_invalid` with
field-level errors. A presentation change increments the owning company's
catalog version, changes the deterministic snapshot hash, flows through
`GET /api/v1/internal/catalog/snapshot`, and appears unchanged in:

```text
GET /api/v1/catalog/categories
```

The Customer `CatalogCategoryResponse` adds nullable `imageUrl` and
`colorHex`; Arabic/Hebrew name and description selection remains unchanged.

### 3.3 Business browse, search, top, aggregates, and favourites

```text
GET /api/v1/catalog/businesses
    ?branchId=<guid>
    &categoryId=<guid>
    &search=<1..100 normalized characters>
    &top=5|10
    &language=ar|he
    &refresh=true|false
```

- `search` is trimmed and matched case-insensitively against Arabic and Hebrew
  business names. An empty normalized search is treated as absent.
- `top` accepts only `5` or `10`; any other value returns
  `400 catalog_top_invalid`.
- Without `top`, stable catalog order remains authoritative.
- With `top`, order is average rating descending, rating count descending, then
  existing stable catalog order and stable ID.
- Existing branch/category ownership filters and
  `catalog_filter_mismatch` behavior remain.

Business list, detail, and offering-context projections add:

```json
{
  "isFavourite": false,
  "averageRating": 4.5,
  "ratingCount": 12
}
```

`averageRating` is `0.0` when `ratingCount` is zero and otherwise is the
deterministically rounded review average for the trusted partition.
`isFavourite` is always `false` for an anonymous request. With a valid Customer
JWT it reflects only that customer in the selected partition.

Favourite mutation routes are:

| Method | Route | Auth | Success |
|---|---|---|---|
| PUT | `/api/v1/catalog/businesses/{businessId}/favourite` | Customer JWT | `204`, idempotent |
| DELETE | `/api/v1/catalog/businesses/{businessId}/favourite` | Customer JWT | `204`, idempotent |

The optional device token may also be supplied and must agree with the JWT
partition. The target must be an active catalog business in that partition;
otherwise return non-disclosing `404 catalog_business_not_found`. The database
enforces one `(UserId, BusinessSourceId, IsDemo)` row.

### 3.4 Reviews and rating aggregates

Owned review routes:

| Method | Route | Auth | Contract |
|---|---|---|---|
| GET | `/api/v1/bookings/{bookingId}/review` | Customer JWT | Read caller's review |
| PUT | `/api/v1/bookings/{bookingId}/review` | Customer JWT | Create or replace one review |
| DELETE | `/api/v1/bookings/{bookingId}/review?expectedRowVersion={current}` | Customer JWT | Delete caller's review |

PUT request:

```json
{
  "rating": 5,
  "comment": "Excellent service.",
  "expectedRowVersion": null
}
```

- `rating` is an integer from 1 through 5.
- `comment` is nullable, trimmed, and at most 1000 characters; blank becomes
  `null`.
- `expectedRowVersion` is required only when replacing an existing review and
  is the prior base64 row-version value.
- DELETE requires the current base64 `expectedRowVersion` query value for an
  existing review. A concurrent update or delete makes that precondition stale
  and returns `409 review_version_conflict`.
- The booking must exist in the trusted partition, belong to the caller, and be
  `Completed`. Wrong owner/missing booking is non-disclosing
  `404 booking_not_found`; other states return
  `409 review_booking_not_completed`; stale versions return
  `409 review_version_conflict`.
- One row per booking is enforced under concurrent requests. Creation returns
  `201`; replacement returns `200`; deletion returns `204`.

Owned response:

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "bookingId": "00000000-0000-0000-0000-000000000000",
  "businessId": "00000000-0000-0000-0000-000000000000",
  "rating": 5,
  "comment": "Excellent service.",
  "createdAtUtc": "2026-09-24T08:00:00Z",
  "updatedAtUtc": "2026-09-24T08:00:00Z",
  "rowVersion": "<base64>"
}
```

Public reviews:

```text
GET /api/v1/catalog/businesses/{businessId}/reviews
    ?page=1&pageSize=20&language=ar|he
```

`page >= 1`; `pageSize` is 1..50. Invalid values return
`400 pagination_invalid`. Results are ordered by `createdAtUtc` descending then
stable review ID. Public items contain only:

```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "rating": 5,
  "comment": "Excellent service.",
  "customerDisplayName": "M***",
  "createdAtUtc": "2026-09-24T08:00:00Z"
}
```

The response contains `page`, `pageSize`, `totalCount`, `averageRating`,
`ratingCount`, and `items`. It never contains booking ID, user ID, email,
phone, address, plate, or row version. Missing comments remain `null`.

### 3.5 Banners

Public read:

```text
GET /api/v1/banners?language=ar|he
```

It returns only active banners in trusted-partition `displayOrder`, then stable
ID order:

```json
{
  "banners": [
    {
      "id": "00000000-0000-0000-0000-000000000000",
      "imageUrl": "https://cdn.example.test/banners/launch.png",
      "displayOrder": 10
    }
  ]
}
```

Customer Admin routes:

| Method | Route | Auth |
|---|---|---|
| GET | `/api/v1/admin/banners` | Customer Admin JWT |
| POST | `/api/v1/admin/banners` | Customer Admin JWT |
| PUT | `/api/v1/admin/banners/{id}` | Customer Admin JWT |
| DELETE | `/api/v1/admin/banners/{id}` | Customer Admin JWT |

Create request:

```json
{
  "imageUrl": "https://cdn.example.test/banners/launch.png",
  "displayOrder": 10,
  "isActive": true
}
```

Update adds required `expectedRowVersion`. `imageUrl` is required, absolute
HTTPS with maximum 500 characters, and cannot contain URI credentials;
`displayOrder` is 0..10000. Admin responses
also include `isActive`, UTC timestamps, and `rowVersion`. POST returns `201`,
PUT `200`, DELETE `204`; stale versions return
`409 banner_version_conflict`. Public reads never expose inactive banners,
row versions, or timestamps.

The frozen non-disclosure contract distinguishes update from delete:
PUT of a nonexistent or cross-partition banner returns `404 banner_not_found`;
DELETE of either returns `409 banner_version_conflict`, matching stale-delete
behavior so deletion does not disclose whether a row exists in another
partition. DELETE is therefore intentionally not idempotent for an unknown ID.

### 3.6 Optional-device public access

The following routes use optional device authentication:

```text
GET  /api/v1/configuration
GET  /api/v1/catalog/categories
GET  /api/v1/catalog/businesses
GET  /api/v1/catalog/businesses/{id}
GET  /api/v1/catalog/businesses/{id}/offerings
GET  /api/v1/catalog/offerings/{id}
POST /api/v1/pricing/reprice
GET  /api/v1/banners
GET  /api/v1/catalog/businesses/{businessId}/reviews
```

No `X-Device-Token` means Production and is not an authentication error. A
valid Demo device selects Demo. A bad supplied token returns the existing
localized device-token problem. Draft, booking, and payment routes retain
their current required-device rules.

Direct pricing keeps its current checkout-intent envelope, adds vehicle
`imageUrl`, no longer requires or persists a device ID, remains stateless, and
returns `Cache-Control: no-store`. Anonymous requests cannot access or mutate
drafts.

`POST /api/v1/catalog/businesses/availability-search` now has route-specific
anonymous, Customer-JWT-only, valid-device, matching dual-credential, Demo, malformed
device, and partition-mismatch coverage. The shared optional-device middleware
suite remains authoritative for the remaining invalid/expired/inactive/rotated
token states rather than duplicating every unrelated route family.

### 3.7 Advisory availability discovery

Customer route:

```text
POST /api/v1/catalog/businesses/availability-search?language=ar|he
```

Request:

```json
{
  "vehicleType": "Sedan",
  "date": "2026-10-01",
  "preferredLocalTime": "10:30:00",
  "categoryId": "00000000-0000-0000-0000-000000000000",
  "location": {
    "latitude": 32.0853,
    "longitude": 34.7818
  }
}
```

`categoryId` and `location` are optional; latitude/longitude must be supplied
together and be in normal geographic ranges. Date must be today or later and
within the supported Business booking horizon. `preferredLocalTime` is a
branch-local wall-clock preference. The stable vehicle enum is validated but
does not alter eligibility or price.

Response:

```json
{
  "language": "ar",
  "date": "2026-10-01",
  "preferredLocalTime": "10:30:00",
  "results": [
    {
      "business": {
        "id": "00000000-0000-0000-0000-000000000000",
        "sourceId": "00000000-0000-0000-0000-000000000000",
        "name": "شركة",
        "isFavourite": false,
        "averageRating": 4.5,
        "ratingCount": 12
      },
      "branch": {
        "id": "00000000-0000-0000-0000-000000000000",
        "sourceId": "00000000-0000-0000-0000-000000000000",
        "name": "الفرع",
        "address": "العنوان"
      },
      "nearestSlot": {
        "startUtc": "2026-10-01T07:30:00Z",
        "startLocal": "2026-10-01T10:30:00",
        "configuredCapacity": 2,
        "remainingCapacity": 1
      },
      "advisory": true
    }
  ]
}
```

Customer filters candidate businesses/branches by active catalog,
category, and service area before the internal call. Each business contributes
at most its best branch/nearest free configured slot. Ordering is absolute
preferred-time difference, average rating descending, rating count descending,
stable catalog order, then stable ID. A valid request with no eligible business, branch, or free slot returns `200` with an empty `results` array. An internal timeout, transport failure, non-success response, malformed/empty/oversized upstream payload, or unavailable dependency returns explicit `502`/`503 availability_unavailable`; Customer never fabricates an empty success for an upstream failure.

The response is advisory because service duration is not yet known. The
existing offering-aware route remains the authoritative next step:

```text
POST /api/v1/catalog/businesses/{businessId}/branches/{branchId}/available-slots
```

Business internal route:

```text
POST /api/v1/internal/appointments/availability-discovery
```

It requires HTTPS and the existing HMAC quartet with the new
`appointment_availability_discovery` operation. Neutral v1 request:

```json
{
  "contractVersion": "v1",
  "date": "2026-10-01",
  "preferredLocalTime": "10:30:00",
  "candidates": [
    {
      "companyId": "00000000-0000-0000-0000-000000000000",
      "branchIds": [
        "00000000-0000-0000-0000-000000000000"
      ]
    }
  ]
}
```

Response contains one result per candidate company, with either its selected
branch/nearest slot or a stable non-sensitive no-availability reason. Business
evaluates active settings, recurring schedules, overrides, lead time, horizon,
timezone/DST, and capacity consumed by non-terminal reservations. It does not
accept a Customer/Business JWT and does not return customer/catalog-local IDs.

## 4. Stable scenario matrix

Legend: `ANON` anonymous; `DEV(P|D)` valid Production/Demo device;
`CUST(P|D)` Customer JWT; `ADMIN(P|D)` Customer Admin JWT; `HMAC(P|D)`
internal HMAC with signed partition. Every success asserts JSON content type,
`no-store`, correlation/security headers, exact partition isolation, and no
secret/PII leakage. Every problem asserts localized Problem Details and a
stable code. Mutations use disposable fixtures and idempotent cleanup.

Each row below is one independently executable HTTP request. `Body: none`
means no request body. Named bodies such as `valid vehicle create` are the
exact JSON objects in section 3 with only the explicitly stated field change.
Unless a row says otherwise, prerequisites are the named disposable fixture,
headers are `Accept: application/json` plus the stated auth/device/HMAC
headers, cleanup is the narrow inverse mutation, status is `planned`, and
result evidence is `none (not executed)`. `automated-testserver` rows are
deliberately **not** live-required: routing, model binding, policy, persistence,
and response contracts are deterministic in TestServer and do not depend on
Kestrel, TLS, proxy, or environment wiring. Only rows marked
`automated-live` belong in the committed live manifest.

This expanded matrix contains **227 unique scenarios**: **11 automated-live**
transport/host scenarios and **216 non-live scenarios** covered by TestServer,
contract, repository, service, migration, or regression automation. The live
manifest contains exactly the same 11 live IDs. Splitting a former compound row
does not claim execution or a pass result.

### 4.1 Vehicle and propagation

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-VEHICLE-CRUD-001` | POST `/api/Vehicles`; CUST(P), caller correlation | Valid enum, HTTPS image | 201; HTTPS `Location`, JSON content type, correlation propagation, no-store and standard security headers; response round-trips type/image; one Production row | automated-testserver | automated |
| `FAN-VEHICLE-CRUD-002` | GET `/api/Vehicles/my-vehicles`; owner CUST(P) | Body: none; created vehicle | 200; matching list item contains exact type/image | automated-testserver | automated |
| `FAN-VEHICLE-CRUD-015` | GET `/api/Vehicles/{id}`; owner CUST(P) | Body: none; created vehicle ID | 200; detail contains exact type/image | automated-testserver | automated |
| `FAN-VEHICLE-CRUD-003` | PUT `/api/Vehicles/{id}`; customer-user-jwt (Production; vehicle owner) | Change enum and HTTPS image | 200; exact persisted update after database reload, no duplicate | automated-testserver | automated |
| `FAN-VEHICLE-CRUD-004` | DELETE `/api/Vehicles/{id}`; owner CUST(P) | Body: none; existing vehicle | 204 empty body; row deleted | automated-testserver | automated |
| `FAN-VEHICLE-CRUD-016` | GET `/api/Vehicles/{id}`; owner CUST(P) | Body: none; ID deleted by `004` | 404 `vehicle_not_found`; no deleted data | automated-testserver | automated |
| `FAN-VEHICLE-AUTH-005` | GET `/api/Vehicles/my-vehicles`; ANON | Body: none | 401 `customer_authentication_required` | automated-testserver | automated |
| `FAN-VEHICLE-AUTH-017` | GET `/api/Vehicles/{foreignId}`; CUST(P) non-owner | Body: none | stable `resource_not_found` problem; no owner, vehicle, or foreign data disclosure | automated-testserver | automated |
| `FAN-VEHICLE-VALIDATION-006` | POST `/api/Vehicles`; CUST(P) | Valid create body with `vehicleType` omitted | 400 `vehicle_type_invalid`; `vehicleType` field error | automated-testserver | automated |
| `FAN-VEHICLE-VALIDATION-018` | POST `/api/Vehicles`; CUST(P) | Valid create body with integer `vehicleType: 0` | 400 `vehicle_type_invalid` | automated-testserver | automated |
| `FAN-VEHICLE-VALIDATION-019` | PUT `/api/Vehicles/{id}`; owner CUST(P) | Valid update body with `vehicleType: "Car"` | 400 `vehicle_type_invalid`; row unchanged | automated-testserver | automated |
| `FAN-VEHICLE-VALIDATION-020` | PUT `/api/Vehicles/{id}`; owner CUST(P) | Valid update body with `vehicleType: "sedan"` | 400 `vehicle_type_invalid`; row unchanged | automated-testserver | automated |
| `FAN-VEHICLE-VALIDATION-007` | POST `/api/Vehicles`; CUST(P) | Valid create body with HTTP `imageUrl` | 400 `vehicle_image_url_invalid` | automated-testserver | automated |
| `FAN-VEHICLE-VALIDATION-021` | PUT `/api/Vehicles/{id}`; owner CUST(P) | Valid update body with relative `imageUrl` | 400 `vehicle_image_url_invalid`; row unchanged | automated-testserver | automated |
| `FAN-VEHICLE-VALIDATION-022` | PUT `/api/Vehicles/{id}`; owner CUST(P) | Valid update body with credential-bearing HTTPS `imageUrl` | 400 `vehicle_image_url_invalid`; row unchanged | automated-testserver | automated |
| `FAN-VEHICLE-VALIDATION-023` | PUT `/api/Vehicles/{id}`; owner CUST(P) | Valid update body with malformed `imageUrl` | 400 `vehicle_image_url_invalid`; row unchanged | automated-testserver | automated |
| `FAN-VEHICLE-VALIDATION-024` | PUT `/api/Vehicles/{id}`; owner CUST(P) | Valid update body with exactly 501-character HTTPS `imageUrl` | 400 `vehicle_image_url_invalid`; immediate database reload proves the existing row is unchanged | automated-testserver | automated |
| `FAN-VEHICLE-VALIDATION-026` | POST `/api/Vehicles`; CUST(P) | Valid create body with exactly 500-character HTTPS `imageUrl` | 201; exact image persists in the single Production row | automated-testserver | automated |
| `FAN-VEHICLE-VALIDATION-027` | POST `/api/Vehicles`; CUST(P) | Valid create body with exactly 501-character HTTPS `imageUrl` | 400 `vehicle_image_url_invalid`; zero vehicle rows | automated-testserver | automated |
| `FAN-VEHICLE-VALIDATION-028` | PUT `/api/Vehicles/{id}`; owner CUST(P) | Valid update body with exactly 500-character HTTPS `imageUrl` | 200; exact image persists after database reload with no duplicate | automated-testserver | automated |
| `FAN-VEHICLE-PARTITION-008` | POST `/api/Vehicles`; CUST(D) | Valid Demo vehicle body | 201; Demo row only | automated-testserver | automated |
| `FAN-VEHICLE-PARTITION-025` | GET `/api/Vehicles/{demoVehicleId}`; CUST(P) | Body: none | non-disclosing 404 | automated-testserver | automated |
| `FAN-VEHICLE-DRAFT-009` | POST `/api/v1/checkout/drafts`; DEV(P) | Exact checkout intent with stable type/image | 200; draft response/persistence round-trip | automated-testserver | automated |
| `FAN-VEHICLE-DRAFT-026` | PUT `/api/v1/checkout/drafts/{orderGuid}`; DEV(P) | Exact updated intent with current version and changed image | 200; version increments once and image round-trips | automated-testserver | automated |
| `FAN-VEHICLE-PRICING-010` | POST `/api/v1/pricing/reprice`; ANON | Same intent across all five types | 200; same eligibility/price; normalized type/image returned | automated-testserver | automated |
| `FAN-VEHICLE-BOOKING-011` | POST `/api/v1/bookings/from-draft`; DEV(P)+CUST(P) | Repriced draft | 201; booking and internal reservation snapshot type/image | automated-testserver | automated |
| `FAN-VEHICLE-WORKORDER-012` | Business work-order read fixture | Confirmed reservation | Business snapshot contains type/image, no Customer entity access | integration | automated |
| `FAN-VEHICLE-MIGRATION-013` | Customer/Business migration tests | `Car`, `SUV`, unknown, null images | Required mapping and new nullable columns/index/model snapshots | migration | automated |
| `FAN-VEHICLE-REGRESSION-014` | pricing/booking regressions | Existing Sedan-equivalent fixture | Totals, availability, ownership unchanged | automated | automated |

### 4.2 Category metadata and catalog synchronization

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-CATEGORY-BUSINESS-001` | POST `/api/v1/business/catalog/categories`; business-owner-jwt | exact create body `{"nameAr":"خارجي","nameHe":null,"imageUrl":"https://cdn.example.test/categories/exterior.png","colorHex":"#1a73e8"}` | 201; color `#1A73E8`; version +1 | automated-testserver | automated |
| `FAN-CATEGORY-BUSINESS-002` | PUT `/api/v1/business/catalog/categories/{id}`; business-owner-jwt | exact update body with `imageUrl:null`, `colorHex:null` | 200; fields cleared, version +1, hash changes | automated-testserver | automated |
| `FAN-CATEGORY-VALIDATION-003` | POST `/api/v1/business/catalog/categories`; business-owner-jwt | valid body except HTTP image URL | 400 `catalog_category_presentation_invalid`; no row/version bump | automated-testserver | automated |
| `FAN-CATEGORY-VALIDATION-012` | PUT `/api/v1/business/catalog/categories/{id}`; business-owner-jwt | valid body except `colorHex:"#GG0000"` | 400 same code; row/version unchanged | automated-testserver | automated |
| `FAN-CATEGORY-AUTH-004` | POST `/api/v1/business/catalog/categories`; Employee JWT | valid create body | 403 `business_authorization_forbidden`; no row | automated-testserver | automated |
| `FAN-CATEGORY-AUTH-013` | PUT `/api/v1/business/catalog/categories/{foreignId}`; business-owner-jwt | valid update body | non-disclosing 404; no foreign mutation | automated-testserver | automated |
| `FAN-CATEGORY-AUTH-014` | POST `/api/v1/business/catalog/categories`; Customer JWT | valid create body | 401 `business_authentication_required`; no row | automated-testserver | automated |
| `FAN-CATEGORY-INTERNAL-005` | GET `/api/v1/internal/catalog/snapshot`; HMAC(P) | Body: none; category with metadata | 200; v1 category includes exact nullable image/color | automated-testserver | automated |
| `FAN-CATEGORY-INTERNAL-006` | GET `/api/v1/internal/catalog/snapshot`; no HMAC | Body: none | 401 `internal_auth_missing_header`; no body leak | automated-testserver | automated |
| `FAN-CATEGORY-INTERNAL-015` | GET `/api/v1/internal/catalog/snapshot`; bad HMAC signature | Body: none | 401 `internal_auth_invalid_signature`; no body leak | automated-testserver | automated |
| `FAN-CATEGORY-CUSTOMER-007` | GET `/api/v1/catalog/categories`; ANON | Body: none; synchronized Production catalog | 200; exact metadata preserved | automated-testserver | automated |
| `FAN-CATEGORY-LOCALIZATION-008` | GET `/api/v1/catalog/categories?language=ar`; ANON | Body: none; Arabic and optional Hebrew fixture | 200; Arabic names, image/color invariant | automated-testserver | automated |
| `FAN-CATEGORY-LOCALIZATION-016` | GET `/api/v1/catalog/categories?language=he`; ANON | Body: none; one missing Hebrew fixture | 200; Hebrew where present, Arabic fallback where absent | automated-testserver | automated |
| `FAN-CATEGORY-PARTITION-009` | GET `/api/v1/catalog/categories`; DEV(D) | Body: none; distinct Demo metadata | 200; Demo metadata only | automated-testserver | automated |
| `FAN-CATEGORY-PARTITION-017` | GET `/api/v1/catalog/categories`; ANON | Body: none; same IDs differ by partition | 200; Production metadata only | automated-testserver | automated |
| `FAN-CATEGORY-MIGRATION-010` | Both migrations | Existing categories | Nullable columns, filters, lengths, no destructive data | migration | automated |
| `FAN-CATEGORY-REGRESSION-011` | refresh/parity tests | Same version/hash and changed hash | Correct no-op/reapply behavior | integration | automated |

### 4.3 Business search, top, aggregates, and favourites

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-BUSINESS-BROWSE-001` | GET `/api/v1/catalog/businesses`; ANON | Body: none; no filters | 200; all `isFavourite=false`; aggregates present | automated-testserver | automated |
| `FAN-BUSINESS-SEARCH-002` | GET `/api/v1/catalog/businesses?search={trimmedArabic}&language=ar`; ANON | Body: none; Arabic fixture | 200; case-insensitive trimmed match only | automated-testserver | automated |
| `FAN-BUSINESS-SEARCH-003` | GET `/api/v1/catalog/businesses?search={Hebrew}&language=ar`; ANON | Body: none; Hebrew-name fixture | 200; match independent of display language | automated-testserver | automated |
| `FAN-BUSINESS-SEARCH-004` | GET `/api/v1/catalog/businesses?search=%20%20`; ANON | Body: none | 200; exact same ordered IDs as no search | automated-testserver | planned |
| `FAN-BUSINESS-SEARCH-027` | GET `/api/v1/catalog/businesses?search={noMatch}`; ANON | Body: none | 200; empty businesses collection | automated-testserver | automated |
| `FAN-BUSINESS-SEARCH-028` | GET `/api/v1/catalog/businesses?search={101Chars}`; ANON | Body: none | 400 search field error | automated-testserver | planned |
| `FAN-BUSINESS-TOP-005` | Service projection with `top=5`; ANON | 12 providers; rating/count/display/name/ID ties | Exact five in frozen ranking/tie order | automated-unit | automated |
| `FAN-BUSINESS-TOP-016` | Service projection with `top=10`; ANON | 12 providers; rating/count/display/name/ID ties | Exact ten in frozen ranking/tie order | automated-unit | automated |
| `FAN-BUSINESS-TOP-006` | GET `/api/v1/catalog/businesses?top=0`; ANON | Body: none | 400 `catalog_top_invalid` | automated-testserver | automated |
| `FAN-BUSINESS-TOP-017` | GET `/api/v1/catalog/businesses?top=1`; ANON | Body: none | 400 `catalog_top_invalid` | automated-testserver | automated |
| `FAN-BUSINESS-TOP-018` | GET `/api/v1/catalog/businesses?top=6`; ANON | Body: none | 400 `catalog_top_invalid` | automated-testserver | automated |
| `FAN-BUSINESS-TOP-019` | GET `/api/v1/catalog/businesses?top=11`; ANON | Body: none | 400 `catalog_top_invalid` | automated-testserver | automated |
| `FAN-BUSINESS-TOP-020` | GET `/api/v1/catalog/businesses?top=text`; ANON | Body: none | 400 `catalog_top_invalid` | automated-testserver | automated |
| `FAN-BUSINESS-FILTER-007` | GET `/api/v1/catalog/businesses?branchId={id}&categoryId={id}&search={term}&top=5`; ANON | Body: none; same-provider fixtures | 200; all filters and top ranking applied | automated-testserver | planned |
| `FAN-BUSINESS-FILTER-029` | GET `/api/v1/catalog/businesses?branchId={branchId}&categoryId={categoryId}&search={term}&top=5`; ANON | Body: none; cross-provider branch/category IDs | 400 `catalog_filter_mismatch` | automated-testserver | planned |
| `FAN-BUSINESS-FAVOURITE-008` | PUT `/api/v1/catalog/businesses/{businessId}/favourite`; CUST(P) | Body: none; active target | 204; exactly one row | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-021` | PUT `/api/v1/catalog/businesses/{businessId}/favourite`; same CUST(P) | Body: none; row from `008` exists | 204; still exactly one row | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-009` | GET `/api/v1/catalog/businesses`; CUST(P) | Body: none; favourite exists | 200; only target has `isFavourite=true` | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-022` | GET `/api/v1/catalog/businesses/{businessId}`; CUST(P) | Body: none; favourite exists | 200; target detail has `isFavourite=true` | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-023` | GET `/api/v1/catalog/businesses/{businessId}/offerings`; CUST(P) | Body: none; favourite exists | 200; context has `isFavourite=true` | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-010` | DELETE `/api/v1/catalog/businesses/{businessId}/favourite`; CUST(P) | Body: none; existing row | 204; row absent | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-024` | DELETE `/api/v1/catalog/businesses/{businessId}/favourite`; same CUST(P) | Body: none; row already absent | 204; row remains absent | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-025` | GET `/api/v1/catalog/businesses/{businessId}`; CUST(P) | Body: none; after `010` | 200; `isFavourite=false` | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-011` | PUT `/api/v1/catalog/businesses/{businessId}/favourite`; ANON | Body: none | 401 `customer_authentication_required`; no row | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-026` | DELETE `/api/v1/catalog/businesses/{businessId}/favourite`; Business JWT | Body: none | 401 `customer_authentication_required`; no mutation | automated-testserver | planned |
| `FAN-BUSINESS-FAVOURITE-012` | PUT `/api/v1/catalog/businesses/{unknownId}/favourite`; CUST(P) | Body: none | non-disclosing 404; no row | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-030` | PUT `/api/v1/catalog/businesses/{inactiveId}/favourite`; CUST(P) | Body: none | non-disclosing 404; no row | automated-testserver | planned |
| `FAN-BUSINESS-FAVOURITE-031` | PUT `/api/v1/catalog/businesses/{demoOnlyId}/favourite`; CUST(P) | Body: none | non-disclosing 404; no row | automated-testserver | planned |
| `FAN-BUSINESS-FAVOURITE-013` | GET `/api/v1/catalog/businesses`; CUST(P)+DEV(D) | Body: none | 403 `data_partition_mismatch` | automated-testserver | planned |
| `FAN-BUSINESS-PARTITION-014` | GET `/api/v1/catalog/businesses?top=5`; CUST(P)+DEV(P) | Body: none; partition-distinct reviews/favourites | 200; Production ranking/favourites only | automated-testserver | planned |
| `FAN-BUSINESS-PARTITION-032` | GET `/api/v1/catalog/businesses?top=5`; CUST(D)+DEV(D) | Body: none; same source IDs | 200; Demo ranking/favourites only | automated-testserver | planned |
| `FAN-BUSINESS-FAVOURITE-033` | PUT and DELETE favourite; CUST(P)+DEV(P) | Active target | Both 204; mutation occurs only in Production | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-034` | PUT and DELETE favourite; CUST(P)+DEV(D) | Active target | Both 403 `data_partition_mismatch`; no mutation | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-035` | PUT and DELETE favourite; CUST(P) | Unknown, disabled, and Demo-only targets | Exact non-disclosing parity: status, code, media type, cache, correlation shape; no IDs/PII; no mutation | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-036` | DELETE favourite; CUST(P) | Existing target | 204, empty body, `Cache-Control: no-store`, correlation echoed, row removed | automated-testserver | automated |
| `FAN-BUSINESS-FAVOURITE-037` | PUT and DELETE favourite; ANON/non-User JWT | Active target | Exact 401/403 problem media, cache, correlation, stable code, no target/user leakage, no mutation | automated-testserver | automated |
| `FAN-BUSINESS-TOP-038` | Service projection with `search` and `top=5`; ANON | Five higher-rated nonmatches and one lower-rated match | Matching business is returned, proving search filtering precedes top truncation | automated-unit | automated |
| `FAN-BUSINESS-PROJECTION-039` | GET categories and offering detail; ANON/CUST(P) | Rating and favourite fixtures | Direct business context asserts `isFavourite`, `averageRating`, and `ratingCount` | automated-testserver | automated |
| `FAN-BUSINESS-BATCH-040` | Catalog projection services | Multiple providers where supported | Reviews and authenticated favourites are each loaded once per service call with the complete distinct provider-ID set | automated-unit | automated |
| `FAN-BUSINESS-QUERY-015` | GET `/api/v1/catalog/businesses?branchId={a}&branchId={b}`; ANON | Body: none | 400 binding error | automated-testserver | planned |
| `FAN-BUSINESS-QUERY-033` | GET `/api/v1/catalog/businesses?categoryId=bad`; ANON | Body: none | 400 categoryId field error | automated-testserver | planned |
| `FAN-BUSINESS-QUERY-034` | GET `/api/v1/catalog/businesses?language=fr`; ANON | Body: none | 400 `language_invalid` | automated-testserver | planned |

### 4.4 Reviews

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-REVIEW-CREATE-001` | PUT `/api/v1/bookings/{completedBookingId}/review`; CUST(P) owner | `{"rating":5,"comment":"Excellent service.","expectedRowVersion":null}` | 201; one row; count +1; rowVersion returned | automated-testserver | automated |
| `FAN-REVIEW-READ-002` | GET `/api/v1/bookings/{completedBookingId}/review`; CUST(P) owner | Body: none; review exists | 200; owned response includes bookingId and rowVersion | automated-testserver | automated |
| `FAN-REVIEW-UPDATE-003` | PUT `/api/v1/bookings/{completedBookingId}/review`; CUST(P) owner | `{"rating":4,"comment":"Updated.","expectedRowVersion":"{current}"}` | 200; count stable, average updated, new rowVersion | automated-testserver | automated |
| `FAN-REVIEW-DELETE-004` | DELETE `/api/v1/bookings/{bookingId}/review?expectedRowVersion={current}`; owner CUST(P) | Body: none; existing review | 204; row deleted and aggregate decremented | automated-testserver | automated |
| `FAN-REVIEW-DELETE-018` | GET `/api/v1/bookings/{bookingId}/review`; owner CUST(P) | Body: none; review deleted by `004` | 404 `review_not_found`; no deleted body | automated-testserver | automated |
| `FAN-REVIEW-ELIGIBILITY-005` | PUT `/api/v1/bookings/{pendingBookingId}/review`; owner CUST(P) | `{"rating":5,"comment":null,"expectedRowVersion":null}` | 409 `review_booking_not_completed` | automated-testserver | automated |
| `FAN-REVIEW-ELIGIBILITY-019` | PUT `/api/v1/bookings/{confirmedBookingId}/review`; owner CUST(P) | same exact body as `005` | 409 `review_booking_not_completed` | automated-testserver | automated |
| `FAN-REVIEW-ELIGIBILITY-020` | PUT `/api/v1/bookings/{inProgressBookingId}/review`; owner CUST(P) | same exact body as `005` | 409 `review_booking_not_completed` | automated-testserver | automated |
| `FAN-REVIEW-ELIGIBILITY-021` | PUT `/api/v1/bookings/{cancelledBookingId}/review`; owner CUST(P) | same exact body as `005` | 409 `review_booking_not_completed` | automated-testserver | automated |
| `FAN-REVIEW-ELIGIBILITY-022` | PUT `/api/v1/bookings/{noShowBookingId}/review`; owner CUST(P) | same exact body as `005` | 409 `review_booking_not_completed` | automated-testserver | automated |
| `FAN-REVIEW-OWNERSHIP-006` | GET `/api/v1/bookings/{foreignBookingId}/review`; CUST(P) | Body: none | non-disclosing 404; no review data | automated-testserver | planned |
| `FAN-REVIEW-OWNERSHIP-023` | PUT `/api/v1/bookings/{missingBookingId}/review`; CUST(P) | valid create review body | 404 `booking_not_found`; no aggregate change | automated-testserver | automated |
| `FAN-REVIEW-OWNERSHIP-024` | DELETE `/api/v1/bookings/{foreignBookingId}/review`; CUST(P) | Body: none | non-disclosing 404; no aggregate change | automated-testserver | automated |
| `FAN-REVIEW-OWNERSHIP-039` | PUT `/api/v1/bookings/{foreignBookingId}/review`; CUST(P) | valid create review body | identical 404 `booking_not_found`; zero mutation/aggregate change | automated-testserver | automated |
| `FAN-REVIEW-OWNERSHIP-040` | DELETE `/api/v1/bookings/{missingBookingId}/review`; CUST(P) | Body: none | identical 404 `booking_not_found`; zero mutation/aggregate change | automated-testserver | automated |
| `FAN-REVIEW-AUTH-007` | GET `/api/v1/bookings/{bookingId}/review`; ANON, no device | Body: none | 401 `device_token_missing` at required-device middleware | automated-testserver | automated |
| `FAN-REVIEW-AUTH-025` | PUT `/api/v1/bookings/{bookingId}/review`; Business JWT + valid device | valid review body | 401 bearer rejection; no row | automated-testserver | automated |
| `FAN-REVIEW-AUTH-026` | DELETE `/api/v1/bookings/{bookingId}/review`; ANON | Body: none | 401 `customer_authentication_required`; no mutation | automated-testserver | planned |
| `FAN-REVIEW-AUTH-041` | DELETE `/api/v1/bookings/{bookingId}/review`; valid Customer JWT, no device | Body: none | 401 `device_token_missing`; no mutation | automated-testserver | automated |
| `FAN-REVIEW-VALIDATION-008` | PUT `/api/v1/bookings/{bookingId}/review`; customer-user-jwt (Production; booking owner) | `{"rating":0,"comment":null,"expectedRowVersion":null}` | 400 `review_invalid`; rating field error | automated-testserver | planned |
| `FAN-REVIEW-VALIDATION-027` | PUT `/api/v1/bookings/{bookingId}/review`; customer-user-jwt (Production; booking owner) | body with `rating:6` | 400 `review_invalid`; rating field error | automated-testserver | planned |
| `FAN-REVIEW-VALIDATION-028` | PUT `/api/v1/bookings/{bookingId}/review`; customer-user-jwt (Production; booking owner) | body with `rating:4.5` | 400 `review_invalid`; rating field error | automated-testserver | planned |
| `FAN-REVIEW-VALIDATION-029` | PUT `/api/v1/bookings/{bookingId}/review`; customer-user-jwt (Production; booking owner) | body with 1001-character `comment` | 400 `review_invalid`; comment field error | automated-testserver | planned |
| `FAN-REVIEW-NORMALIZE-009` | PUT `/api/v1/bookings/{completedBookingId}/review`; CUST(P) owner | `{"rating":5,"comment":"   ","expectedRowVersion":null}` | 201; stored/returned comment is null | automated-testserver | automated |
| `FAN-REVIEW-CONCURRENCY-010` | PUT `/api/v1/bookings/{completedBookingId}/review`; first concurrent CUST(P) request | exact create body | one request returns 201 and creates one row | automated-testserver-sqlserver | automated |
| `FAN-REVIEW-CONCURRENCY-030` | PUT `/api/v1/bookings/{completedBookingId}/review`; second concurrent CUST(P) request | exact same create body; `010` is the winning create | 409 `review_version_conflict`; never a second row or unhandled server error | automated-testserver-sqlserver | automated |
| `FAN-REVIEW-CONCURRENCY-031` | PUT `/api/v1/bookings/{completedBookingId}/review`; CUST(P) owner | subsequent retry/update with stale `expectedRowVersion` from before the winning write | 409 `review_version_conflict`; row unchanged | automated-testserver | automated |
| `FAN-REVIEW-PUBLIC-011` | GET `/api/v1/catalog/businesses/{businessId}/reviews?page=1&pageSize=20`; ANON | Body: none; multiple dated reviews | 200; newest-first stable page and aggregates | automated-testserver | automated |
| `FAN-REVIEW-PAGING-012` | GET `/api/v1/catalog/businesses/{businessId}/reviews?page=1&pageSize=1`; ANON | Body: none | 200; first item and stable total | automated-testserver | automated |
| `FAN-REVIEW-PAGING-032` | GET `/api/v1/catalog/businesses/{businessId}/reviews?page=2&pageSize=1`; ANON | Body: none | 200; second distinct item, same total | automated-testserver | automated |
| `FAN-REVIEW-PAGING-033` | GET `/api/v1/catalog/businesses/{businessId}/reviews?page=1&pageSize=50`; ANON | Body: none | 200; 50-item first page plus distinct second page | automated-testserver | automated |
| `FAN-REVIEW-PAGING-013` | GET `/api/v1/catalog/businesses/{businessId}/reviews?page=0&pageSize=20`; ANON | Body: none | 400 `pagination_invalid` | automated-testserver | planned |
| `FAN-REVIEW-PAGING-034` | GET `/api/v1/catalog/businesses/{businessId}/reviews?page=1&pageSize=0`; ANON | Body: none | 400 `pagination_invalid` | automated-testserver | planned |
| `FAN-REVIEW-PAGING-035` | GET `/api/v1/catalog/businesses/{businessId}/reviews?page=1&pageSize=51`; ANON | Body: none | 400 `pagination_invalid` | automated-testserver | planned |
| `FAN-REVIEW-PAGING-036` | GET `/api/v1/catalog/businesses/{businessId}/reviews?page=text&pageSize=20`; ANON | Body: none | 400 `pagination_invalid` | automated-testserver | automated |
| `FAN-REVIEW-PRIVACY-014` | GET `/api/v1/catalog/businesses/{businessId}/reviews`; ANON | Body: none; rich customer/booking fixture | 200; omits booking/user/contact/address/plate/rowVersion | automated-testserver | automated |
| `FAN-REVIEW-LOCALIZATION-015` | GET `/api/v1/bookings/{missingId}/review?language=ar`; CUST(P) | Body: none | 404 stable code with Arabic title/detail/language | automated-testserver | automated |
| `FAN-REVIEW-LOCALIZATION-037` | GET `/api/v1/bookings/{missingId}/review?language=he`; CUST(P) | Body: none | same 404 code with Hebrew title/detail/language | automated-testserver | automated |
| `FAN-REVIEW-VERSION-042` | PUT create with supplied `expectedRowVersion` | valid 8-byte base64 | 409 `review_version_conflict`; no row | automated-testserver | automated |
| `FAN-REVIEW-VERSION-043` | PUT update with missing/malformed/wrong-length version | existing review | missing returns 409; malformed/wrong-length return 400; row unchanged | automated-testserver | automated |
| `FAN-REVIEW-VERSION-044` | PUT update with current version | SQL Server rowversion fixture | 200; stable created timestamp, later updated timestamp, new 8-byte rowversion | automated-testserver-sqlserver | automated |
| `FAN-REVIEW-CONCURRENCY-050` | Two PUT updates with the same current `expectedRowVersion` | SQL Server/TestServer synchronized review reads; distinct submitted values | exactly one 200 and one 409 `review_version_conflict`; no 500; final row is one submitted value with a new rowversion | automated-testserver-sqlserver | automated |
| `FAN-REVIEW-CONCURRENCY-051` | Two DELETE requests with the same current `expectedRowVersion` | SQL Server/TestServer synchronized review reads | exactly one 204 and one 409 `review_version_conflict`; no 404/500; final row absent and aggregate decremented once | automated-testserver-sqlserver | automated |
| `FAN-REVIEW-CONCURRENCY-052` | PUT update races DELETE using the same current `expectedRowVersion` | SQL Server/TestServer synchronized review reads | one winner; loser is stable 409 `review_version_conflict`; no 500; final row and aggregate match the winning operation | automated-testserver-sqlserver | automated |
| `FAN-REVIEW-VERSION-053` | Swagger DELETE review operation | Generated OpenAPI document | exposes `expectedRowVersion` as a query parameter | automated-testserver-sqlserver | automated |
| `FAN-REVIEW-PUBLIC-AUTH-045` | Public GET; malformed/unknown supplied device | optional-device route | 401 `device_token_invalid`; no anonymous fallback | automated-testserver | automated |
| `FAN-REVIEW-PUBLIC-AUTH-046` | Public GET; CUST(D)+DEV(P) | mismatched trusted credentials | 403 `data_partition_mismatch`; no cross-partition read | automated-testserver | automated |
| `FAN-REVIEW-BUSINESS-047` | Public GET; unknown/disabled/cross-partition business IDs | Arabic and Hebrew requests | 404 `catalog_business_not_found`; fully localized details | automated-testserver | automated |
| `FAN-REVIEW-MASKING-048` | Public GET and service mapping | null/blank/one-character/Arabic/Hebrew names | `***`, `***`, first-character masking without script corruption | automated-testserver+unit | automated |
| `FAN-REVIEW-AGGREGATE-049` | Repository aggregate batch | empty, duplicate, reviewed and zero-review IDs | empty safe; deduplicated mixed results; explicit zero aggregates | automated-sqlserver | automated |
| `FAN-REVIEW-PARTITION-016` | GET `/api/v1/catalog/businesses/{businessId}/reviews`; ANON | Body: none; same source ID in P/D | 200; Production rows/aggregates only | automated-testserver | automated |
| `FAN-REVIEW-PARTITION-038` | GET `/api/v1/catalog/businesses/{businessId}/reviews`; DEV(D) | Body: none | 200; Demo rows/aggregates only | automated-testserver | automated |
| `FAN-REVIEW-MIGRATION-017` | Customer migration | Existing bookings | Unique booking constraint, indexes/filter/rowVersion | migration | automated |

### 4.5 Banners

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-BANNER-PUBLIC-001` | GET `/api/v1/banners`; ANON | Active/inactive ordered fixtures | 200; only active; order then stable ID | automated-testserver | automated |
| `FAN-BANNER-PUBLIC-002` | GET `/api/v1/banners`; DEV(D) | Body: none; distinct Demo fixtures | 200; Demo-only banners | automated-testserver | automated |
| `FAN-BANNER-PUBLIC-003` | GET `/api/v1/banners`; malformed supplied device token | Body: none | 401 `device_token_invalid` | automated-testserver | automated |
| `FAN-BANNER-ADMIN-004` | POST `/api/v1/admin/banners`; ADMIN(P) | Valid body | 201; rowVersion/timestamps; public visibility if active | automated-testserver | automated |
| `FAN-BANNER-ADMIN-005` | GET `/api/v1/admin/banners`; ADMIN(P) | Active/inactive fixtures | 200; both returned; admin-only fields present | automated-testserver | automated |
| `FAN-BANNER-ADMIN-006` | PUT `/api/v1/admin/banners/{id}`; ADMIN(P) | exact update body with current rowVersion, new order, `isActive:false` | 200; public list changes | automated-testserver | automated |
| `FAN-BANNER-ADMIN-007` | DELETE `/api/v1/admin/banners/{id}?expectedRowVersion={current}`; ADMIN(P) | Body: none; existing banner | 204; row deleted | automated-testserver | automated |
| `FAN-BANNER-ADMIN-013` | GET `/api/v1/admin/banners`; ADMIN(P) | Body: none; after `007` | 200; deleted ID absent | automated-testserver | automated |
| `FAN-BANNER-ADMIN-014` | GET `/api/v1/banners`; ANON | Body: none; after `007` | 200; deleted ID absent | automated-testserver | automated |
| `FAN-BANNER-AUTH-008` | GET `/api/v1/admin/banners`; ANON | Body: none | 401 `customer_authentication_required` | automated-testserver | automated |
| `FAN-BANNER-AUTH-015` | POST `/api/v1/admin/banners`; User JWT | valid create body | 403 `customer_authorization_forbidden`; no row | automated-testserver | automated |
| `FAN-BANNER-AUTH-016` | PUT `/api/v1/admin/banners/{id}`; Company JWT | valid update body/current version | 403; row unchanged | automated-testserver | automated |
| `FAN-BANNER-AUTH-017` | DELETE `/api/v1/admin/banners/{id}?expectedRowVersion={current}`; Business JWT | Body: none | 401; row unchanged | automated-testserver | automated |
| `FAN-BANNER-VALIDATION-009` | POST `/api/v1/admin/banners`; ADMIN(P) | body with HTTP `imageUrl` | 400 `banner_invalid`; imageUrl field error | automated-testserver | automated |
| `FAN-BANNER-VALIDATION-018` | PUT `/api/v1/admin/banners/{id}`; ADMIN(P) | body with `displayOrder:10001` and current version | 400 `banner_invalid`; row unchanged | automated-testserver | automated |
| `FAN-BANNER-CONCURRENCY-010` | PUT `/api/v1/admin/banners/{id}`; ADMIN(P) | valid update body with stale `expectedRowVersion` | 409 `banner_version_conflict`; row unchanged | automated-testserver | automated |
| `FAN-BANNER-CONCURRENCY-019` | DELETE `/api/v1/admin/banners/{id}?expectedRowVersion={stale}`; ADMIN(P) | Body: none | 409 `banner_version_conflict`; row remains | automated-testserver | automated |
| `FAN-BANNER-PARTITION-011` | GET `/api/v1/banners`; DEV(P) | Production and Demo fixtures share the same display order | 200; Production banners only | automated-testserver | automated |
| `FAN-BANNER-PARTITION-020` | GET `/api/v1/banners`; DEV(D) | Production and Demo fixtures share the same display order | 200; Demo banners only | automated-testserver | automated |
| `FAN-BANNER-BOUNDARY-021` | POST `/api/v1/admin/banners`; ADMIN(P) | exact 500-character HTTPS URL and orders 0/10000, then 501-character URL/order 10001 | boundaries accepted; overflow 400; rejected requests do not mutate | automated-testserver | automated |
| `FAN-BANNER-BOUNDARY-022` | POST `/api/v1/admin/banners`; ADMIN(P) | credential-bearing URL, empty/missing host | 400 `banner_invalid`; no row | automated-testserver | automated |
| `FAN-BANNER-BODY-023` | POST `/api/v1/admin/banners`; ADMIN(P) | empty, JSON null, malformed JSON, text/plain, >64 KiB | stable 400/415/413; no mutation | automated-testserver | automated |
| `FAN-BANNER-VERSION-024` | PUT/DELETE; ADMIN(P) | missing and malformed expectedRowVersion | 400 `banner_invalid`; row preserved | automated-testserver | automated |
| `FAN-BANNER-DEVICE-025` | GET `/api/v1/banners`; unknown/disabled/expired/rotated/duplicate device token | Body: none | stable localized 401 device code; no Production fallback | automated-testserver | automated |
| `FAN-BANNER-PARTITION-026` | GET `/api/v1/banners`; Production device with colliding Demo order | Production and Demo banners | Production only | automated-testserver | automated |
| `FAN-BANNER-PARTITION-027` | GET `/api/v1/banners`; Demo JWT + Production device | Body: none | 403 `data_partition_mismatch` | automated-testserver | automated |
| `FAN-BANNER-NONDISCLOSURE-028` | PUT/DELETE nonexistent and cross-partition IDs; ADMIN(P) | current-shaped version | PUT 404; DELETE 409; hidden row preserved | automated-testserver | automated |
| `FAN-BANNER-LOCALIZATION-029` | invalid POST with `language=he` and caller correlation ID | invalid image URL | Hebrew Content-Language/title/detail; stable code and correlation ID | automated-testserver | automated |
| `FAN-BANNER-SAFE500-030` | GET `/api/v1/banners`; injected banner dependency failure | secret-bearing probe | safe localized 500; no-store/security headers/correlation; no exception, secret, or request leakage | automated-testserver | automated |
| `FAN-BANNER-SQL-031` | SQL-backed POST/PUT/DELETE; ADMIN(P) | current then stale/current versions | rowversion changes; stale delete 409; current delete 204 | automated-relational | automated |
| `FAN-BANNER-RACE-032` | SQL-backed parallel PUT/PUT | same current version | one 200, one stable 409, no 500, winner state/version persisted | automated-relational | automated |
| `FAN-BANNER-RACE-033` | SQL-backed parallel DELETE/DELETE | same current version | one 204, one stable 409, no 500, row absent | automated-relational | automated |
| `FAN-BANNER-RACE-034` | SQL-backed parallel PUT/DELETE | same current version | one winner, one stable 409, no 500, final state matches winner | automated-relational | automated |
| `FAN-BANNER-DEMO-035` | Demo reseed | mutated expected banner, deleted expected banner, unexpected Demo banner | restore, recreate, remove unexpected, repeated reseed idempotent | automated-relational | automated |
| `FAN-BANNER-MIGRATION-012` | Customer migration | Existing database | Required URL, rowVersion, order/active/filter indexes | migration | automated |

### 4.6 Optional device, direct pricing, and public reads

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-OPTIONAL-CONFIG-001` | GET `/api/v1/configuration`; ANON | Active Production config | 200 Production, localized; if no active record is provisioned, 503 `configuration_unavailable` with a localized "not configured yet" detail | automated-testserver + hosted-smoke | automated |
| `FAN-OPTIONAL-CATALOG-002` | GET `/api/v1/catalog/categories`; ANON | Body: none; Production catalog | 200; no missing-token error | automated-testserver | automated |
| `FAN-OPTIONAL-CATALOG-013` | GET `/api/v1/catalog/businesses`; ANON | Body: none; Production catalog | 200; no missing-token error | automated-testserver | automated |
| `FAN-OPTIONAL-CATALOG-014` | GET `/api/v1/catalog/businesses/{id}`; ANON | Body: none; Production business | 200; no missing-token error | automated-testserver | automated |
| `FAN-OPTIONAL-CATALOG-015` | GET `/api/v1/catalog/businesses/{id}/offerings`; ANON | Body: none; Production offerings | 200; no missing-token error | automated-testserver | automated |
| `FAN-OPTIONAL-CATALOG-016` | GET `/api/v1/catalog/offerings/{id}`; ANON | Body: none; Production offering | 200; no missing-token error | automated-testserver | automated |
| `FAN-OPTIONAL-CATALOG-SLOTS` | Retained alias for `STEP20-SLOTS-CUSTOMER-001`; POST `/api/v1/catalog/businesses/{businessId}/branches/{branchId}/available-slots`; ANON | Exact valid Production offering-aware body | 200 authoritative slots; no missing-token error | automated-testserver | automated |
| `FAN-OPTIONAL-PRICING-003` | POST `/api/v1/pricing/reprice`; ANON | Valid Production intent | 200 authoritative quote; no device/draft row | automated-testserver | automated |
| `FAN-OPTIONAL-READS-004` | GET `/api/v1/banners`; ANON | Body: none; Production fixtures | 200 Production banners | automated-testserver | automated |
| `FAN-OPTIONAL-READS-017` | GET `/api/v1/catalog/businesses/{businessId}/reviews?page=1&pageSize=20`; ANON | Body: none; Production fixtures | 200 Production reviews without a device token | automated-testserver | automated |
| `FAN-OPTIONAL-READS-018` | POST `/api/v1/catalog/businesses/availability-search`; ANON | exact valid availability body | 200 Production results or valid empty results | automated-testserver | automated |
| `FAN-OPTIONAL-DEMO-005` | GET `/api/v1/configuration`; DEV(D) | Body: none; active global config | 200 localized configuration; if no active global record is provisioned, 503 `configuration_unavailable` with a localized "not configured yet" detail | automated-testserver | automated |
| `FAN-OPTIONAL-DEMO-019` | GET `/api/v1/catalog/categories`; DEV(D) | Body: none | 200 Demo catalog only | automated-testserver | automated |
| `FAN-OPTIONAL-DEMO-020` | POST `/api/v1/pricing/reprice`; DEV(D) | exact valid Demo pricing body | 200 Demo authoritative quote | automated-testserver | automated |
| `FAN-OPTIONAL-DEMO-021` | GET `/api/v1/banners`; DEV(D) | Body: none | 200 Demo banners only | automated-testserver | automated |
| `FAN-OPTIONAL-DEMO-022` | GET `/api/v1/catalog/businesses/{businessId}/reviews`; DEV(D) | Body: none | 200 Demo reviews only | automated-testserver | automated |
| `FAN-OPTIONAL-DEMO-023` | POST `/api/v1/catalog/businesses/availability-search`; DEV(D) | exact valid Demo availability body | 200 Demo results only | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-006` | GET `/api/v1/configuration`; malformed supplied device token | Body: none | 401 `device_token_invalid`; never Production fallback | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-024` | GET `/api/v1/configuration`; unknown supplied device token | Body: none | 401 `device_token_invalid`; never Production fallback | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-025` | GET `/api/v1/configuration`; expired supplied device token | Body: none | 401 `device_token_expired`; never Production fallback | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-026` | GET `/api/v1/configuration`; inactive supplied device token | Body: none | 401 `device_token_inactive`; never Production fallback | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-027` | GET `/api/v1/configuration`; rotated supplied device token | Body: none | 401 `device_token_invalid`; never Production fallback | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-028` | GET `/api/v1/catalog/categories`; malformed supplied device token | Body: none | 401 `device_token_invalid`; never Production fallback | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-029` | GET `/api/v1/catalog/businesses`; malformed supplied device token | Body: none | 401 `device_token_invalid`; never Production fallback | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-030` | GET `/api/v1/catalog/businesses/{id}`; malformed supplied device token | Body: none | 401 `device_token_invalid`; never Production fallback | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-031` | GET `/api/v1/catalog/businesses/{id}/offerings`; malformed supplied device token | Body: none | 401 `device_token_invalid`; never Production fallback | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-032` | GET `/api/v1/catalog/offerings/{id}`; malformed supplied device token | Body: none | 401 `device_token_invalid`; never Production fallback | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-033` | POST `/api/v1/pricing/reprice`; malformed supplied device token | exact valid pricing body | 401 `device_token_invalid`; no upstream call/draft | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-034` | GET `/api/v1/banners`; malformed supplied device token | Body: none | 401 `device_token_invalid`; never Production fallback | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-035` | GET `/api/v1/catalog/businesses/{businessId}/reviews`; malformed or unknown supplied device token | Body: none | 401 `device_token_invalid`; never Production fallback | automated-testserver | automated |
| `FAN-OPTIONAL-INVALID-036` | POST `/api/v1/catalog/businesses/availability-search`; malformed supplied device token | exact valid availability body | 401 `device_token_invalid`; no internal call | automated-testserver | automated |
| `FAN-OPTIONAL-DUPLICATE-007` | GET `/api/v1/configuration`; duplicate `X-Device-Token` headers | Body: none | 401 `device_token_invalid`; never Production fallback | automated-testserver | automated |
| `FAN-OPTIONAL-JWT-008` | GET `/api/v1/catalog/businesses`; CUST(D), no device | Demo account | 200; Demo projection and favourites | automated-testserver | automated |
| `FAN-OPTIONAL-MISMATCH-009` | GET `/api/v1/catalog/businesses`; CUST(P)+DEV(D) | Valid mismatched identities | 403 partition mismatch | automated-testserver | automated |
| `FAN-OPTIONAL-REQUIRED-010` | POST `/api/v1/checkout/drafts`; ANON without device | exact valid draft body | 401 `device_token_missing`; no draft | automated-testserver | automated |
| `FAN-OPTIONAL-REQUIRED-039` | POST `/api/v1/bookings/from-draft`; CUST(P) without device | exact valid booking body | 401 `device_token_missing`; no booking | automated-testserver | automated |
| `FAN-OPTIONAL-REQUIRED-040` | POST `/api/v1/payments/intents`; CUST(P) without device | exact valid payment body | 401 `device_token_missing`; no payment | automated-testserver | automated |
| `FAN-OPTIONAL-RATELIMIT-011` | GET `/api/v1/catalog/businesses`; ANON | final request in an anonymous burst fixture | 429; anonymous partition limited without affecting device/JWT partitions | automated-testserver | automated |
| `FAN-OPTIONAL-RATELIMIT-037` | GET `/api/v1/catalog/businesses`; DEV(P) | final request in a device burst fixture | 429; device partition limited without affecting anonymous/JWT partitions | automated-testserver | automated |
| `FAN-OPTIONAL-RATELIMIT-038` | GET `/api/v1/catalog/businesses`; CUST(P) | final request in a customer-JWT burst fixture | 429; JWT partition limited without affecting anonymous/device partitions | automated-testserver | automated |
| `FAN-OPTIONAL-UPSTREAM-012` | GET `/api/v1/catalog/businesses`; ANON | Business catalog dependency unavailable | 503; explicit safe failure, never fabricated empty success | automated-testserver | automated |
| `FAN-OPTIONAL-UPSTREAM-041` | POST `/api/v1/pricing/reprice`; ANON | exact valid pricing body; Business dependency unavailable | 503; explicit safe failure, never fabricated quote | automated-testserver | automated |

### 4.7 Availability discovery and internal HMAC

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-AVAILABILITY-CUSTOMER-001` | POST `/api/v1/catalog/businesses/availability-search`; ANON | Valid type/date/time | 200 one best branch/business, advisory flag | automated-testserver | automated |
| `FAN-AVAILABILITY-CUSTOMER-002` | POST `/api/v1/catalog/businesses/availability-search`; CUST(P) | exact valid availability body; favourite/rating fixtures | 200; favourite/aggregates merged; ordering exact | automated-service + testserver identity matrix | automated |
| `FAN-AVAILABILITY-FILTER-003` | POST `/api/v1/catalog/businesses/availability-search`; ANON | exact valid body with category and in-area location | 200; only eligible candidates sent/returned | automated-service | automated |
| `FAN-AVAILABILITY-FILTER-004` | POST `/api/v1/catalog/businesses/availability-search`; ANON | valid body with out-of-area location | 200 with exact `results:[]`; no internal call or unrelated data | automated-testserver | automated |
| `FAN-AVAILABILITY-FILTER-019` | POST `/api/v1/catalog/businesses/availability-search`; ANON | valid body with category having no eligible business | 200 with exact `results:[]`; no fabricated candidates | automated-service | automated |
| `FAN-AVAILABILITY-VALIDATION-005` | POST `/api/v1/catalog/businesses/availability-search`; ANON | body with `vehicleType:"Car"` | 400; vehicleType field error; no internal call | contract + controller validation | automated |
| `FAN-AVAILABILITY-VALIDATION-020` | POST `/api/v1/catalog/businesses/availability-search`; ANON | body with past `date` | 400; date field error; no internal call | validator + controller validation | automated |
| `FAN-AVAILABILITY-VALIDATION-021` | POST `/api/v1/catalog/businesses/availability-search`; ANON | body with `preferredLocalTime:"25:00:00"` | 400; time field error; no internal call | model-binding integration | automated |
| `FAN-AVAILABILITY-VALIDATION-022` | POST `/api/v1/catalog/businesses/availability-search`; ANON | body with malformed `categoryId` | 400; categoryId field error; no internal call | model-binding integration | automated |
| `FAN-AVAILABILITY-VALIDATION-023` | POST `/api/v1/catalog/businesses/availability-search`; ANON | body with latitude but no longitude | 400; location field error; no internal call | validator | automated |
| `FAN-AVAILABILITY-VALIDATION-024` | POST `/api/v1/catalog/businesses/availability-search`; ANON | body with latitude 91 and longitude 181 | 400; location field errors; no internal call | validator | automated |
| `FAN-AVAILABILITY-LOCALIZATION-006` | POST `/api/v1/catalog/businesses/availability-search?language=ar`; ANON | exact valid body; missing Hebrew catalog values | 200; Arabic values; slot facts invariant | catalog projection + availability merge | automated |
| `FAN-AVAILABILITY-LOCALIZATION-033` | POST `/api/v1/catalog/businesses/availability-search?language=he`; ANON | exact valid body; missing Hebrew catalog values | 200; Hebrew values with Arabic fallback; slot facts invariant | automated-service | automated |
| `FAN-AVAILABILITY-PARTITION-007` | POST `/api/v1/catalog/businesses/availability-search`; DEV(D) | exact valid availability body; Demo catalog/schedules | 200; signed Demo internal call; Demo-only results | automated-testserver | automated |
| `FAN-AVAILABILITY-FAILURE-008` | POST `/api/v1/catalog/businesses/availability-search`; ANON | valid body; internal client times out | 503 `availability_unavailable`; no `results` success body | automated-service/client | automated |
| `FAN-AVAILABILITY-FAILURE-025` | POST `/api/v1/catalog/businesses/availability-search`; ANON | valid body; Business returns 500 | 503 `availability_unavailable`; no fabricated empty success | automated-service/client | automated |
| `FAN-AVAILABILITY-FAILURE-026` | POST `/api/v1/catalog/businesses/availability-search`; ANON | valid body; Business returns malformed/empty success | 502 `availability_unavailable`; no fabricated empty success | automated-client/service | automated |
| `FAN-AVAILABILITY-FAILURE-027` | POST `/api/v1/catalog/businesses/availability-search`; ANON | valid body; Business response exceeds bound | 502 `availability_unavailable`; no fabricated empty success | automated-client | automated |
| `FAN-AVAILABILITY-INTERNAL-009` | POST `/api/v1/internal/appointments/availability-discovery`; HMAC(P) | exact v1 body with two company candidates, each containing one fixture branch ID | 200; exactly two results in request order; each result selects its candidate fixture branch exactly once | automated-testserver + live manifest | passed |
| `FAN-AVAILABILITY-INTERNAL-010` | POST `/api/v1/internal/appointments/availability-discovery`; no HMAC headers | exact valid v1 body | 401 `internal_auth_missing_header`; no data | automated-testserver + live manifest | passed |
| `FAN-AVAILABILITY-INTERNAL-025` | POST `/api/v1/internal/appointments/availability-discovery`; HMAC(P) with one nibble mutated after generating an otherwise valid signature | exact valid v1 body | 401 `internal_auth_invalid_signature`; no data | automated-testserver | automated |
| `FAN-AVAILABILITY-INTERNAL-026` | POST `/api/v1/internal/appointments/availability-discovery`; HMAC(P) timestamp older than skew window | exact valid v1 body | 401 `internal_auth_timestamp_out_of_range`; no data | automated-testserver + live manifest | passed |
| `FAN-AVAILABILITY-INTERNAL-027` | POST `/api/v1/internal/appointments/availability-discovery`; HMAC(P) with fresh fixed nonce | exact valid v1 body | 200; nonce persisted for replay check | automated-testserver + live manifest | passed |
| `FAN-AVAILABILITY-INTERNAL-028` | POST `/api/v1/internal/appointments/availability-discovery`; repeat exact HMAC/body from `027` | exact same v1 body | 401 `internal_auth_replay_nonce`; no second processing | automated-testserver + live manifest | passed |
| `FAN-AVAILABILITY-INTERNAL-011` | POST `/api/v1/internal/appointments/availability-discovery`; valid HMAC service without operation grant | exact valid v1 body | 403 `internal_service_forbidden`; no data | automated-testserver + live manifest | passed |
| `FAN-AVAILABILITY-INTERNAL-012` | POST `/api/v1/internal/appointments/availability-discovery` over the live-local HTTP listener; valid HMAC(P) | Exact manifest body: `{"contractVersion":"v1","date":"{{var:availabilityDate}}","preferredLocalTime":"10:30:00","candidates":[{"companyId":"{{var:productionBusinessSourceId}}","branchIds":["{{var:productionBranchSourceId}}"]},{"companyId":"{{var:secondProductionBusinessSourceId}}","branchIds":["{{var:secondProductionBranchSourceId}}"]}]}`; HTTP listener is deliberately enabled only for this non-Production transport rejection check | 403 `https_required`; Problem Details content type; correlation ID echoed; no secret, connection string, stack, or exception leakage | shared middleware automated + live manifest | passed |
| `FAN-AVAILABILITY-INTERNAL-013` | POST `/api/v1/internal/appointments/availability-discovery`; HMAC(P) | malformed candidate JSON body | 400; no domain mutation or data leak | automated-testserver | automated |
| `FAN-AVAILABILITY-INTERNAL-029` | POST `/api/v1/internal/appointments/availability-discovery`; HMAC(P) | exact v1 body with `candidates:[]` | 400 candidate field error; no data leak | validator | automated |
| `FAN-AVAILABILITY-INTERNAL-030` | POST `/api/v1/internal/appointments/availability-discovery`; HMAC(P) | exact v1 body with duplicate company/branch candidate | 400 candidate field error; no data leak | validator | automated |
| `FAN-AVAILABILITY-INTERNAL-031` | POST `/api/v1/internal/appointments/availability-discovery`; HMAC(P) | JSON body exceeding 65,536 bytes | 413 `internal_request_body_too_large`; no data leak | automated-testserver | automated |
| `FAN-AVAILABILITY-SCHEDULE-014` | internal | recurring schedule/capacity | Nearest aligned free configured slot | service/repository | automated |
| `FAN-AVAILABILITY-OVERRIDE-015` | internal | active closure/date override | Override replaces recurring schedule | service/repository | automated |
| `FAN-AVAILABILITY-TIME-016` | POST `/api/v1/internal/appointments/availability-discovery`; HMAC(P) | exact boundary-time candidate body | 200; fail-closed, deterministic local/UTC result | automated-service | automated |
| `FAN-AVAILABILITY-CAPACITY-017` | POST `/api/v1/internal/appointments/availability-discovery`; HMAC(P) | exact candidate body with overlapping reservations fixture | 200; correct remaining capacity | service + SQL Server relational | automated |
| `FAN-AVAILABILITY-ADVISORY-018` | POST `/api/v1/catalog/businesses/availability-search`; ANON | exact valid discovery body before offering selection | 200 advisory result; creates no reservation/capacity claim | testserver + SQL Server relational | automated |
| `FAN-AVAILABILITY-ADVISORY-032` | POST `/api/v1/catalog/businesses/{businessId}/branches/{branchId}/available-slots`; DEV(P) | exact existing offering-aware body using branch returned by `018` | 200; detailed offering-aware slots remain authoritative and discovery does not bypass this check | existing automated detailed-slot suite + discovery no-offering test | automated |

### 4.8 Swagger, migrations, regression, and isolation

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-CONTRACT-SWAGGER-001` | GET `/swagger/v1/swagger.json`; ANON on Customer host | Body: none; Development/Swagger enabled | 200; exact new Customer paths/schemas/statuses and optional device security | automated-live | planned |
| `FAN-CONTRACT-SWAGGER-CURRENT-003` | GET `/swagger/v1/swagger.json`; ANON on Customer host | Run manifest with tag `current-slice`; inspect the `GET /api/v1/catalog/businesses` query parameter schemas/descriptions and favourite PUT/DELETE operations | 200; `search` is an optional nullable string with `maxLength: 100` and documents trimmed, Unicode Form C normalized, case-insensitive Arabic/Hebrew business-name matching; `top` is an optional nullable `int32` with enum values `5` and `10` and documents the average-rating, rating-count, display-order, Arabic-name, stable-ID ranking applied before limiting; favourite PUT/DELETE each require CustomerBearer, optionally composed with DeviceToken, and expose exactly 204/401/403/404 without requiring future banner/availability routes | automated-live | automated |
| `FAN-CONTRACT-SWAGGER-002` | GET `/swagger/v1/swagger.json`; ANON on Business host | Body: none; Development/Swagger enabled | 200; category metadata and internal availability path/HMAC quartet | automated-live | planned |
| `FAN-CONTRACT-SWAGGER-003` | GET Customer `/swagger/v1/swagger.json`; ANON | Body: none | 200; no Business management/internal routes and no secret/PII examples | automated-testserver | planned |
| `FAN-CONTRACT-SWAGGER-011` | GET Business `/swagger/v1/swagger.json`; ANON | Body: none | 200; no Customer profile/vehicle/payment routes and no secret/PII examples | automated-testserver | planned |
| `FAN-CONTRACT-SWAGGER-012` | GET Customer `/swagger/v1/swagger.json`; anonymous | Body: none; TestServer host environment is `Production` and `Swagger:Enabled` is absent | 404; Swagger endpoint is not mapped, no OpenAPI document is returned, and response contains no route/schema or secret/PII content | automated-testserver | planned |
| `FAN-CONTRACT-SERIALIZATION-004` | Contract tests | All enum values/null optionals | Exact string enums, casing, nullability, no integer enums | automated | automated |
| `FAN-CONTRACT-MIGRATION-005` | Customer migration suite | Upgrade/rollback/model snapshot | New roots/columns/indexes/filter/legacy mapping correct | migration | planned |
| `FAN-CONTRACT-MIGRATION-006` | Business migration suite | Upgrade/rollback/model snapshot | Category and work-order snapshot columns correct | migration | planned |
| `FAN-CUSTOMER-SOURCE-IDENTITY-MIGRATION-014` | Customer migration suite | Upgrade populated legacy catalog rows into partition-safe source indexes | Existing provider/category IDs, source IDs, partition, and relationships are preserved | migration | automated |
| `FAN-CUSTOMER-SOURCE-IDENTITY-MIGRATION-015` | Customer migration suite | Insert matching source company/category IDs in Production and Demo after upgrade | Partition-safe indexes accept both populated partitions | migration | automated |
| `FAN-CUSTOMER-SOURCE-IDENTITY-MIGRATION-016` | Customer migration suite | Downgrade with cross-partition duplicate `SourceCompanyId` or `SourceCategoryId` | Stable SQL error `51001`; rows, partition-safe indexes, and migration history remain unchanged | migration | automated |
| `FAN-CUSTOMER-SOURCE-IDENTITY-MIGRATION-017` | Customer migration suite | Downgrade populated catalog without duplicate source IDs | Old global unique indexes are restored and populated rows are preserved | migration | automated |
| `FAN-CONTRACT-STARTUP-007` | GET `/api/Health/db`; ANON on independently started Customer host | Body: none; only Customer DB/config present | 200 healthy; no Business DB dependency | automated-live | planned |
| `FAN-CONTRACT-STARTUP-010` | GET `/api/health`; ANON on independently started Business host | Body: none; only Business DB/config present | 200 healthy; no Customer DB dependency | automated-live | planned |
| `FAN-CONTRACT-REGRESSION-008` | Full affected tests | Existing routes | Draft/booking/payment/HMAC/catalog/availability behavior retained | automated | planned |
| `FAN-CONTRACT-SECURITY-009` | Contract assertion over the negative HTTP scenario result set | All negative TestServer and live scenario response captures | No tokens, signatures, SQL, PII, or booking IDs in captured public response fields | automated-contract | planned |

## 5. Code test plan

- Validator tests for vehicle enum/image, category URL/color, review rating and
  comment, banner URL/order, search/top, paging, and availability input.
- Customer repository tests for favourite uniqueness/idempotency, review
  concurrency and aggregates, banner ordering/state, and partition filters.
- Business repository/service tests for category version increments and
  advisory slot generation across schedules, overrides, DST, horizon, lead
  time, and reservation states.
- Neutral contract serialization tests for all vehicle enum values, category
  metadata, image snapshots, and availability v1.
- Customer and Business migration/model-snapshot tests including legacy vehicle
  normalization, deterministic partition-safe catalog source identity downgrade
  guards, populated-row preservation, and owning-DbContext-only changes.
- Customer TestServer tests for every changed route, optional-device middleware,
  optional JWT composition, Admin policy, localization, errors, Swagger, and
  no leakage.
- Business TestServer tests for category CRUD, catalog snapshot propagation,
  internal HMAC operation authorization, HTTPS, replay, and response mapping.
- Existing direct-pricing, draft, booking, payment, catalog-refresh,
  available-slots, internal HMAC, partition, and host-isolation regressions.

## 6. Live-local setup and cleanup

The manifest expects local-only variables/environment values for base URLs,
Production/Demo device tokens, Customer/Admin JWTs, catalog IDs, completed and
non-completed booking IDs, row versions, and HMAC service configuration.
Unresolved IDs deliberately remain `{{var:...}}` placeholders. They must be
supplied by a disposable fixture initializer or a gitignored
`*.local.json`; they must never be replaced by committed real credentials.

Mutations must use unique fixture data. Cleanup order is review/favourite,
vehicle/banner, Customer fixtures, then Business fixtures. Cleanup is
idempotent and must never target Production. Raw results stay under
`scripts/http-tests/artifacts/` and remain uncommitted.

## 7. QA blocking rules

A slice is blocked from completion when any of the following applies:

- required unit, repository, migration, contract, TestServer, or live scenario
  is missing, still planned/automated without current evidence, failed, or
  deferred without an explicit non-shipping rationale and compensating test;
- either API cannot start against its own disposable database;
- a migration or model snapshot is missing, destructive, cross-owned, or does
  not preserve Production/Demo filters and automatic assignment;
- a supplied invalid optional device token falls back to anonymous Production;
- favourites, reviews, banners, ratings, or availability leak across users,
  businesses, or partitions;
- public reviews expose booking/customer/contact/vehicle PII;
- Swagger differs from the implemented route/auth/enum/nullability contract;
- vehicle type changes price/eligibility, availability discovery is treated as
  authoritative, or the existing offering-aware slot check is weakened;
- any warning/error, unstable ordering, unexplained status/code, concurrency
  `500`, secret/PII leak, or missing cleanup remains unresolved.

After every implementation slice, independent QA must rerun the smallest
complete affected suite plus that slice's live manifest tags. Final completion
requires `dotnet test`, Release build, both local health checks, the complete
manifest, exact pass/fail/skip counts, and a fresh independent QA regression
pass. No scenario in this plan is considered passed until that evidence exists.
