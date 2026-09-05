# Ghseeli APIs

Ghseeli is a car-wash and vehicle-services backend built as two independently
deployed ASP.NET Core APIs. The Customer API serves mobile/customer journeys;
the Business API serves business owners, staff, catalog management,
availability, reservations, and work orders.

> Frontend and AI implementation guide:
> [`GhseeliApis/docs/FRONTEND_AI_INTEGRATION_GUIDE.md`](GhseeliApis/docs/FRONTEND_AI_INTEGRATION_GUIDE.md)

## Solution overview

| Project | Target | Responsibility |
|---|---:|---|
| `Ghseeli.CustomerApi` | .NET 8 | Customer identity, devices, profiles, vehicles, addresses, catalog browsing, checkout, bookings, and Lahza payments |
| `Ghseeli.BusinessApi` | .NET 8 | Business identity, companies, branches, catalog, availability, reservations, work orders, and status transitions |
| `Ghseeli.IntegrationContracts` | .NET 8 | Versioned HTTP DTOs and enums shared between the two APIs |
| `Ghseeli.Common` | .NET 8 | Shared infrastructure such as application logging |
| `Ghseeli.CustomerApi.Tests` | .NET 9 | Customer unit, relational, contract, security, and HTTP tests |
| `Ghseeli.BusinessApi.Tests` | .NET 9 | Business unit, relational, contract, security, and HTTP tests |

The APIs have separate identities, JWTs, databases, migrations, configuration,
and deployments. Neither API may reference the other API implementation or
read the other API database. Cross-API communication uses versioned contracts
and authenticated HTTPS requests.

## Repository layout

| Path | Contents |
|---|---|
| `GhseeliApis/GhseeliApis.sln` | Main solution |
| `GhseeliApis/Ghseeli.CustomerApi` | Customer API |
| `GhseeliApis/Ghseeli.BusinessApi` | Business API |
| `GhseeliApis/Ghseeli.IntegrationContracts` | Neutral integration contracts |
| `GhseeliApis/Ghseeli.Common` | Shared infrastructure |
| `GhseeliApis/*Tests` | Automated tests |
| `GhseeliApis/docs` | Product and frontend integration documentation |
| `GhseeliApis/scripts/http-tests` | Manifest-driven local HTTP test harness |
| `GhseeliApis/API_BOUNDARIES.md` | Authoritative ownership, security, and cross-API rules |
| `GhseeliApis/HTTP_TEST_PLAN_STANDARD.md` | Required HTTP testing process |
| `.github/workflows/deploy-monsterasp.yml` | Manual production deployment workflow |

## Architecture

```text
Customer application
        |
        v
Ghseeli.CustomerApi  ---- signed HTTPS/HMAC ---->  Ghseeli.BusinessApi
        |                                             |
        v                                             v
Customer SQL database                         Business SQL database
```

Each API generally follows:

```text
Controllers -> Handlers/Services -> Repositories -> EF Core -> owned database
```

| Authority | Owning API |
|---|---|
| Customer identity, devices, saved vehicles and addresses | Customer |
| Customer-facing catalog cache | Customer |
| Catalog definitions, prices, duration, schedules and capacity | Business |
| Checkout draft and customer booking snapshot | Customer |
| Appointment reservation and operational work order | Business |
| Operational booking status | Business |
| Customer payment and Lahza webhook state | Customer |

## Main application flows

| Flow | Summary |
|---|---|
| Customer onboarding | Register device, register/login customer, optionally manage profile, vehicles, and addresses |
| Discovery | Read localized configuration, categories, businesses, offerings, add-ons, and available slots |
| Checkout | Create a device-owned draft, update it with optimistic versioning, then request authoritative repricing |
| Booking | Authenticate the customer and confirm a priced draft; Customer reserves the appointment through Business |
| Operations | Business staff transition the work order; signed callbacks update the Customer booking |
| Payment | Customer initializes hosted Lahza checkout, leaves the app, returns, and the backend verifies provider state |
| Business setup | Owner registers, manages company/branches, catalog, add-ons, schedules, closures, service area, and capacity |

For detailed UI scenarios, headers, state handling, retry rules, and AI
instructions, use the
[frontend integration guide](GhseeliApis/docs/FRONTEND_AI_INTEGRATION_GUIDE.md).

## Requirements

| Tool | Version/purpose |
|---|---|
| .NET SDK | .NET 8 for APIs and .NET 9 for test projects |
| SQL Server | Runtime persistence |
| EF Core CLI | Migration commands |
| PowerShell | Deployment and HTTP harness scripts |
| GitHub CLI | Optional, for manually dispatching and monitoring deployment |

Install the EF CLI if it is not already available:

```powershell
dotnet tool install --global dotnet-ef --version 8.*
```

## Build and test

Run commands from `GhseeliApis`, the directory containing `GhseeliApis.sln`.

| Task | Command |
|---|---|
| Restore | `dotnet restore` |
| Build | `dotnet build` |
| Run every test | `dotnet test` |
| Run Customer tests | `dotnet test .\Ghseeli.CustomerApi.Tests\Ghseeli.CustomerApi.Tests.csproj` |
| Run Business tests | `dotnet test .\Ghseeli.BusinessApi.Tests\Ghseeli.BusinessApi.Tests.csproj` |
| Run one test class | `dotnet test --filter "FullyQualifiedName~VehicleValidationTests"` |
| Run one test | `dotnet test --filter "FullyQualifiedName~VehiclesControllerTests.GetMyVehicles_ReturnsOk"` |

The latest documented baseline is **1,848 passing tests**:

| Test project | Passed |
|---|---:|
| Customer | 1,264 |
| Business | 584 |
| Total | 1,848 |

## Local configuration

Never place real credentials in `appsettings.json`. Configure development
values with user secrets or environment variables.

### Customer API

```powershell
dotnet user-secrets set "ConnectionStrings:CustomerConnection" "<sql-server-connection>" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet user-secrets set "JwtSettings:SecretKey" "<minimum-32-character-secret>" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet user-secrets set "BusinessApiClient:BaseUrl" "https://localhost:7167" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet user-secrets set "BusinessApiClient:ServiceId" "customer-api" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet user-secrets set "BusinessApiClient:ActiveSecret" "<customer-to-business-hmac-secret>" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet user-secrets set "CustomerInternalServiceAuthentication:Services:0:ServiceId" "business-api" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet user-secrets set "CustomerInternalServiceAuthentication:Services:0:ActiveSecret" "<business-to-customer-hmac-secret>" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet user-secrets set "CustomerInternalServiceAuthentication:Services:0:AllowedOperations:0" "booking_status_callback" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
```

Optional Customer integrations:

```powershell
dotnet user-secrets set "Authentication:Google:ClientId" "<client-id>" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet user-secrets set "Authentication:Google:ClientSecret" "<client-secret>" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet user-secrets set "Authentication:Facebook:AppId" "<app-id>" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet user-secrets set "Authentication:Facebook:AppSecret" "<app-secret>" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet user-secrets set "Lahza:SecretKey" "<test-secret>" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet user-secrets set "Lahza:CallbackUrl" "https://localhost:3000/payment/callback" --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
```

`Lahza:CallbackUrl` is a frontend return URL or universal link. It is not a
Customer API endpoint and does not prove payment success.

OAuth provider consoles must register callback URLs on the Customer API:

| Provider | Local callback |
|---|---|
| Google | `https://localhost:62878/api/auth/google-callback` |
| Facebook | `https://localhost:62878/api/auth/facebook-callback` |

Use the deployed Customer API origin for non-local environments. The current
external-login and account-linking redirect flows are **not approved for
frontend use** because return URLs are not yet allowlisted; external login can
also return a bearer token through a redirect query string. Use email/password
authentication until those flows are hardened.

### Business API

```powershell
dotnet user-secrets set "ConnectionStrings:BusinessConnection" "<sql-server-connection>" --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
dotnet user-secrets set "BusinessJwtSettings:SecretKey" "<minimum-32-character-secret>" --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
dotnet user-secrets set "InternalServiceAuthentication:Services:0:ServiceId" "customer-api" --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
dotnet user-secrets set "InternalServiceAuthentication:Services:0:ActiveSecret" "<customer-to-business-hmac-secret>" --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
dotnet user-secrets set "InternalServiceAuthentication:Services:0:AllowedOperations:0" "catalog_snapshot" --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
dotnet user-secrets set "InternalServiceAuthentication:Services:0:AllowedOperations:1" "appointment_validate" --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
dotnet user-secrets set "InternalServiceAuthentication:Services:0:AllowedOperations:2" "reservation_create" --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
dotnet user-secrets set "InternalServiceAuthentication:Services:0:AllowedOperations:3" "reservation_status_read" --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
dotnet user-secrets set "InternalServiceAuthentication:Services:0:AllowedOperations:4" "appointment_available_slots" --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
dotnet user-secrets set "CustomerBookingStatusClient:BaseUrl" "https://localhost:62878" --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
dotnet user-secrets set "CustomerBookingStatusClient:ServiceId" "business-api" --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
dotnet user-secrets set "CustomerBookingStatusClient:ActiveSecret" "<business-to-customer-hmac-secret>" --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
```

The two HMAC directions must use different secrets.

## Database migrations

Each API owns its migrations. Run from the solution directory:

```powershell
dotnet ef database update `
  --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj `
  --startup-project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj

dotnet ef database update `
  --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj `
  --startup-project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
```

Create migrations only in the owning API:

```powershell
dotnet ef migrations add <MigrationName> `
  --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj `
  --startup-project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj `
  --output-dir Migrations
```

Replace the project paths with `Ghseeli.BusinessApi` for Business migrations.

## Run locally

Start the APIs in separate terminals:

```powershell
dotnet run --project .\Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet run --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj --launch-profile https
```

Default HTTPS launch URLs:

| API | URL | Swagger |
|---|---|---|
| Customer | `https://localhost:62878` | `https://localhost:62878/swagger` |
| Business | `https://localhost:7167` | `https://localhost:7167/swagger` |

Startup requires a reachable owned database. Cross-API calls require HTTPS
unless the explicit development-only insecure HTTP override is configured.

Neither API currently configures browser CORS. Native mobile clients may call
the Customer API directly. Browser frontends must use a same-origin reverse
proxy/backend-for-frontend, or the backend must first add a tightly allowlisted
CORS policy for approved origins.

## Swagger and frontend schemas

Swagger/OpenAPI is the canonical source for:

- endpoint paths and verbs;
- request and response schemas;
- required and nullable fields;
- enum values;
- validation bounds;
- security requirements;
- documented status codes and Problem Details.

Do not manually recreate TypeScript interfaces from this README. Generate a
frontend client from each API's `/swagger/v1/swagger.json` and regenerate it
when the backend contract changes.

## Authentication summary

| Credential | Header | Used by |
|---|---|---|
| Device token | `X-Device-Token` | Customer configuration, catalog, checkout, pricing, booking, and payment routes |
| Customer JWT | `Authorization: Bearer <token>` | Customer account operations plus booking/payment |
| Business JWT | `Authorization: Bearer <token>` | Business-owner/staff operations |
| Internal HMAC | `X-Ghseeli-*` headers | API-to-API routes only |
| Idempotency key | `Idempotency-Key` | Mutating operations that can be safely replayed |
| Lahza signature | `X-Lahza-Signature` | Lahza webhook only |

Customer JWTs are not valid in the Business API, and Business JWTs are not
valid in the Customer API.

## HTTP testing

The reusable test harness is under `GhseeliApis\scripts\http-tests`.

```powershell
.\scripts\http-tests\Run-SelfTests.ps1
.\scripts\http-tests\Test-Step17LiveAssets.ps1
.\scripts\http-tests\Test-Step18LiveAssets.ps1
.\scripts\http-tests\Test-Step20LiveAssets.ps1
```

Committed manifests live under `scripts\http-tests\plans`. Generated results,
tokens, local variables, and temporary databases must remain uncommitted.
Follow [`HTTP_TEST_PLAN_STANDARD.md`](GhseeliApis/HTTP_TEST_PLAN_STANDARD.md)
for every HTTP-visible change.

## Deployment

Production deployment is manual through
`.github/workflows/deploy-monsterasp.yml`.

```powershell
gh workflow run deploy-monsterasp.yml --ref master
gh run list --workflow deploy-monsterasp.yml --limit 1
```

The workflow tests and builds the complete solution, migrates each database,
publishes each API, injects runtime configuration, deploys both sites, and
checks their database-backed health endpoints.

| API | Current host |
|---|---|
| Customer | `http://ghseelicustomer.runasp.net` |
| Business | `http://ghseelibusiness.runasp.net` |

MonsterASP TLS is currently deferred. Cross-API production URLs intentionally
remain HTTPS and must not be downgraded. Lahza initialization, verification,
and webhook routes are hidden in Production until payment release approval.

## Documentation

| Document | Audience and purpose |
|---|---|
| [`FRONTEND_AI_INTEGRATION_GUIDE.md`](GhseeliApis/docs/FRONTEND_AI_INTEGRATION_GUIDE.md) | Primary prompt/context document for frontend implementation |
| [`API_BOUNDARIES.md`](GhseeliApis/API_BOUNDARIES.md) | Backend ownership, security, route map, and integration invariants |
| [`HTTP_TEST_PLAN_STANDARD.md`](GhseeliApis/HTTP_TEST_PLAN_STANDARD.md) | Required test-first HTTP process |
| `STEP_*_HTTP_TEST_PLAN.md` and `*_RESULTS.md` | Retained implementation history and sanitized test evidence |

## Non-negotiable rules

1. Never share database access between the APIs.
2. Never reference one API implementation project from the other.
3. Never trust client-supplied prices, totals, status, ownership, or capacity.
4. Never commit connection strings, JWT keys, OAuth secrets, HMAC secrets, or
   Lahza credentials.
5. Preserve stable error codes and use Swagger as the wire-contract source.
6. Add tests before changing observable behavior.
7. Do not enable production Lahza routes until the external payment and HTTPS
   release gates are approved.
