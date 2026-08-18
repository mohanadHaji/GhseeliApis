# Copilot Instructions — GhseeliApis

## Application Boundaries

The solution is being split into independently deployed backends:

- `GhseeliApis` — Customer API and customer database.
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

# Run the API locally
cd GhseeliApis
dotnet run

# Run the Business API locally
dotnet run --project Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj

# Run only Business API tests
dotnet test Ghseeli.BusinessApi.Tests\Ghseeli.BusinessApi.Tests.csproj

# EF Core migrations (run from GhseeliApis/GhseeliApis project dir)
dotnet ef migrations add MigrationName
dotnet ef database update

# Business API migrations (run from the solution directory)
dotnet ef migrations add MigrationName --project Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj --startup-project Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj --output-dir Persistence\Migrations
```

There is no separate lint command; use `dotnet build` for compiler and static validation.

Secrets (JWT key, connection strings, OAuth, Stripe) are loaded from **user secrets** in development. Use `dotnet user-secrets` to configure — never put real secrets in `appsettings.json`. Production deployment uses `publish-production.ps1` from the solution root, which does a clean Release build targeting `win-x64`.

Both API projects and the integration-contract project target .NET 8. Test projects target .NET 9. EF design-time commands require the owning API's configured connection string.

## Test-First Development

For every meaningful behavior change:

1. Define observable behavior and important failure/edge cases.
2. Write the appropriate unit, contract, integration, or feature tests first.
3. Run them and verify they fail for the expected missing behavior.
4. Implement the minimum correct behavior.
5. Run targeted and affected regression tests until green.
6. Add tests for requirements discovered during implementation before adding that behavior.

Do not weaken valid tests to fit an implementation or mark work complete with relevant failing tests. Do not create meaningless tests for passive DTOs or configuration-only files with no behavior.

## Architecture

Each ASP.NET Core 8 API follows the four-layer architecture where applicable:

```
Controllers → Handlers → Repositories → EF Core (SQL Server)
                ↕
             Services (Auth, Stripe)
```

- **Controllers** — Thin REST endpoints. Validate input, extract user claims, delegate to handlers. All routes follow `api/[controller]`.
- **Handlers** — Business logic layer. Orchestrate repository calls, enforce domain rules, return DTOs. Every entity domain has its own handler (e.g., `BookingHandler`, `UserHandler`).
- **Services** — Cross-cutting concerns: `AuthService` (JWT + OAuth), `StripePaymentService` (payments).
- **Repositories** — Data access via EF Core. One interface + implementation per entity. Each repo calls `SaveChangesAsync` internally.

All dependencies are registered as **Scoped** except `IAppLogger` which is **Singleton**.

At startup, `Program.cs` connects to the database to seed the `User`, `Company`, and `Admin` roles. A reachable database is required for the API to finish starting. Swagger is available at the application root only in Development.

## Domain Model

Core entities: **User**, **Company**, **Vehicle**, **UserAddress**, **Service**, **ServiceOption**, **Booking**, **Payment**, **Wallet**, **WalletTransaction**, **Notification**, **CompanyAvailability**.

- All entities use **Guid** primary keys.
- `User` extends `IdentityUser<Guid>` (ASP.NET Core Identity).
- Monetary fields use `decimal(18,2)`.
- Booking has a state machine: `Pending → Confirmed → InProgress → Completed/Cancelled/NoShow`.

## Key Conventions

### Validation

Domain models implement the project-specific `IValidatable` interface with a `Validate()` method returning `Interfaces.ValidationResult` (`IsValid` plus `Errors`). Follow the nearest feature's placement: some controllers validate constructed models, while handlers validate business-layer models. Request DTOs may also use data annotations for ASP.NET model binding.

### DTOs

DTOs live in `DTOs/` organized by feature subfolder. Follow this naming:

- `CreateXRequest` — POST body
- `UpdateXRequest` — PUT body
- `XResponse` — single-item response
- `XListResponse` — list-item response (simplified)

### Authorization

Three roles defined in `Constants/AppRoles.cs`: **User**, **Company**, **Admin**. Authorization policies combine them:

- `UserPolicy`, `CompanyPolicy`, `AdminPolicy` — single role
- `UserOrCompanyPolicy`, `CompanyOrAdminPolicy` — combined

Self-registration is forced to the "User" role to prevent privilege escalation. Users cannot modify their own role or active status via the `PUT /api/users/me` endpoint.

### Logging

Use the custom `IAppLogger` interface (not `ILogger<T>`). It provides `LogInfo`, `LogWarning`, and `LogError` methods. Inject it via constructor in all handlers and controllers.

### Testing

Tests use **xUnit** + **Moq** + **FluentAssertions**. The test project mirrors the main project structure:

- `Controllers/` — Controller tests mock handlers, set up `ClaimsPrincipal` via `SetupAuthenticatedUser()` helper
- `Handlers/` — Handler tests mock repositories
- `Models/` — Validation tests call `model.Validate()` directly
- `Services/` — Auth and Stripe service tests

Tests follow Arrange-Act-Assert with `/// <summary>` XML doc comments on test classes.

### Database

EF Core with SQL Server. Connection string resolution priority:
1. `RemoteTest` (user secrets — for testing against remote DB)
2. `Production` (environment variables)
3. `DefaultConnection` (appsettings.json)

Retry policy: 5 retries, 30s max delay, 60s command timeout. The `SqlServerSetupExtension` class configures all of this.

Runtime persistence uses SQL Server. Historical migrations still reference MySQL provider types, so the Pomelo package is required unless those migrations are converted.

### Stripe Integration

Payments flow through `IPaymentGatewayService` (implemented by `StripePaymentService`). The `StripeWebhookController` handles events at `POST /api/stripe/webhook` with signature verification. It processes `payment_intent.succeeded`, `payment_intent.payment_failed`, `charge.refunded`, and `payment_intent.canceled` events using `booking_id` from PaymentIntent metadata.

Only credit card payments go through Stripe. Other methods (Wallet, CashOnArrival, ThirdParty) are handled internally or are not yet implemented.

## Feature Wiring

New domains normally span model and `ApplicationDbContext` configuration, feature DTOs, repository interface/implementation, handler interface/implementation, controller, explicit scoped registration in `Program.cs`, and matching tests. Repositories persist writes; handlers own business workflows and DTO mapping; controllers remain focused on HTTP and authorization.

Configure relationships, indexes, delete behavior, string lengths, and decimal precision explicitly in `ApplicationDbContext`, then create an EF migration.

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
