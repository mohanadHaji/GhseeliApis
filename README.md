# Ghseeli APIs

Ghseeli is currently a **car-wash and vehicle-services platform** implemented
as two independently deployed ASP.NET Core APIs with separate SQL Server
databases.

This is the repository's only README and the first handoff document for a new
development session. The current HTTP product remains car-wash focused. The
database now contains an internal, additive business-vertical foundation so
future verticals such as dry cleaning or vehicle mechanics can be introduced
without redesigning company, branch, catalog, scheduling, pricing, or shared
work-order ownership.

## Repository layout

The solution is under `GhseeliApis\GhseeliApis.sln`.

| Project | Responsibility |
|---|---|
| `GhseeliApis` | Customer identity, devices, vehicles, addresses, synchronized catalog, checkout drafts, pricing, customer bookings, payments, and Stripe webhooks |
| `Ghseeli.BusinessApi` | Business identity, companies, branches, catalog, availability, reservations, work orders, and staff transitions |
| `Ghseeli.IntegrationContracts` | Neutral versioned HTTP DTOs and enums shared across the APIs |
| `Ghseeli.Common` | Shared infrastructure that is safe for both applications, including application logging |
| `GhseeliApis.Tests` | Customer API unit, relational, contract, security, and HTTP tests |
| `Ghseeli.BusinessApi.Tests` | Business API unit, relational, contract, security, and HTTP tests |

The APIs must not reference each other's implementation project or access each
other's database. Cross-API operations use secured, idempotent HTTPS and
contracts from `Ghseeli.IntegrationContracts`.

## Current product boundary

All current routes, DTOs, Swagger descriptions, validation, and workflows are
car-wash focused. In particular:

- Customer checkout and booking require vehicle data.
- Business reservations create vehicle work-order details.
- Customer catalog browsing exposes only the existing car-wash catalog.
- New business-owner registration is assigned internally to the `car_wash`
  vertical.
- Other verticals are not enabled for registration or exposed through any
  current API.
- Card payment must remain disabled when Stripe is not configured. Wallet,
  cash-on-arrival, and third-party payment remain unavailable.

The vertical-ready schema does **not** mean mechanics or dry cleaners are
implemented. Supporting one later will require additive vertical-specific
tables, validation, contracts, APIs, and tests.

## Architecture

Each API follows this flow where applicable:

```text
Controllers -> Handlers/Services -> Repositories -> EF Core -> owned SQL database
```

The Business API is authoritative for catalog, price, duration, availability,
capacity, reservations, and work-order status. The Customer API owns customer
identity and immutable customer-facing booking/payment snapshots.

## Business database schema

The Business database uses the `dbo` schema and contains these logical areas.

### Identity and ownership

- ASP.NET Core Identity tables own business users and roles.
- `Companies` owns a business account and catalog version.
- `Branches` belongs to one company.
- `BusinessUserAssignments` assigns an owner or employee to a company and
  optionally a branch.

### Business vertical foundation

- `BusinessVerticals` is a controlled lookup. It has a stable unique `Code`,
  localized names, active state, registration state, audit timestamp, and row
  version.
- `CompanyBusinessVerticals` is the normalized company-to-vertical relation.
  Its composite key prevents duplicate assignments.
- A filtered unique index permits at most one active primary vertical per
  company.
- A check constraint prevents a primary assignment from being inactive.
- New companies receive one active primary `car_wash` assignment
  automatically; a supplied new-company assignment set without exactly one
  active primary assignment is rejected.
- `ServiceCategories` references the company's assigned vertical through a
  composite foreign key.
- `AppointmentReservations` and `WorkOrders` store immutable vertical ID/code
  snapshots. Save-time guards reject changes after insertion and require a
  newly created reservation and work order to carry matching `car_wash`
  snapshots.
- Current publication, catalog-management reads, and reservation validation
  accept only active `car_wash` assignments and categories. Disabled or future
  vertical data cannot leak through the existing car-wash APIs.

The only seeded and registration-enabled vertical is:

| ID | Code | Meaning |
|---|---|---|
| `a842f536-17b7-4be6-a18d-1bdc6245094c` | `car_wash` | Current Ghseeli car-wash product |

Existing companies and records are backfilled to `car_wash`.

### Catalog and availability

- `ServiceCategories` belongs to a company and one assigned business vertical.
- `ServiceOfferings` belongs to a category and optionally a branch.
- `AddonGroups` and `AddonChoices` define localized selection rules, price
  adjustments, and duration adjustments.
- `BranchAvailabilitySettings`, `BranchRecurringSchedules`,
  `BranchAvailabilityOverrides`, and `BranchServiceAreas` define appointment
  schedules, closures, capacity, lead time, horizon, and service reach.

### Reservations and work orders

- `AppointmentReservations` owns the authoritative slot reservation and
  cross-system booking reference.
- `WorkOrders` stores the shared order header: status, customer snapshot,
  service location, vertical snapshot, and audit data.
- `VehicleWorkOrderDetails` is a required one-to-one car-wash extension of a
  work order. It stores vehicle type, plate, make, model, and color.
- `WorkOrderItems` and `WorkOrderSelections` store immutable itemized price,
  duration, and add-on snapshots.
- `BookingStatusOutboxMessages` and `BookingStatusRequeueHistory` provide
  idempotent status delivery and controlled recovery.

Moving vehicle fields into `VehicleWorkOrderDetails` keeps existing behavior
while avoiding vehicle-only columns in the shared work-order header.

### Internal integration

- `InternalServiceNonces` prevents signed-request replay.
- `InternalServiceIdempotencyRecords` preserves safe internal retries.

## Customer database schema

The Customer database also uses `dbo`.

### Identity and customer data

- ASP.NET Core Identity tables own customer users and roles.
- `Vehicles` and `UserAddresses` belong only to the Customer API.
- `CustomerDevices` stores hashed device credentials, activity, and rotation
  state.
- `CustomerConfigurations` stores localized customer-facing configuration.

### Catalog read model

- `CatalogProviders`, `CatalogBranches`, `CatalogCategories`,
  `CatalogOfferings`, `CatalogAddonGroups`, and `CatalogAddonChoices` form the
  synchronized Business catalog snapshot.
- `CatalogProviders.BusinessVerticalCode` is internal and defaults to
  `car_wash`. Customer catalog queries explicitly exclude every other vertical,
  and the marker is intentionally not exposed by current DTOs.

### Checkout, booking, and payment

- Checkout draft tables store device-owned selections, pricing snapshots,
  expiry, and concurrency state.
- `BookingConfirmationAttempts` makes cross-database confirmation retryable.
- `CustomerBookings`, items, and selections store immutable confirmed
  customer-facing snapshots.
- `CustomerBookings.BusinessVerticalCode` defaults to `car_wash` and remains
  internal. Booking confirmation copies the provider marker, and save-time
  guards prevent later snapshot mutation.
- Customer bookings retain vehicle snapshots because the current Customer API
  remains explicitly vehicle-focused.
- Payment and webhook tables own server-authoritative amount, currency,
  idempotency, and verified Stripe lifecycle state.
- Processed status messages prevent duplicate or out-of-order callbacks.

## Relationship summary

```text
BusinessVertical
  1 -> many CompanyBusinessVertical
Company
  1 -> many CompanyBusinessVertical
  1 -> many Branch
  1 -> many ServiceCategory
CompanyBusinessVertical
  1 -> many ServiceCategory (CompanyId + BusinessVerticalId)
ServiceCategory
  1 -> many ServiceOffering
ServiceOffering
  1 -> many AddonGroup
AddonGroup
  1 -> many AddonChoice
AppointmentReservation
  1 -> 1 WorkOrder
WorkOrder
  1 -> 1 VehicleWorkOrderDetails
  1 -> many WorkOrderItem
WorkOrderItem
  1 -> many WorkOrderSelection
```

## Migrations

Current additive migrations:

- Business:
  - `20260824132604_InitialBusinessDatabase`
  - `20260825205916_AddBusinessVerticalReadiness`
- Customer:
  - `20260824132736_InitialCustomerDatabase`
  - `20260824230024_AddCustomerDeviceActiveState`
  - `20260825205815_AddCustomerBusinessVerticalSnapshots`

The vertical migration:

1. Seeds `car_wash`.
2. Assigns every existing company to it as active and primary.
3. Backfills category, reservation, work-order, and Customer snapshot markers.
4. Copies every existing Business work-order vehicle snapshot into
   `VehicleWorkOrderDetails` before removing the old columns.
5. Preserves the data in the reverse direction if the migration is rolled
   back.

Automated populated-upgrade tests apply both additive migrations over existing
Business and Customer rows. They verify `car_wash` backfill, preserved
relationships and values, relational vehicle-detail persistence, and repeated
migration idempotency.

Future migrations must remain additive and must be created only in the
DbContext that owns the model.

## Future vertical expansion

When a new vertical is approved:

1. Add a disabled `BusinessVerticals` row with a stable code and localized
   names.
2. Design that vertical's domain and state machine rather than placing
   arbitrary fields in JSON or an EAV table.
3. Add focused extension tables, such as a garment-order detail table or
   mechanic inspection detail table.
4. Add versioned integration contracts and explicit API behavior.
5. Add registration/admin controls; only then set `RegistrationEnabled`.
6. Extend Customer synchronization and filtering deliberately.
7. Add migration, authorization, ownership, lifecycle, concurrency, and full
   HTTP journey tests before exposing it.

Do not add speculative nullable columns to `WorkOrders`, do not enable a
vertical before its workflow exists, and do not let a disabled vertical leak
into current car-wash browsing.

## Build, test, and run

Run commands from `GhseeliApis\`, the directory containing
`GhseeliApis.sln`.

```powershell
dotnet build
dotnet test
dotnet run --project .\GhseeliApis\GhseeliApis.csproj
dotnet run --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
```

Apply migrations independently:

```powershell
dotnet ef database update `
  --project .\GhseeliApis\GhseeliApis.csproj `
  --startup-project .\GhseeliApis\GhseeliApis.csproj

dotnet ef database update `
  --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj `
  --startup-project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
```

Development connection strings, JWT keys, OAuth credentials, internal HMAC
secrets, and Stripe values must come from user secrets or environment
variables. Never place real secrets in committed settings or test artifacts.

## Testing workflow

Meaningful changes are test-first:

1. Define observable behavior, failure cases, authorization, ownership, and
   HTTP applicability.
2. Write focused tests and confirm the expected failure.
3. Implement the minimum correct behavior.
4. Run targeted and affected regression suites.
5. Run live local HTTP scenarios when the network/middleware contract changes.
6. Record exact pass/fail/deferred totals and keep this README synchronized
   with schema changes.

The manifest-driven PowerShell HTTP harness is under
`GhseeliApis\scripts\http-tests`. Its entry point is
`Invoke-HttpTests.ps1`; committed plans are under `plans`, while generated
results and `*.local.json` overrides remain ignored under `artifacts`.

The real Stripe test-network scenario is intentionally separate. Until valid
test-mode Stripe configuration is available, deployment must keep card
payment disabled and must not claim the real Stripe release gate passed.

## Handoff for a new session

Before changing code:

1. Read this README and `GhseeliApis\API_BOUNDARIES.md`.
2. Read `.github\copilot-instructions.md`.
3. Inspect both current EF model snapshots and the latest migrations.
4. Preserve independent API/database ownership.
5. Keep all current HTTP contracts car-wash focused unless the user explicitly
   starts a vertical implementation.
6. Update this README whenever schema or future-expansion assumptions change.

The next planned activity after this schema-readiness change is the
comprehensive non-Stripe pre-deployment acceptance gate: inventory every
endpoint and field, exercise independent and cross-system lifecycles with
multiple actors, verify state transitions and failure recovery, and reconcile
every route to passing evidence. The user will request that full gate
separately.
