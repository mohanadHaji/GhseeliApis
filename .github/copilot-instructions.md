# Copilot Instructions — GhseeliApis

## Agent Handoff and Production Readiness

Before answering questions such as "what is left?", "is this production
ready?", or "what should we build next?", read:

1. `README.md` at the repository root for the service and database design.
2. `GhseeliApis/README.md` for completed work, known gaps, testing evidence,
   and the production-readiness ledger.
3. `GhseeliApis/API_BOUNDARIES.md` for ownership and security invariants.

Verify the handoff against current code and deployment state when making new
changes. Keep its production-readiness statuses current. Do not present
conditional features such as SMS, OAuth, browser CORS, FCM, or alternative
payment methods as unconditional blockers unless the user says the product
requires them.

## Application Boundaries

The solution contains independently deployed backends:

- `Ghseeli.CustomerApi` — Customer API and customer database.
- `Ghseeli.BusinessApi` — Business-owner API and business database.
- `Ghseeli.IntegrationContracts` — Neutral versioned HTTP DTOs/enums only; no domain logic, EF entities, authentication, or application dependencies.

Never reference one API implementation project from the other or access the other API's database. See `GhseeliApis/API_BOUNDARIES.md` for the route, ownership, security, and integration contract.

## Build, Test, Run, and Publish

All commands run from the `GhseeliApis/` solution directory (the one containing `GhseeliApis.sln`).

```shell
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test by name
dotnet test --filter "FullyQualifiedName~VehiclesControllerTests.GetMyVehicles_ReturnsOk"

# Run tests in one class
dotnet test --filter "FullyQualifiedName~VehicleValidationTests"

# Run the Customer API locally
dotnet run --project Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj

# Run the Business API locally
dotnet run --project Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj

# Run only Business API tests
dotnet test Ghseeli.BusinessApi.Tests\Ghseeli.BusinessApi.Tests.csproj

# Customer API migrations (run from the solution directory)
dotnet ef migrations add MigrationName --project Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj --startup-project Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj
dotnet ef database update --project Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj --startup-project Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj

# Business API migrations (run from the solution directory)
dotnet ef migrations add MigrationName --project Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj --startup-project Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj --output-dir Persistence\Migrations
dotnet ef database update --project Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj --startup-project Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj
```

There is no separate lint command; use `dotnet build` for compiler and static validation.

Secrets (JWT keys, connection strings, OAuth, SMTP, HMAC, Lahza) are loaded
from **user secrets** in development. Use `dotnet user-secrets` to configure
them and never put real secrets in `appsettings.json`. Production deployment is
manual through `.github/workflows/deploy-monsterasp.yml`; it runs the full
tests and Release build, applies each API's migrations, publishes `win-x86`
self-contained artifacts, injects runtime configuration, deploys both APIs,
and verifies database health. Hosted Demo seeding is a separate guarded manual
workflow in `.github/workflows/seed-hosted-demo.yml`.

Both API projects and the integration-contract project target .NET 8. Test projects target .NET 9. EF design-time commands require the owning API's configured connection string.

## Test-First Development

For every meaningful behavior change:

1. Define observable behavior, important failure/edge cases, and whether HTTP coverage applies.
2. If HTTP applies, create or update the feature-specific HTTP plan **before implementing the changed behavior**. Use `HTTP_TEST_PLAN_STANDARD.md` and keep stable scenario IDs aligned between docs and tests. If the selected execution level includes live local/dev/test HTTP, also add or update `scripts\http-tests\plans\<feature>.manifest.json`. If HTTP does not apply, record `HTTP required: No - <reason>`.
3. Write the appropriate unit, contract, integration, or feature tests first.
4. Run them and verify they fail for the expected missing behavior.
5. Implement the minimum correct behavior.
6. Run targeted and affected regression tests until green.
7. Add tests for requirements discovered during implementation before adding that behavior.
8. Cover every changed endpoint plus affected existing endpoints, including success, validation, auth/ownership, boundary, failure, version/state, and regression scenarios.
9. After automated tests and required migrations pass, execute the planned HTTP scenarios at the declared execution level: rerun TestServer coverage for HTTP-visible behavior, and run the local manifest with `scripts\http-tests\Invoke-HttpTests.ps1` when live local/dev/test HTTP is part of the risk or contract. Fix failures test-first, rerun, and record exact automated test totals plus HTTP scenario pass/fail/deferred counts with rationale in the completion note or `*_HTTP_TEST_RESULTS.md`.

Do not weaken valid tests to fit an implementation or mark work complete with relevant failing tests. Do not create meaningless tests for passive DTOs or configuration-only files with no behavior.
Documentation-only or non-HTTP internal refactors may mark HTTP not applicable with rationale. A meaningful change is not done while any required HTTP scenario is still only planned/automated, failed, or deferred without explicit non-shipping rationale and compensating coverage. Keep raw harness artifacts under `scripts\http-tests\artifacts\` and secrets/local overrides in `*.local.json` or `local.*.json`; do not commit them. Missing relevant HTTP coverage, a stale plan, or any unexplained failure blocks completion. Never point local/destructive manifests at Production. Production checks require explicit user approval, safe smoke scenarios, and no fabricated customer or business records. Never expose or commit secrets, tokens, or test credentials.

## Architecture

Each ASP.NET Core 8 API follows the four-layer architecture where applicable:

```
Controllers → Handlers → Repositories → EF Core (SQL Server)
                ↕
 Services (Auth, Catalog, Checkout, Booking, Lahza, Internal HTTP)
```

- **Controllers** — Thin REST endpoints. Validate input, extract trusted user
  and device identity, and delegate to handlers/services. New public routes use
  Customer `/api/v1` or Business `/api/v1/business`; retained legacy customer
  identity/profile routes use `/api/[controller]`; internal routes use
  `/api/v1/internal`.
- **Handlers** — Business logic layer where the feature uses the handler
  pattern. Orchestrate repository calls, enforce domain rules, and return DTOs.
- **Services** — Authentication, device security, catalog synchronization,
  checkout/pricing, bookings, availability, internal HMAC clients, outbox
  delivery, and Lahza payments.
- **Repositories** — Data access via EF Core. One interface + implementation per entity. Each repo calls `SaveChangesAsync` internally.

Most request and persistence dependencies are **Scoped**. `IAppLogger` and
other explicitly stateless infrastructure may be **Singleton**; hosted workers
create scopes before resolving DbContexts or partition-aware services.

At startup, each `Program.cs` connects to its owned database and ensures its
identity roles exist. A reachable owned database is required for either API to
finish starting. Swagger is available in Development and when explicitly
enabled through `Swagger:Enabled`; the current hosted APIs enable it.

## Domain Model

Customer roots include **User**, **CustomerDevice**, **Vehicle**,
**UserAddress**, **CatalogProviderReadModel**, **CheckoutDraft**,
**CustomerBooking**, and **CustomerPayment**.

Business roots include **BusinessUser**, **Company**, **Branch**,
**BusinessVertical**, **ServiceCategory**, **ServiceOffering**,
**AppointmentReservation**, and **WorkOrder**.

- All entities use **Guid** primary keys.
- Customer `User` and Business `BusinessUser` independently extend
  `IdentityUser<Guid>`.
- Monetary fields use `decimal(18,2)`.
- Booking has a state machine: `Pending → Confirmed → InProgress → Completed/Cancelled/NoShow`.
- Wallet, wallet transactions, and notifications are not active runtime
  domains and must not be inferred from deleted legacy models.

## Key Conventions

### Validation

Business API request DTOs use **FluentValidation** with automatic ASP.NET Core model validation. Keep Business API DTOs free of `System.ComponentModel.DataAnnotations` validation attributes, register validators from the Business API assembly, require Arabic business/catalog names and Arabic branch addresses, and keep Hebrew counterparts optional (normalize blank optional Hebrew values to `null`).

Keep simple request-shape rules (required fields, max lengths, ranges, IDs) in FluentValidation validators, and keep cross-field/domain invariants in focused business-rule validators or services (for example catalog selection/default rules).

Customer API features that have not been migrated should continue following the nearest existing validation pattern. Domain models implement the project-specific `IValidatable` interface with a `Validate()` method returning `Interfaces.ValidationResult` (`IsValid` plus `Errors`). Follow the nearest feature's placement: some controllers validate constructed models, while handlers validate business-layer models.

### DTOs

DTOs live in `DTOs/` organized by feature subfolder. Follow this naming:

- `CreateXRequest` — POST body
- `UpdateXRequest` — PUT body
- `XResponse` — single-item response
- `XListResponse` — list-item response (simplified)

### Authorization

Customer roles defined in `Constants/AppRoles.cs` include **User**,
**Company**, and **Admin**. Customer authorization policies combine them:

- `UserPolicy`, `CompanyPolicy`, `AdminPolicy` — single role
- `UserOrCompanyPolicy`, `CompanyOrAdminPolicy` — combined

Self-registration is forced to the "User" role to prevent privilege escalation. Users cannot modify their own role or active status via the `PUT /api/users/me` endpoint.

Business identity is separate and uses **Owner**, **Employee**, and **Admin**
roles plus company/branch assignment checks. Customer JWTs are never valid in
Business API, and Business JWTs are never valid in Customer API.

Customer `/api/v1` endpoints require `X-Device-Token` by default unless
explicitly exempted. Booking and payment operations require both the device
token and Customer bearer token. Internal routes use HMAC, and the Lahza
webhook uses only the exact-body Lahza signature.

The hosted databases also contain trusted `Production` and `Demo` partitions.
Never accept `isDemo`, `dataPartition`, or an equivalent public client input.
Partition selection must come only from validated devices/accounts, JWT
claims, or HMAC-signed internal requests. New partition roots must preserve the
global query-filter and automatic `IsDemo` assignment invariants. Background
workers must process Production and Demo through explicit partition scopes;
they must not depend on an ambient request partition or bypass the filters.
Demo workflows must suppress SMTP, Lahza, push, and other external side effects
unless a feature has an explicit safe Demo implementation.

### Logging

Use `Ghseeli.Common.Logging.IAppLogger` for application logging (not direct `Console.WriteLine` and not app-local logger copies). Register `Ghseeli.Common.Logging.ConsoleLogger` as a singleton and inject `IAppLogger` through constructors where logging is needed.

- Log meaningful mutations, workflow outcomes, and caught error paths.
- Do **not** log secrets, JWTs, passwords, raw tokens, full request bodies, emails, phone numbers, localized descriptions, or other PII.
- Prefer IDs, operation names, and safe state transitions in messages.
- Avoid noisy logging on routine read-only success paths; reserve warnings for validation, access, not-found, or other actionable rejection outcomes.

### Testing

Tests use **xUnit** + **Moq** + **FluentAssertions**. The test project mirrors the main project structure:

- `Controllers/` — Controller tests mock handlers, set up `ClaimsPrincipal` via `SetupAuthenticatedUser()` helper
- `Handlers/` — Handler tests mock repositories
- `Models/` — Validation tests call `model.Validate()` directly
- `Services/` — authentication, catalog, checkout, booking, internal HTTP,
  outbox, and Lahza service tests

Tests follow Arrange-Act-Assert with `/// <summary>` XML doc comments on test classes.

### Database

EF Core uses SQL Server with one required connection string per API:

1. Customer: `ConnectionStrings:CustomerConnection`
2. Business: `ConnectionStrings:BusinessConnection`

Development values come from user secrets or environment variables. Hosted
values come from protected GitHub `Production` environment secrets.

The Customer `SqlServerSetupExtension` configures 5 retries, 30s maximum
delay, and a 60s command timeout. Preserve each API's existing SQL Server
configuration rather than assuming both DbContexts share one setup helper.

Runtime persistence uses SQL Server. Both APIs have independent, clean SQL Server initial migrations. Do not restore the deleted MySQL/Pomelo migration history. Add future migrations only to the DbContext that owns the changed model.

### Lahza Integration

Customer payments flow through `IPaymentGateway` implemented by
`LahzaPaymentGateway`. `LahzaWebhookController` handles exact-body signed
events at `POST /api/lahza/webhook`. Initialization and verification remain
server-authoritative for booking ownership, amount, currency, reference, and
provider state.

Only `Card` uses Lahza. `Wallet`, `CashOnArrival`, and `ThirdParty` remain
disabled with stable reason codes until fully implemented. The hosted Lahza
configuration is a test-mode proof of concept, not approval for real-money
launch. Demo bookings must always be rejected before any Lahza request.

### OTP, FCM, and Deferred Features

- Email OTP is implemented and deployed; Demo OTP uses fixed code `111111`
  without SMTP.
- SMS OTP is not implemented.
- FCM token registration/rotation is implemented, but push delivery is not.
- OAuth routes exist but are not approved for frontend use until return URLs
  are allowlisted and redirect token delivery is redesigned.
- Neither API enables browser CORS; browser clients need an approved
  allowlisted policy or same-origin BFF.

## Feature Wiring

New domains normally span the owning API's model and DbContext configuration,
feature DTOs, repository interface/implementation, handler or service,
controller, explicit registration in `Program.cs`, and matching tests.
Repositories persist writes; handlers/services own workflows and DTO mapping;
controllers remain focused on HTTP, authentication, and authorization.

Configure relationships, indexes, delete behavior, string lengths, decimal
precision, and Demo partition filters explicitly in the owning DbContext, then
create a migration only in that API.

### Test Authentication Setup Pattern

Controller tests simulate an authenticated user by constructing a `ClaimsPrincipal`:

```csharp
private void SetupAuthenticatedUser(Guid userId, string role = "User")
{
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        new Claim(ClaimTypes.Email, "test@example.com"),
        new Claim(ClaimTypes.Name, "Test User"),
        new Claim(ClaimTypes.Role, role)
    };
    var identity = new ClaimsIdentity(claims, "TestAuth");
    _controller.ControllerContext = new ControllerContext
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
    };
}
```
