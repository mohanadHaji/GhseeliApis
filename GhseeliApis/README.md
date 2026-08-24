# Ghseeli - Vehicle Services Platform

[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0-512BD4)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/Tests-1636%20Passing-success)](.)
[![Database](https://img.shields.io/badge/Database-SQL%20Server-CC2927)](https://www.microsoft.com/sql-server)

Ghseeli is a .NET solution with two independently deployed backends:

- `GhseeliApis` is the Customer API.
- `Ghseeli.BusinessApi` is the Business API for owners, employees, and administrators.
- `Ghseeli.IntegrationContracts` contains only neutral versioned HTTP contracts.

The APIs have separate identities, databases, configuration, migrations,
domains, and deployments. Neither API may reference the other implementation
project or access the other database. See
[`API_BOUNDARIES.md`](API_BOUNDARIES.md) for the ownership, route, security,
and integration contract.

## Architecture and ownership

Both APIs follow this request flow where applicable:

```text
Controllers -> Handlers/Services -> Repositories -> EF Core -> SQL Server
```

The Customer API owns:

- Customer identity, OAuth, profiles, addresses, and vehicles.
- Devices and customer configuration.
- Synchronized catalog read models and customer browsing.
- Device-owned checkout drafts and pricing orchestration.
- Customer booking snapshots, status callbacks, and payments.
- Stripe webhook processing.

The Business API owns:

- Business owner, employee, and administrator identity.
- Companies, branches, categories, offerings, add-ons, and prices.
- Schedules, closures, capacity, service areas, and availability.
- Authoritative catalog validation and pricing publication.
- Reservations, work orders, and business-initiated booking transitions.

Cross-API operations use secured, idempotent HTTPS and DTOs from
`Ghseeli.IntegrationContracts`.

## Clean database model

Each API uses its own SQL Server database and one clean initial migration:

- Customer: `20260824132736_InitialCustomerDatabase`
- Business: `20260824132604_InitialBusinessDatabase`

Every owned table and each `__EFMigrationsHistory` table is explicitly mapped
to `dbo`. The Customer schema intentionally excludes the removed legacy
Company, Service, ServiceOption, Booking, Payment, Wallet,
WalletTransaction, Notification, and CompanyAvailability entities. The
Business schema contains no customer Identity, device, checkout, or payment
tables.

The replaced migration histories cannot upgrade an existing database. Back up
any data that must be retained and recreate each disposable or pre-production
database independently. Never run destructive database commands against
production or a shared database.

```powershell
# Customer database
dotnet ef database drop --force `
  --project .\GhseeliApis\GhseeliApis.csproj `
  --startup-project .\GhseeliApis\GhseeliApis.csproj
dotnet ef database update `
  --project .\GhseeliApis\GhseeliApis.csproj `
  --startup-project .\GhseeliApis\GhseeliApis.csproj

# Business database
dotnet ef database drop --force `
  --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj `
  --startup-project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
dotnet ef database update `
  --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj `
  --startup-project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
```

Rerunning either `database update` command must be a no-op.

## Local configuration

Prerequisites:

- .NET 8 and .NET 9 SDKs.
- SQL Server LocalDB, Developer edition, or another SQL Server instance with a
  separate database for each API.

Configure development secrets from the solution directory. Never commit real
connection strings, JWT keys, OAuth credentials, HMAC credentials, or Stripe
keys.

```powershell
dotnet user-secrets set "ConnectionStrings:CustomerConnection" "<customer-connection>" `
  --project .\GhseeliApis\GhseeliApis.csproj
dotnet user-secrets set "CustomerSchema" "dbo" `
  --project .\GhseeliApis\GhseeliApis.csproj

dotnet user-secrets set "ConnectionStrings:BusinessConnection" "<business-connection>" `
  --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
dotnet user-secrets set "BusinessSchema" "dbo" `
  --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
```

Additional JWT, internal integration, OAuth, and Stripe settings are
environment-specific and must also come from user secrets or environment
variables.

## Build, test, and run

Run commands from the solution directory containing `GhseeliApis.sln`.

```powershell
dotnet restore
dotnet build .\GhseeliApis.sln
dotnet test .\GhseeliApis.sln

dotnet run --project .\GhseeliApis\GhseeliApis.csproj
dotnet run --project .\Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
```

The current Step 16 baseline is 1,636 passing tests: 1,110 Customer tests and
526 Business tests.

Swagger is exposed at `/swagger` only in Development or by explicit
non-production configuration. It is disabled by default in Production. The
Customer and Business hosts publish independent OpenAPI documents.

## HTTP contracts

[`STEP_15_HTTP_TEST_PLAN.md`](STEP_15_HTTP_TEST_PLAN.md) is the frozen
localization, error, transport, and OpenAPI oracle.
[`STEP_16_HTTP_TEST_PLAN.md`](STEP_16_HTTP_TEST_PLAN.md) is the clean schema,
route inventory, startup, and host-isolation oracle.

Step 16 removes the legacy Customer Companies, Services, ServiceOptions,
Bookings, and Payments operations. They are absent from endpoint metadata and
Swagger and return route-level `404` without processing credentials or causing
side effects. Modern `/api/v1` catalog, checkout, booking, status, and payment
flows remain.

Feature-specific HTTP plans, manifests, fixture scripts, lifecycle runners,
and sanitized results live under `scripts\http-tests`.

### Security requirements

| Operation family | Required security |
|---|---|
| Customer profile, address, and vehicle | Customer bearer JWT |
| Customer configuration, catalog, drafts, and pricing | `X-Device-Token` |
| Customer booking confirmation and payment | Customer bearer JWT and `X-Device-Token` |
| Business owner/staff management | Business bearer JWT and role/assignment policy |
| Either host's `/api/v1/internal/*` | Complete internal HMAC headers |
| Stripe webhook | `Stripe-Signature` |

Internal HMAC uses `X-Ghseeli-Service-Id`, `X-Ghseeli-Timestamp`,
`X-Ghseeli-Nonce`, and `X-Ghseeli-Signature`. Internal mutations and
validations also require a bounded `Idempotency-Key`. Credential types cannot
substitute for each other, and Customer and Business JWTs are rejected
cross-host.

### Localization and errors

Customer-visible content supports Arabic and Hebrew. A valid `?language=ar|he`
query value takes precedence over `Accept-Language`; otherwise the supported
range with the greatest positive quality is selected. Missing, malformed, or
unsupported headers fall back safely to Arabic.

Application failures use `application/problem+json` with stable `code`,
`correlationId`, status, and localized presentation where applicable.
Responses and logs must not disclose secrets, credentials, raw signed bodies,
PII, provider internals, or internal database IDs.

### Payment ownership

Payment intent requests contain only public booking identity and method
selection. Amount, currency, totals, transaction state, and payment state are
server-owned and come from the immutable confirmed booking. Stripe intents are
created unconfirmed. Only verified Stripe webhooks can mark a booking paid or
reconcile a refund.

`Wallet`, `CashOnArrival`, and `ThirdParty` remain unavailable until their
server flows are implemented.

## Development workflow

For every meaningful behavior change:

1. Define observable behavior, failure cases, authorization, ownership, and
   HTTP applicability.
2. Create or update the feature HTTP plan before implementation when HTTP
   behavior changes.
3. Write the appropriate automated tests and prove they fail for the expected
   missing behavior.
4. Implement the minimum correct behavior.
5. Run targeted and affected regression tests.
6. Execute the declared HTTP scenarios and record exact pass, fail, and
   deferred totals with rationale.
7. Review the complete change and resolve discovered defects test-first.

Never run HTTP tests against production. Keep harness artifacts under
`scripts\http-tests\artifacts` and local secrets or overrides in ignored
`*.local.json` or `local.*.json` files.

## Technology

- ASP.NET Core 8
- Entity Framework Core 8
- ASP.NET Core Identity
- SQL Server
- xUnit, Moq, and FluentAssertions
- Stripe test/live integration through environment-owned configuration

## License

This project is licensed under the MIT License.
