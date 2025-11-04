# ?? Controllers Creation Plan - Car Washing Service

## Overview

Creating REST API controllers for all entities without authentication/authorization (for now).

## Controller Architecture

Each controller follows this pattern:
```
Controller ? Handler ? Repository ? Database
     ?           ?
  Validation  Business Logic
```

## Controllers to Create

### 1. ? UsersController (Already Exists)
- Manages car owners
- CRUD operations for users

### 2. ?? CompaniesController
**Purpose:** Manage car washing service companies

**Endpoints:**
```
GET    /api/companies           - Get all companies
GET    /api/companies/{id}      - Get company by ID
GET    /api/companies/area/{area} - Get companies by service area
POST   /api/companies           - Create new company
PUT    /api/companies/{id}      - Update company
DELETE /api/companies/{id}      - Delete company
```

**Related Entities:**
- Company
- CompanyAvailability (nested)
- ServiceOption (nested)

### 3. ?? VehiclesController
**Purpose:** Manage user vehicles

**Endpoints:**
```
GET    /api/vehicles            - Get all vehicles (for current user)
GET    /api/vehicles/{id}       - Get vehicle by ID
POST   /api/vehicles            - Add new vehicle
PUT    /api/vehicles/{id}       - Update vehicle
DELETE /api/vehicles/{id}       - Delete vehicle
```

**Business Rules:**
- Vehicle must belong to the requesting user
- Cannot delete vehicle with active bookings

### 4. ?? AddressesController
**Purpose:** Manage user addresses

**Endpoints:**
```
GET    /api/addresses           - Get all addresses (for current user)
GET    /api/addresses/{id}      - Get address by ID
POST   /api/addresses           - Add new address
PUT    /api/addresses/{id}      - Update address
DELETE /api/addresses/{id}      - Delete address
PUT    /api/addresses/{id}/set-primary - Set as primary address
```

**Business Rules:**
- Only one primary address per user
- Cannot delete address with active bookings

### 5. ?? ServicesController
**Purpose:** Manage service types and options

**Endpoints:**
```
GET    /api/services            - Get all services
GET    /api/services/{id}       - Get service by ID
GET    /api/services/{id}/options - Get service options
POST   /api/services            - Create new service (admin)
PUT    /api/services/{id}       - Update service (admin)
DELETE /api/services/{id}       - Delete service (admin)
```

### 6. ?? ServiceOptionsController
**Purpose:** Manage specific service offerings with pricing

**Endpoints:**
```
GET    /api/service-options      - Get all service options
GET    /api/service-options/{id} - Get service option by ID
GET    /api/service-options/company/{companyId} - Get by company
POST   /api/service-options      - Create service option
PUT    /api/service-options/{id} - Update service option
DELETE /api/service-options/{id} - Delete service option
```

### 7. ?? BookingsController
**Purpose:** Manage service bookings (core business logic)

**Endpoints:**
```
GET    /api/bookings            - Get all bookings (filtered by user/company)
GET    /api/bookings/{id}       - Get booking by ID
GET    /api/bookings/user/{userId} - Get user's bookings
GET    /api/bookings/company/{companyId} - Get company's bookings
GET    /api/bookings/upcoming   - Get upcoming bookings
GET    /api/bookings/history    - Get past bookings
POST   /api/bookings            - Create new booking
PUT    /api/bookings/{id}       - Update booking
PUT    /api/bookings/{id}/confirm - Confirm booking (company)
PUT    /api/bookings/{id}/start - Start service (company)
PUT    /api/bookings/{id}/complete - Complete service (company)
PUT    /api/bookings/{id}/cancel - Cancel booking
DELETE /api/bookings/{id}       - Delete booking
```

**Business Rules:**
- User must have vehicle before booking
- Check company availability
- Validate booking time slot
- Calculate total price
- Check for conflicts

### 8. ?? PaymentsController
**Purpose:** Handle payment transactions

**Endpoints:**
```
GET    /api/payments            - Get all payments (for user)
GET    /api/payments/{id}       - Get payment by ID
GET    /api/payments/booking/{bookingId} - Get booking payment
POST   /api/payments            - Create payment (process)
POST   /api/payments/{id}/confirm - Confirm payment
POST   /api/payments/{id}/refund - Refund payment
```

**Integration Points:**
- Payment gateway (placeholder for now)
- Wallet system

### 9. ?? WalletsController
**Purpose:** Manage user wallet balance

**Endpoints:**
```
GET    /api/wallets/my-wallet    - Get current user's wallet
GET    /api/wallets/{userId}     - Get user wallet (admin)
POST   /api/wallets/top-up       - Add funds to wallet
GET    /api/wallets/transactions - Get wallet transactions
```

### 10. ?? NotificationsController
**Purpose:** Manage user notifications

**Endpoints:**
```
GET    /api/notifications        - Get all notifications (for user)
GET    /api/notifications/unread - Get unread notifications
PUT    /api/notifications/{id}/read - Mark as read
PUT    /api/notifications/read-all - Mark all as read
DELETE /api/notifications/{id}   - Delete notification
```

### 11. ?? AvailabilityController
**Purpose:** Manage company availability schedules

**Endpoints:**
```
GET    /api/availability/company/{companyId} - Get company availability
GET    /api/availability/company/{companyId}/date/{date} - Check specific date
POST   /api/availability         - Add availability slot
PUT    /api/availability/{id}    - Update availability slot
DELETE /api/availability/{id}    - Delete availability slot
```

## Implementation Order

### Phase 1: Foundation (Create First)
1. **CompaniesController** - Core entity
2. **ServicesController** - Service types
3. **ServiceOptionsController** - Service pricing

### Phase 2: User Domain
4. **VehiclesController** - User vehicles
5. **AddressesController** - User addresses

### Phase 3: Booking & Payment
6. **BookingsController** - Main business logic
7. **PaymentsController** - Payment processing
8. **WalletsController** - Wallet management

### Phase 4: Supporting Features
9. **NotificationsController** - User alerts
10. **AvailabilityController** - Schedule management

## Components Needed for Each Controller

### For Each Entity, Create:

1. **Repository Interface** (`IXRepository.cs`)
   ```csharp
   Task<List<X>> GetAllAsync();
   Task<X?> GetByIdAsync(Guid id);
   Task<X> AddAsync(X entity);
   Task<X> UpdateAsync(X entity);
   Task DeleteAsync(X entity);
   ```

2. **Repository Implementation** (`XRepository.cs`)
   - Database access via EF Core
   - Include related entities
   - Proper async/await

3. **Handler Interface** (`IXHandler.cs`)
   ```csharp
   Task<List<X>> GetAllAsync();
   Task<X?> GetByIdAsync(Guid id);
   Task<X> CreateAsync(X entity);
   Task<X?> UpdateAsync(Guid id, X entity);
   Task<bool> DeleteAsync(Guid id);
   ```

4. **Handler Implementation** (`XHandler.cs`)
   - Business logic
   - Validation
   - Logging
   - Error handling

5. **Controller** (`XController.cs`)
   - HTTP endpoints
   - Input validation
   - Status codes
   - Response formatting

6. **DTOs** (if needed)
   - Request DTOs
   - Response DTOs
   - Prevents over-posting

## Sample Controller Structure

```csharp
[ApiController]
[Route("api/[controller]")]
public class CompaniesController : ControllerBase
{
    private readonly ICompanyHandler _companyHandler;
    private readonly IAppLogger _logger;

    public CompaniesController(ICompanyHandler companyHandler, IAppLogger logger)
    {
        _companyHandler = companyHandler;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var companies = await _companyHandler.GetAllAsync();
        return Ok(companies);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var company = await _companyHandler.GetByIdAsync(id);
        if (company == null)
            return NotFound();
        return Ok(company);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Company company)
    {
        var created = await _companyHandler.CreateAsync(company);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] Company company)
    {
        var updated = await _companyHandler.UpdateAsync(id, company);
        if (updated == null)
            return NotFound();
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _companyHandler.DeleteAsync(id);
        if (!deleted)
            return NotFound();
        return NoContent();
    }
}
```

## Validation Strategy

For each controller, validate:
1. **ID Format** - Use Guid validation
2. **Required Fields** - Check for null/empty
3. **Business Rules** - Handler layer
4. **Relationships** - Foreign key validation
5. **Authorization** - (Later, when adding auth)

## Error Handling Strategy

```csharp
try
{
    // Operation
}
catch (DbUpdateException ex)
{
    _logger.LogError("Database error", ex);
    return StatusCode(500, "Database error");
}
catch (Exception ex)
{
    _logger.LogError("Unexpected error", ex);
    return StatusCode(500, "An error occurred");
}
```

## Registration in Program.cs

```csharp
// Repositories
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<IVehicleRepository, VehicleRepository>();
builder.Services.AddScoped<IAddressRepository, AddressRepository>();
// ... etc

// Handlers
builder.Services.AddScoped<ICompanyHandler, CompanyHandler>();
builder.Services.AddScoped<IVehicleHandler, VehicleHandler>();
builder.Services.AddScoped<IAddressHandler, AddressHandler>();
// ... etc
```

## Testing Strategy

For each controller, create tests:
1. **GetAll** - Returns list
2. **GetById** - Returns entity / 404
3. **Create** - Returns 201 Created
4. **Update** - Returns 200 / 404
5. **Delete** - Returns 204 / 404

## Next Steps

Ready to create controllers in this order:
1. ? CompaniesController (foundation)
2. ? VehiclesController (user domain)
3. ? AddressesController (user domain)
4. ? ServicesController (catalog)
5. ? ServiceOptionsController (catalog)
6. ? BookingsController (core business)
7. ? PaymentsController (transactions)
8. ? WalletsController (balance)
9. ? NotificationsController (alerts)
10. ? AvailabilityController (scheduling)

**Shall I proceed with creating all these controllers?** ??
