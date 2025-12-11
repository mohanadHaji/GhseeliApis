# Quick Test: User Self-Service Endpoints

## Prerequisites

1. Have a registered user account
2. Have a valid JWT token

## Test Suite

### Setup: Get Authentication Token

```bash
# Register new user
curl -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "testuser@example.com",
    "password": "TestPass123",
    "fullName": "Test User",
    "phone": "+1234567890",
    "role": "User"
  }'

# Extract token from response
TOKEN="<paste_token_here>"
```

---

## Test 1: View Own Profile ?

```bash
curl -X GET http://localhost:5000/api/users/me \
  -H "Authorization: Bearer $TOKEN"
```

**Expected:** `200 OK` with user profile data

---

## Test 2: Update Own Profile ?

```bash
curl -X PUT http://localhost:5000/api/users/me \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "fullName": "Updated Name",
    "phone": "+9876543210"
  }'
```

**Expected:** `200 OK` with updated profile

---

## Test 3: Try to Change Role (Should Fail) ?

```bash
curl -X PUT http://localhost:5000/api/users/me \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "role": "Admin"
  }'
```

**Expected:** `400 Bad Request`
```json
{
  "message": "Cannot change role or active status. Contact an administrator."
}
```

---

## Test 4: Try to View Another User's Profile (Should Fail) ?

```bash
# First, get another user's ID from admin endpoint or database
OTHER_USER_ID="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"

curl -X GET http://localhost:5000/api/users/$OTHER_USER_ID \
  -H "Authorization: Bearer $TOKEN"
```

**Expected:** `403 Forbidden`
```json
{
  "message": "You can only view your own profile unless you are an admin"
}
```

---

## Test 5: View Own Profile by ID ?

```bash
# Get your user ID from the profile response
YOUR_USER_ID="<your_user_id_from_token>"

curl -X GET http://localhost:5000/api/users/$YOUR_USER_ID \
  -H "Authorization: Bearer $TOKEN"
```

**Expected:** `200 OK` with profile data (same as `/api/users/me`)

---

## Test 6: Delete Own Account ?

```bash
curl -X DELETE http://localhost:5000/api/users/me \
  -H "Authorization: Bearer $TOKEN"
```

**Expected:** `204 No Content` (account deleted)

**Verify deletion:**
```bash
# Try to login with deleted account
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "testuser@example.com",
    "password": "TestPass123"
  }'
```

**Expected:** `400 Bad Request` (invalid credentials)

---

## Test 7: Access Without Token (Should Fail) ?

```bash
curl -X GET http://localhost:5000/api/users/me
```

**Expected:** `401 Unauthorized`

---

## PowerShell Test Script

Save as `test-user-self-service.ps1`:

```powershell
$baseUrl = "http://localhost:5000"

Write-Host "`n=== User Self-Service Endpoints Test ===" -ForegroundColor Cyan

# Test 1: Register user
Write-Host "`n[Test 1] Registering new user..." -ForegroundColor Yellow
$registerResponse = Invoke-RestMethod -Uri "$baseUrl/api/auth/register" -Method Post -ContentType "application/json" -Body (@{
    email = "testuser$(Get-Random)@example.com"
    password = "TestPass123"
    fullName = "Test User"
    phone = "+1234567890"
    role = "User"
} | ConvertTo-Json)

$token = $registerResponse.token
$userId = $registerResponse.userId
Write-Host "? User registered: $($registerResponse.email)" -ForegroundColor Green
Write-Host "   User ID: $userId" -ForegroundColor Gray
Write-Host "   Token: $($token.Substring(0, 30))..." -ForegroundColor Gray

# Test 2: Get own profile
Write-Host "`n[Test 2] Getting own profile..." -ForegroundColor Yellow
$headers = @{ Authorization = "Bearer $token" }
$profileResponse = Invoke-RestMethod -Uri "$baseUrl/api/users/me" -Method Get -Headers $headers
Write-Host "? Profile retrieved: $($profileResponse.fullName)" -ForegroundColor Green

# Test 3: Update own profile
Write-Host "`n[Test 3] Updating own profile..." -ForegroundColor Yellow
$updateResponse = Invoke-RestMethod -Uri "$baseUrl/api/users/me" -Method Put -Headers $headers -ContentType "application/json" -Body (@{
    fullName = "Updated Test User"
    phone = "+9876543210"
} | ConvertTo-Json)
Write-Host "? Profile updated: $($updateResponse.fullName), $($updateResponse.phone)" -ForegroundColor Green

# Test 4: Try to change role (should fail)
Write-Host "`n[Test 4] Attempting to change role to Admin (should fail)..." -ForegroundColor Yellow
try {
    Invoke-RestMethod -Uri "$baseUrl/api/users/me" -Method Put -Headers $headers -ContentType "application/json" -Body (@{
        role = "Admin"
    } | ConvertTo-Json) -ErrorAction Stop
    Write-Host "? FAIL: Should have been rejected!" -ForegroundColor Red
} catch {
    if ($_.Exception.Response.StatusCode -eq 400) {
        Write-Host "? PASS: Role escalation blocked (400 Bad Request)" -ForegroundColor Green
    } else {
        Write-Host "??  Unexpected status: $($_.Exception.Response.StatusCode)" -ForegroundColor Yellow
    }
}

# Test 5: Get own profile by ID
Write-Host "`n[Test 5] Getting own profile by ID..." -ForegroundColor Yellow
$profileByIdResponse = Invoke-RestMethod -Uri "$baseUrl/api/users/$userId" -Method Get -Headers $headers
Write-Host "? Profile retrieved by ID: $($profileByIdResponse.fullName)" -ForegroundColor Green

# Test 6: Try to access without token (should fail)
Write-Host "`n[Test 6] Attempting to access without token (should fail)..." -ForegroundColor Yellow
try {
    Invoke-RestMethod -Uri "$baseUrl/api/users/me" -Method Get -ErrorAction Stop
    Write-Host "? FAIL: Should require authentication!" -ForegroundColor Red
} catch {
    if ($_.Exception.Response.StatusCode -eq 401) {
        Write-Host "? PASS: Unauthorized access blocked (401)" -ForegroundColor Green
    } else {
        Write-Host "??  Unexpected status: $($_.Exception.Response.StatusCode)" -ForegroundColor Yellow
    }
}

# Test 7: Delete own account
Write-Host "`n[Test 7] Deleting own account..." -ForegroundColor Yellow
$deleteResponse = Invoke-RestMethod -Uri "$baseUrl/api/users/me" -Method Delete -Headers $headers
Write-Host "? Account deleted successfully" -ForegroundColor Green

# Test 8: Verify account is deleted (login should fail)
Write-Host "`n[Test 8] Verifying account deletion (login should fail)..." -ForegroundColor Yellow
try {
    Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post -ContentType "application/json" -Body (@{
        email = $registerResponse.email
        password = "TestPass123"
    } | ConvertTo-Json) -ErrorAction Stop
    Write-Host "? FAIL: Deleted user should not be able to login!" -ForegroundColor Red
} catch {
    Write-Host "? PASS: Login failed for deleted account" -ForegroundColor Green
}

Write-Host "`n=== Test Complete ===" -ForegroundColor Cyan
```

---

## Expected Results Summary

| Test | Endpoint | Expected Status | Expected Behavior |
|------|----------|-----------------|-------------------|
| 1 | POST /api/auth/register | 201 Created | User registered |
| 2 | GET /api/users/me | 200 OK | Returns profile |
| 3 | PUT /api/users/me | 200 OK | Updates profile |
| 4 | PUT /api/users/me (role) | 400 Bad Request | Role change blocked |
| 5 | GET /api/users/{own_id} | 200 OK | Returns profile |
| 6 | GET /api/users/me (no token) | 401 Unauthorized | Access denied |
| 7 | DELETE /api/users/me | 204 No Content | Account deleted |
| 8 | POST /api/auth/login (deleted) | 400 Bad Request | Login fails |

---

## Common Issues

### Issue 1: 401 Unauthorized

**Cause:** Token not provided or expired

**Solution:**
- Include `Authorization: Bearer <token>` header
- Get fresh token if expired (default 60 minutes)

### Issue 2: 403 Forbidden on GET /api/users/{id}

**Cause:** Trying to view another user's profile

**Solution:**
- Use `/api/users/me` instead
- Or ensure the ID matches your user ID from the token

### Issue 3: 400 Bad Request on PUT /api/users/me

**Cause:** Trying to change `role` or `isActive`

**Solution:**
- Remove `role` and `isActive` from request body
- Contact admin to change these fields

---

## Logs to Check

### Successful Operations

**Get Profile:**
```
GET /api/users/me - User {userId} requesting their profile
GET /api/users/me - Returning profile for user {userId}
```

**Update Profile:**
```
PUT /api/users/me - User {userId} updating their profile
PUT /api/users/me - User {userId} profile updated successfully
```

**Delete Account:**
```
DELETE /api/users/me - User {userId} requesting account deletion
DELETE /api/users/me - User {userId} account deleted successfully
```

### Security Events

**Role Escalation Attempt:**
```
PUT /api/users/me - User {userId} attempted to change role or active status
PUT /api/users/me - Model validation failed for user {userId}
```

**Unauthorized Profile Access:**
```
GET /api/users/{otherUserId} - User {userId} attempted to access another user's profile without admin rights
```

---

## Integration with Frontend

### React Example

```javascript
// Get own profile
const getMyProfile = async () => {
  const token = localStorage.getItem('token');
  const response = await fetch('http://localhost:5000/api/users/me', {
    headers: {
      'Authorization': `Bearer ${token}`
    }
  });
  return await response.json();
};

// Update profile
const updateMyProfile = async (updates) => {
  const token = localStorage.getItem('token');
  const response = await fetch('http://localhost:5000/api/users/me', {
    method: 'PUT',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(updates)
  });
  return await response.json();
};

// Delete account
const deleteMyAccount = async () => {
  const token = localStorage.getItem('token');
  await fetch('http://localhost:5000/api/users/me', {
    method: 'DELETE',
    headers: {
      'Authorization': `Bearer ${token}`
    }
  });
  // Clear token and redirect to login
  localStorage.removeItem('token');
  window.location.href = '/login';
};
```

---

## Security Checklist

After deployment, verify:

- [ ] `/api/users/me` requires authentication (401 without token)
- [ ] Users can view their own profile
- [ ] Users can update their email, name, phone
- [ ] Users **cannot** change their role
- [ ] Users **cannot** change their active status
- [ ] Users **cannot** view other users' profiles
- [ ] Users can delete their own account
- [ ] Deleted users cannot login
- [ ] Admins can still view/update/delete any user via `/api/users/{id}`

---

## Next Steps

1. ? Test locally with the PowerShell script
2. ? Verify all security measures work
3. ? Consider adding password change endpoint
4. ? Consider adding email verification after email change
5. ? Deploy to production
6. ? Update API documentation (Swagger)
7. ? Update frontend to use new endpoints
