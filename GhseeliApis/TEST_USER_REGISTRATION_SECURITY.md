# Testing User Registration with Role Escalation Protection

## Quick Test Commands

### ? Test 1: Normal Self-Registration (Should Succeed)

```bash
curl -X POST https://gasli.runasp.net/api/users \
  -H "Content-Type: application/json" \
  -d '{
    "email": "newuser@example.com",
    "password": "SecurePass123",
    "fullName": "New User",
    "phone": "+1234567890"
  }'
```

**Expected Response:** `201 Created`
```json
{
  "id": "...",
  "email": "newuser@example.com",
  "fullName": "New User",
  "roles": ["User"],
  "isActive": true
}
```

---

### ? Test 2: Try to Register as Admin (Should Fail)

```bash
curl -X POST https://gasli.runasp.net/api/users \
  -H "Content-Type: application/json" \
  -d '{
    "email": "hacker@example.com",
    "password": "SecurePass123",
    "fullName": "Wannabe Admin",
    "phone": "+1234567890",
    "role": "Admin"
  }'
```

**Expected Response:** `400 Bad Request`
```json
{
  "message": "Cannot self-register with elevated roles. Contact an administrator to request elevated privileges."
}
```

---

### ? Test 3: Try to Register as Company (Should Fail)

```bash
curl -X POST https://gasli.runasp.net/api/users \
  -H "Content-Type: application/json" \
  -d '{
    "email": "company@example.com",
    "password": "SecurePass123",
    "fullName": "Company Owner",
    "phone": "+1234567890",
    "role": "Company"
  }'
```

**Expected Response:** `400 Bad Request`
```json
{
  "message": "Cannot self-register with elevated roles. Contact an administrator to request elevated privileges."
}
```

---

### ? Test 4: Try to Access Admin Endpoints Without Auth (Should Fail)

```bash
# Try to get all users
curl -X GET https://gasli.runasp.net/api/users
```

**Expected Response:** `401 Unauthorized`

---

### ? Test 5: Try to Access Admin Endpoints With User Role (Should Fail)

```bash
# First, register and login to get a token
curl -X POST https://gasli.runasp.net/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "regularuser@example.com",
    "password": "SecurePass123",
    "fullName": "Regular User",
    "phone": "+1234567890",
    "role": "User"
  }'

# Extract token from response, then:
curl -X GET https://gasli.runasp.net/api/users \
  -H "Authorization: Bearer <user_token>"
```

**Expected Response:** `403 Forbidden`

---

## PowerShell Test Script

Save as `test-user-registration-security.ps1`:

```powershell
$baseUrl = "https://gasli.runasp.net"

Write-Host "`n=== Testing User Registration Security ===" -ForegroundColor Cyan

# Test 1: Normal registration
Write-Host "`n[Test 1] Normal self-registration..." -ForegroundColor Yellow
$response1 = Invoke-RestMethod -Uri "$baseUrl/api/users" -Method Post -ContentType "application/json" -Body (@{
    email = "test$(Get-Random)@example.com"
    password = "SecurePass123"
    fullName = "Test User"
    phone = "+1234567890"
} | ConvertTo-Json) -ErrorAction SilentlyContinue

if ($response1.roles -contains "User") {
    Write-Host "? PASS: User registered with 'User' role" -ForegroundColor Green
} else {
    Write-Host "? FAIL: Unexpected role assignment" -ForegroundColor Red
}

# Test 2: Try to register as Admin
Write-Host "`n[Test 2] Attempting to register as Admin..." -ForegroundColor Yellow
try {
    $response2 = Invoke-RestMethod -Uri "$baseUrl/api/users" -Method Post -ContentType "application/json" -Body (@{
        email = "admin$(Get-Random)@example.com"
        password = "SecurePass123"
        fullName = "Wannabe Admin"
        phone = "+1234567890"
        role = "Admin"
    } | ConvertTo-Json) -ErrorAction Stop
    
    Write-Host "? FAIL: Should have been rejected!" -ForegroundColor Red
} catch {
    if ($_.Exception.Response.StatusCode -eq 400) {
        Write-Host "? PASS: Admin role escalation blocked (400 Bad Request)" -ForegroundColor Green
    } else {
        Write-Host "??  UNEXPECTED: Got status $($_.Exception.Response.StatusCode)" -ForegroundColor Yellow
    }
}

# Test 3: Try to register as Company
Write-Host "`n[Test 3] Attempting to register as Company..." -ForegroundColor Yellow
try {
    $response3 = Invoke-RestMethod -Uri "$baseUrl/api/users" -Method Post -ContentType "application/json" -Body (@{
        email = "company$(Get-Random)@example.com"
        password = "SecurePass123"
        fullName = "Company Owner"
        phone = "+1234567890"
        role = "Company"
    } | ConvertTo-Json) -ErrorAction Stop
    
    Write-Host "? FAIL: Should have been rejected!" -ForegroundColor Red
} catch {
    if ($_.Exception.Response.StatusCode -eq 400) {
        Write-Host "? PASS: Company role escalation blocked (400 Bad Request)" -ForegroundColor Green
    } else {
        Write-Host "??  UNEXPECTED: Got status $($_.Exception.Response.StatusCode)" -ForegroundColor Yellow
    }
}

# Test 4: Try to access admin endpoint without auth
Write-Host "`n[Test 4] Attempting to access admin endpoint without auth..." -ForegroundColor Yellow
try {
    $response4 = Invoke-RestMethod -Uri "$baseUrl/api/users" -Method Get -ErrorAction Stop
    Write-Host "? FAIL: Should require authentication!" -ForegroundColor Red
} catch {
    if ($_.Exception.Response.StatusCode -eq 401) {
        Write-Host "? PASS: Unauthorized access blocked (401)" -ForegroundColor Green
    } else {
        Write-Host "??  UNEXPECTED: Got status $($_.Exception.Response.StatusCode)" -ForegroundColor Yellow
    }
}

Write-Host "`n=== Test Complete ===" -ForegroundColor Cyan
```

---

## Expected Behavior Summary

| Test | Endpoint | Role Requested | Expected Status | Expected Behavior |
|------|----------|---------------|-----------------|-------------------|
| 1 | POST /api/users | None (defaults to User) | `201 Created` | User created with "User" role |
| 2 | POST /api/users | Admin | `400 Bad Request` | Registration rejected with error message |
| 3 | POST /api/users | Company | `400 Bad Request` | Registration rejected with error message |
| 4 | GET /api/users | N/A (no auth) | `401 Unauthorized` | Access denied - authentication required |
| 5 | GET /api/users | User (with token) | `403 Forbidden` | Access denied - insufficient privileges |
| 6 | GET /api/users | Admin (with token) | `200 OK` | Access granted - returns user list |

---

## Logs to Check

After testing, check MonsterASP.NET logs for these security events:

**? Successful Registration:**
```
POST /api/users - Request received to create user: Email='newuser@example.com', FullName='New User', Role='User'
POST /api/users - User created successfully with ID=...
```

**? Blocked Role Escalation:**
```
POST /api/users - Request received to create user: Email='hacker@example.com', FullName='Wannabe Admin', Role='Admin'
POST /api/users - Attempt to self-register with elevated role 'Admin' by email 'hacker@example.com'
POST /api/users - Model validation failed
```

**? Unauthorized Access:**
```
GET /api/users - Request received (but rejected by authorization middleware)
```

---

## Security Validation Checklist

After deployment:

- [ ] Test normal user registration (should succeed)
- [ ] Test registration with "Admin" role (should fail with 400)
- [ ] Test registration with "Company" role (should fail with 400)
- [ ] Test registration with "USER" role (case-insensitive, should succeed and force to "User")
- [ ] Test GET /api/users without auth (should fail with 401)
- [ ] Test GET /api/users with User role token (should fail with 403)
- [ ] Test GET /api/users with Admin role token (should succeed with 200)
- [ ] Verify logs show security warnings for role escalation attempts
- [ ] Verify new users cannot see other users' data
- [ ] Verify new users cannot update/delete other users

---

## What Changed

### File: `GhseeliApis\Controllers\UsersController.cs`

**Added Security Validation:**
```csharp
// SECURITY: Prevent role escalation during self-registration
if (!string.IsNullOrEmpty(request.Role) && 
    !request.Role.Equals("User", StringComparison.OrdinalIgnoreCase))
{
    _logger.LogWarning($"POST /api/users - Attempt to self-register with elevated role '{request.Role}' by email '{request.Email}'");
    return BadRequest(new { Message = "Cannot self-register with elevated roles. Contact an administrator to request elevated privileges." });
}

// Force "User" role for all public registrations
request.Role = "User";
```

**Key Features:**
- ? Case-insensitive comparison (`StringComparison.OrdinalIgnoreCase`)
- ? Logs security warnings for audit trail
- ? Forces "User" role even if validation is bypassed
- ? Clear error message guides users to correct process
- ? Prevents both "Admin" and "Company" role escalation

---

## Related Endpoints

Remember you also have:

- **`POST /api/auth/register`** - Alternative registration endpoint (returns JWT token)
- **`POST /api/auth/login`** - Login endpoint
- **`GET /api/auth/me`** - Get current user info

**Recommendation:** Direct users to use `/api/auth/register` for normal registration flow.
