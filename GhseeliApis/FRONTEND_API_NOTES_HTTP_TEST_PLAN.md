# Frontend API Notes HTTP Test Plan

Date: 2026-09-24

Status: **Planned contract/test slice only.** No runtime implementation or HTTP
result is claimed by this document.

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

Execution level: **TestServer and repeatable live local HTTP** against
disposable Customer and Business SQL Server databases. Production execution is
prohibited. The committed live-local input is
`scripts/http-tests/plans/frontend-api-notes.manifest.json`.

All scenarios below are `planned`. Implementation work must add the stated
lower-level/TestServer coverage, progressively enable the manifest entries,
execute them, and record sanitized evidence in a separate results document.

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
   Customer routes. Bearer tokens never authorize internal routes.
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
| DELETE | `/api/v1/bookings/{bookingId}/review` | Customer JWT | Delete caller's review |

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

Update adds required `expectedRowVersion`. `imageUrl` is nullable or absolute
HTTPS with maximum 500 characters; `displayOrder` is 0..10000. Admin responses
also include `isActive`, UTC timestamps, and `rowVersion`. POST returns `201`,
PUT `200`, DELETE `204`; stale versions return
`409 banner_version_conflict`. Public reads never expose inactive banners,
row versions, or timestamps.

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
POST /api/v1/catalog/businesses/availability-search
```

No `X-Device-Token` means Production and is not an authentication error. A
valid Demo device selects Demo. A bad supplied token returns the existing
localized device-token problem. Draft, booking, and payment routes retain
their current required-device rules.

Direct pricing keeps its current checkout-intent envelope, adds vehicle
`imageUrl`, no longer requires or persists a device ID, remains stateless, and
returns `Cache-Control: no-store`. Anonymous requests cannot access or mutate
drafts.

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
stable catalog order, then stable ID. No result is an empty successful list;
upstream failure returns `503 availability_unavailable`.

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

### 4.1 Vehicle and propagation

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-VEHICLE-CRUD-001` | POST `/api/Vehicles`; CUST(P) | Valid enum, HTTPS image | 201; response round-trips type/image; one Production row | both | planned |
| `FAN-VEHICLE-CRUD-002` | GET list/detail; owner CUST(P) | Created vehicle | 200; both projections contain identical type/image | both | planned |
| `FAN-VEHICLE-CRUD-003` | PUT `/api/Vehicles/{id}`; owner | Change enum/image to null | 200; persisted update, no duplicate | both | planned |
| `FAN-VEHICLE-CRUD-004` | DELETE then GET; owner | Existing vehicle | 204 then 404 | both | planned |
| `FAN-VEHICLE-AUTH-005` | all vehicle routes; ANON/wrong owner | Missing JWT or foreign ID | 401 or non-disclosing 404; no mutation | TestServer/live | planned |
| `FAN-VEHICLE-VALIDATION-006` | POST/PUT; CUST(P) | Missing/integer/unknown/case-changed enum | 400 `vehicle_type_invalid`; field error | both | planned |
| `FAN-VEHICLE-VALIDATION-007` | POST/PUT; CUST(P) | HTTP/relative/userinfo/malformed/>500 image URL | 400 `vehicle_image_url_invalid` | both | planned |
| `FAN-VEHICLE-PARTITION-008` | CRUD; CUST(D) | Demo fixture | Demo-only visibility; Production caller gets 404 | both | planned |
| `FAN-VEHICLE-DRAFT-009` | POST/PUT draft; DEV(P) | Stable type and image | Draft intent response/persistence round-trip | both | planned |
| `FAN-VEHICLE-PRICING-010` | POST direct reprice; ANON | Same intent across all five types | Same eligibility/price; normalized type/image returned | both | planned |
| `FAN-VEHICLE-BOOKING-011` | confirm booking; DEV+CUST | Repriced draft | Booking and internal reservation snapshot type/image | TestServer | planned |
| `FAN-VEHICLE-WORKORDER-012` | Business work-order read fixture | Confirmed reservation | Business snapshot contains type/image, no Customer entity access | integration | planned |
| `FAN-VEHICLE-MIGRATION-013` | Customer/Business migration tests | `Car`, `SUV`, unknown, null images | Required mapping and new nullable columns/index/model snapshots | migration | planned |
| `FAN-VEHICLE-REGRESSION-014` | pricing/booking regressions | Existing Sedan-equivalent fixture | Totals, availability, ownership unchanged | automated | planned |

### 4.2 Category metadata and catalog synchronization

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-CATEGORY-BUSINESS-001` | POST Business category; Owner JWT | Valid image/color | 201; uppercase color; version +1 | both | planned |
| `FAN-CATEGORY-BUSINESS-002` | PUT category; Owner JWT | Change/clear fields with current version | 200; version +1; snapshot hash changes | both | planned |
| `FAN-CATEGORY-VALIDATION-003` | POST/PUT | Invalid URL/color | 400 `catalog_category_presentation_invalid`; no version bump | both | planned |
| `FAN-CATEGORY-AUTH-004` | Business category mutations | Employee/foreign company/Customer JWT | 403/404/401; no mutation | TestServer | planned |
| `FAN-CATEGORY-INTERNAL-005` | GET internal snapshot; HMAC(P) | Category with metadata | 200; v1 includes nullable image/color | both | planned |
| `FAN-CATEGORY-INTERNAL-006` | snapshot; missing/bad HMAC | Same | 401 stable internal code; no body leak | both | planned |
| `FAN-CATEGORY-CUSTOMER-007` | GET categories; ANON | Synchronized Production catalog | 200; metadata preserved | both | planned |
| `FAN-CATEGORY-LOCALIZATION-008` | GET categories ar/he | Hebrew missing/present | Names follow fallback; image/color invariant | both | planned |
| `FAN-CATEGORY-PARTITION-009` | GET categories; DEV(D) vs ANON | Distinct Demo metadata | Exact partition isolation | both | planned |
| `FAN-CATEGORY-MIGRATION-010` | Both migrations | Existing categories | Nullable columns, filters, lengths, no destructive data | migration | planned |
| `FAN-CATEGORY-REGRESSION-011` | refresh/parity tests | Same version/hash and changed hash | Correct no-op/reapply behavior | integration | planned |

### 4.3 Business search, top, aggregates, and favourites

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-BUSINESS-BROWSE-001` | GET businesses; ANON | No filters | 200; `isFavourite=false`; aggregates present | both | planned |
| `FAN-BUSINESS-SEARCH-002` | GET `?search=<Arabic>`; ANON | Mixed-case/whitespace fixtures | Case-insensitive trimmed match | both | planned |
| `FAN-BUSINESS-SEARCH-003` | GET `?search=<Hebrew>`; ANON | Hebrew name fixture | Match independent of selected display language | both | planned |
| `FAN-BUSINESS-SEARCH-004` | GET search; ANON | Empty/no match/>100 | Empty acts absent; no match empty 200; overlong 400 | both | planned |
| `FAN-BUSINESS-TOP-005` | GET `?top=5` and `?top=10` | Rating/tie fixtures | Exact count and ranking/tie rules | both | planned |
| `FAN-BUSINESS-TOP-006` | GET `?top=0|1|6|11|text` | None | 400 `catalog_top_invalid` | both | planned |
| `FAN-BUSINESS-FILTER-007` | search/top with branch/category | Cross-provider fixtures | Existing filter semantics and mismatch code retained | both | planned |
| `FAN-BUSINESS-FAVOURITE-008` | PUT favourite; CUST(P) | Active target | 204; one row; repeat remains 204/no duplicate | both | planned |
| `FAN-BUSINESS-FAVOURITE-009` | GET list/detail/offerings; CUST(P) | Favourite exists | Correct `isFavourite=true` only for target | both | planned |
| `FAN-BUSINESS-FAVOURITE-010` | DELETE favourite twice; CUST(P) | Existing then absent | Both 204; row absent | both | planned |
| `FAN-BUSINESS-FAVOURITE-011` | PUT/DELETE; ANON/Business JWT | None | 401 `customer_authentication_required` | both | planned |
| `FAN-BUSINESS-FAVOURITE-012` | PUT; CUST(P) | Unknown/inactive/Demo target | Non-disclosing 404; no row | both | planned |
| `FAN-BUSINESS-FAVOURITE-013` | GET; CUST(P)+DEV(D) | Mismatched identities | 403 `data_partition_mismatch` | both | planned |
| `FAN-BUSINESS-PARTITION-014` | ranking/favourite; P vs D | Different reviews/favourites | No cross-partition aggregate or favourite leakage | both | planned |
| `FAN-BUSINESS-QUERY-015` | list query | Duplicate/malformed GUID/language params | 400 stable binding/language errors | TestServer/live | planned |

### 4.4 Reviews

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-REVIEW-CREATE-001` | PUT owned review; CUST(P) | Owned Completed booking, rating 5 | 201; one review; aggregate count +1 | both | planned |
| `FAN-REVIEW-READ-002` | GET owned review; owner | Existing review | 200; booking ID allowed only on owned response | both | planned |
| `FAN-REVIEW-UPDATE-003` | PUT; owner | Current rowVersion, changed rating/comment | 200; count stable; average updated | both | planned |
| `FAN-REVIEW-DELETE-004` | DELETE; owner | Existing review | 204; subsequent GET 404; aggregate decremented | both | planned |
| `FAN-REVIEW-ELIGIBILITY-005` | PUT | Pending/Confirmed/InProgress/Cancelled/NoShow | 409 `review_booking_not_completed` | both | planned |
| `FAN-REVIEW-OWNERSHIP-006` | GET/PUT/DELETE | Foreign or missing booking | Non-disclosing 404; no aggregate change | both | planned |
| `FAN-REVIEW-AUTH-007` | owned routes; ANON/Business JWT | None | 401 Customer auth problem | both | planned |
| `FAN-REVIEW-VALIDATION-008` | PUT | rating 0/6/non-integer; comment >1000 | 400 `review_invalid`; field errors | both | planned |
| `FAN-REVIEW-NORMALIZE-009` | PUT | whitespace comment | Stored/returned null | TestServer/live | planned |
| `FAN-REVIEW-CONCURRENCY-010` | concurrent create/update | Same booking/stale rowVersion | One row; loser 409, never 500 | repository/TestServer | planned |
| `FAN-REVIEW-PUBLIC-011` | GET public reviews; ANON | Multiple dated reviews | Newest-first deterministic page and aggregates | both | planned |
| `FAN-REVIEW-PAGING-012` | GET page boundaries | page 1/2, size 1/50 | Stable total/count/no duplicates | both | planned |
| `FAN-REVIEW-PAGING-013` | GET invalid paging | page 0, size 0/51/text | 400 `pagination_invalid` | both | planned |
| `FAN-REVIEW-PRIVACY-014` | GET public reviews | Rich booking/customer fixture | No booking/user/contact/address/plate/rowVersion leakage | both | planned |
| `FAN-REVIEW-LOCALIZATION-015` | GET public/owned ar/he errors | Missing target/invalid request | Stable code, localized message; comment unchanged | both | planned |
| `FAN-REVIEW-PARTITION-016` | public/owned P vs D | Same business source ID | Isolated rows and aggregates | both | planned |
| `FAN-REVIEW-MIGRATION-017` | Customer migration | Existing bookings | Unique booking constraint, indexes/filter/rowVersion | migration | planned |

### 4.5 Banners

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-BANNER-PUBLIC-001` | GET banners; ANON | Active/inactive ordered fixtures | Only active; order then stable ID | both | planned |
| `FAN-BANNER-PUBLIC-002` | GET; DEV(D) | Distinct Demo fixtures | Demo-only banners | both | planned |
| `FAN-BANNER-PUBLIC-003` | GET; invalid supplied device | None | 401 `device_token_invalid` | both | planned |
| `FAN-BANNER-ADMIN-004` | POST admin banners; ADMIN(P) | Valid body | 201; rowVersion/timestamps; public visibility if active | both | planned |
| `FAN-BANNER-ADMIN-005` | GET admin banners; ADMIN(P) | Active/inactive fixtures | Both returned; admin-only fields present | both | planned |
| `FAN-BANNER-ADMIN-006` | PUT; ADMIN(P) | Current version, reorder/deactivate | 200; public list changes | both | planned |
| `FAN-BANNER-ADMIN-007` | DELETE; ADMIN(P) | Existing/current version | 204 then public/admin absent | both | planned |
| `FAN-BANNER-AUTH-008` | admin CRUD; ANON/User/Company/Business JWT | None | 401/403; no mutation | both | planned |
| `FAN-BANNER-VALIDATION-009` | POST/PUT | bad URL/order | 400 `banner_invalid`; field errors | both | planned |
| `FAN-BANNER-CONCURRENCY-010` | PUT/DELETE | stale rowVersion | 409 `banner_version_conflict` | both | planned |
| `FAN-BANNER-PARTITION-011` | admin/public P vs D | Same display order | Exact isolation | both | planned |
| `FAN-BANNER-MIGRATION-012` | Customer migration | Existing database | Nullable URL, rowVersion, order/active/filter indexes | migration | planned |

### 4.6 Optional device, direct pricing, and public reads

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-OPTIONAL-CONFIG-001` | GET configuration; ANON | Production config | 200 Production, localized | both | planned |
| `FAN-OPTIONAL-CATALOG-002` | each public catalog GET; ANON | Production catalog | 200/no missing-token error | both | planned |
| `FAN-OPTIONAL-PRICING-003` | POST direct pricing; ANON | Valid Production intent | 200 authoritative quote; no device/draft row | both | planned |
| `FAN-OPTIONAL-READS-004` | banners/reviews/availability; ANON | Production fixtures | 200 Production | both | planned |
| `FAN-OPTIONAL-DEMO-005` | all optional routes; DEV(D) | Demo fixtures | Demo partition selected | both | planned |
| `FAN-OPTIONAL-INVALID-006` | all optional routes | malformed/unknown/expired/inactive/rotated token | 401, never fallback to Production | both | planned |
| `FAN-OPTIONAL-DUPLICATE-007` | all optional routes | duplicate token headers | 401 `device_token_invalid` | TestServer/live | planned |
| `FAN-OPTIONAL-JWT-008` | browse; CUST(D), no device | Demo account | Demo projection and favourites | both | planned |
| `FAN-OPTIONAL-MISMATCH-009` | browse; CUST(P)+DEV(D) | Valid mismatched identities | 403 partition mismatch | both | planned |
| `FAN-OPTIONAL-REQUIRED-010` | drafts/bookings/payments | Missing device | Existing 401 `device_token_missing` retained | regression | planned |
| `FAN-OPTIONAL-RATELIMIT-011` | anonymous/device/JWT requests | Burst fixtures | Separate stable rate-limit partitions; no shared anonymous/device key leak | TestServer/live | planned |
| `FAN-OPTIONAL-UPSTREAM-012` | catalog/pricing/availability | Business unavailable/timeout/bad body | Safe 502/503; never empty fabricated success | both | planned |

### 4.7 Availability discovery and internal HMAC

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-AVAILABILITY-CUSTOMER-001` | POST search; ANON | Valid type/date/time | 200 one best branch/business, advisory flag | both | planned |
| `FAN-AVAILABILITY-CUSTOMER-002` | POST; CUST(P) | Favourite/rating fixtures | Favourite/aggregates merged; ordering exact | both | planned |
| `FAN-AVAILABILITY-FILTER-003` | POST | category and in-area location | Only eligible candidates sent/returned | both | planned |
| `FAN-AVAILABILITY-FILTER-004` | POST | out-of-area/no category match | 200 empty results; no unrelated data | both | planned |
| `FAN-AVAILABILITY-VALIDATION-005` | POST | bad enum/date/time/GUID/partial/out-of-range location | 400 stable field errors; no internal call | both | planned |
| `FAN-AVAILABILITY-LOCALIZATION-006` | POST ar/he | Missing Hebrew catalog values | Correct fallback; slot facts invariant | both | planned |
| `FAN-AVAILABILITY-PARTITION-007` | POST; DEV(D) | Demo catalog/schedules | Signed Demo internal call; Demo-only results | both | planned |
| `FAN-AVAILABILITY-FAILURE-008` | POST | internal timeout/5xx/invalid/oversized body | 502/503 `availability_unavailable`; no internals | both | planned |
| `FAN-AVAILABILITY-INTERNAL-009` | POST internal; HMAC(P) | Multiple companies/branches | 200; one nearest eligible branch per company | both | planned |
| `FAN-AVAILABILITY-INTERNAL-010` | internal | missing/bad/stale/replayed HMAC | 401 stable internal auth code | both | planned |
| `FAN-AVAILABILITY-INTERNAL-011` | internal | valid HMAC without operation grant | 403 `internal_auth_forbidden` | both | planned |
| `FAN-AVAILABILITY-INTERNAL-012` | internal over HTTP | Development HTTP not enabled | 403 `https_required` | live | planned |
| `FAN-AVAILABILITY-INTERNAL-013` | internal | malformed/empty/duplicate/oversized candidates | 400/413; no data leak | both | planned |
| `FAN-AVAILABILITY-SCHEDULE-014` | internal | recurring schedule/capacity | Nearest aligned free configured slot | service/repository | planned |
| `FAN-AVAILABILITY-OVERRIDE-015` | internal | active closure/date override | Override replaces recurring schedule | service/repository | planned |
| `FAN-AVAILABILITY-TIME-016` | internal | lead-time/horizon/DST/overnight boundaries | Fail-closed, deterministic local/UTC result | service/TestServer | planned |
| `FAN-AVAILABILITY-CAPACITY-017` | internal | overlapping active vs terminal reservations | Correct remaining capacity | repository/TestServer | planned |
| `FAN-AVAILABILITY-ADVISORY-018` | customer then existing available-slots | Duration later selected | Discovery does not bypass authoritative offering-aware check | regression/live | planned |

### 4.8 Swagger, migrations, regression, and isolation

| ID | Call / identity | Setup and request | Expected / side effects | Automation | Status |
|---|---|---|---|---|---|
| `FAN-CONTRACT-SWAGGER-001` | Customer Swagger | Development enabled | All new routes/schemas/statuses present; optional device accurately documented | both | planned |
| `FAN-CONTRACT-SWAGGER-002` | Business Swagger | Development enabled | Category metadata and internal availability route/HMAC present | both | planned |
| `FAN-CONTRACT-SWAGGER-003` | Both Swagger docs | None | No route from the other implementation; no secrets/examples with PII | TestServer/live | planned |
| `FAN-CONTRACT-SERIALIZATION-004` | Contract tests | All enum values/null optionals | Exact string enums, casing, nullability, no integer enums | automated | planned |
| `FAN-CONTRACT-MIGRATION-005` | Customer migration suite | Upgrade/rollback/model snapshot | New roots/columns/indexes/filter/legacy mapping correct | migration | planned |
| `FAN-CONTRACT-MIGRATION-006` | Business migration suite | Upgrade/rollback/model snapshot | Category and work-order snapshot columns correct | migration | planned |
| `FAN-CONTRACT-STARTUP-007` | Start each host independently | Only owning DB configured | Both start/health pass; no cross-DB dependency | live | planned |
| `FAN-CONTRACT-REGRESSION-008` | Full affected tests | Existing routes | Draft/booking/payment/HMAC/catalog/availability behavior retained | automated | planned |
| `FAN-CONTRACT-SECURITY-009` | error/no-leak scan | All negative live scenarios | No tokens, signatures, SQL, PII, booking IDs in public review | both | planned |

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
  normalization and owning-DbContext-only changes.
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
