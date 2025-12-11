# ? User Self-Service Endpoints Added

## Overview

Users can now manage their own accounts! Three new self-service endpoints have been added to allow authenticated users to view, update, and delete their own profiles without requiring admin intervention.

---

## What Changed

### New Endpoints Added

| Endpoint | Method | Authorization | Purpose |
|----------|--------|---------------|---------|
| `/api/users/me` | GET | `[Authorize]` | View own profile |
| `/api/users/me` | PUT | `[Authorize]` | Update own profile |
| `/api/users/me` | DELETE | `[Authorize]` | Delete own account |

### Existing Endpoint Modified

| Endpoint | Before | After | Change |
|----------|--------|-------|--------|
| `/api/users/{id}` | `[Authorize(Roles = "Admin")]` | `[Authorize]` | Users can view own profile, admins can view any profile |

---

## Complete API Reference

### Self-Service Endpoints (Any Authenticated User)

#### 1. GET `/api/users/me` - Get My Profile

**Authorization:** Bearer token (any authenticated user)

**Request:**
```http
GET /api/users/me
Authorization: Bearer <your_jwt_token>
```

**Response (200 OK):**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "john.doe@example.com",
  "fullName": "John Doe",
  "phone": "+1234567890",
  "isActive": true,
  "createdAt": "2024-01-15T10:30:00Z",
  "updatedAt": "2024-12-11T14:00:00Z",
  "roles": ["User"],
  "vehicleCount": 2,
  "addressCount": 1,
  "bookingCount": 5,
  "walletBalance": 150.00
}
```

---

#### 2. PUT `/api/users/me` - Update My Profile

**Authorization:** Bearer token (any authenticated user)

**What You Can Update:**
- ? Email
- ? Full Name
- ? Phone Number
- ? Role (admin only)
- ? IsActive status (admin only)

**Request:**
```http
PUT /api/users/me
Authorization: Bearer <your_jwt_token>
Content-Type: application/json

{
  "email": "newemail@example.com",
  "fullName": "John Smith",
  "phone": "+9876543210"
}
```

**Response (200 OK):**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "newemail@example.com",
  "fullName": "John Smith",
  "phone": "+9876543210",
  "isActive": true,
  "createdAt": "2024-01-15T10:30:00Z",
  "updatedAt": "2024-12-11T14:30:00Z",
  "roles": ["User"],
  "vehicleCount": 2,
  "addressCount": 1,
  "bookingCount": 5,
  "walletBalance": 150.00
}
```

**Security - Attempting to Change Role (400 Bad Request):**
```http
PUT /api/users/me
Authorization: Bearer <your_jwt_token>
Content-Type: application/json

{
  "email": "test@example.com",
  "role": "Admin"  ? Not allowed!
}
```

**Response:**
```json
{
  "message": "Cannot change role or active status. Contact an administrator."
}
```

---

#### 3. DELETE `/api/users/me` - Delete My Account

**Authorization:** Bearer token (any authenticated user)

**?? Warning:** This action is permanent and cannot be undone!

**Request:**
```http
DELETE /api/users/me
Authorization: Bearer <your_jwt_token>
```

**Response (204 No Content):**
```
(Empty body - account deleted successfully)
```

---

### Modified Endpoint

#### GET `/api/users/{id}` - Get User By ID

**Authorization:** Bearer token (any authenticated user)

**Access Rules:**
- ? Users can view their **own** profile
- ? Admins can view **any** profile
- ? Regular users **cannot** view other users' profiles

**Example 1: User viewing their own profile (Allowed)**
```http
GET /api/users/3fa85f64-5717-4562-b3fc-2c963f66afa6
Authorization: Bearer <token_for_user_3fa85f64>

Response: 200 OK (profile data)
```

**Example 2: User trying to view another user's profile (Forbidden)**
```http
GET /api/users/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
Authorization: Bearer <token_for_user_3fa85f64>

Response: 403 Forbidden
{
  "message": "You can only view your own profile unless you are an admin"
}
```

**Example 3: Admin viewing any user's profile (Allowed)**
```http
GET /api/users/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
Authorization: Bearer <admin_token>

Response: 200 OK (profile data)
```

---

### Admin-Only Endpoints (Unchanged)

| Endpoint | Method | Authorization | Purpose |
|----------|--------|---------------|---------|
| `/api/users` | GET | `[Authorize(Roles = "Admin")]` | List all users |
| `/api/users` | POST | `[AllowAnonymous]` | Create user (public registration) |
| `/api/users/{id}` | PUT | `[Authorize(Roles = "Admin")]` | Admin update any user |
| `/api/users/{id}` | DELETE | `[Authorize(Roles = "Admin")]` | Admin delete any user |

---

## Authorization Matrix

### GET Operations

| Endpoint | Anonymous | User (Own) | User (Others) | Admin |
|----------|-----------|------------|---------------|-------|
| `GET /api/users` | ? | ? | ? | ? |
| `GET /api/users/{id}` | ? | ? (own ID) | ? | ? |
| `GET /api/users/me` | ? | ? | N/A | ? |

### UPDATE Operations

| Endpoint | Anonymous | User (Own) | User (Others) | Admin |
|----------|-----------|------------|---------------|-------|
| `PUT /api/users/{id}` | ? | ? | ? | ? |
| `PUT /api/users/me` | ? | ? | N/A | ? |

**Note:** Users updating via `/api/users/me` **cannot** change their role or active status.

### DELETE Operations

| Endpoint | Anonymous | User (Own) | User (Others) | Admin |
|----------|-----------|------------|---------------|-------|
| `DELETE /api/users/{id}` | ? | ? | ? | ? |
| `DELETE /api/users/me` | ? | ? | N/A | ? |

### CREATE Operations

| Endpoint | Anonymous | User | Admin |
|----------|-----------|------|-------|
| `POST /api/users` | ? (public registration) | ? | ? |

---

## Security Features

### 1. **Role Escalation Prevention**

Users **cannot** promote themselves to elevated roles:

```http
PUT /api/users/me
{
  "role": "Admin"  ? BLOCKED
}

Response: 400 Bad Request
{
  "message": "Cannot change role or active status. Contact an administrator."
}
```

### 2. **Active Status Protection**

Users **cannot** change their active status:

```http
PUT /api/users/me
{
  "isActive": false  ? BLOCKED
}

Response: 400 Bad Request
{
  "message": "Cannot change role or active status. Contact an administrator."
}
```

### 3. **Profile Privacy**

Users **cannot** view other users' profiles:

```http
GET /api/users/other-user-id
Authorization: Bearer <regular_user_token>

Response: 403 Forbidden
{
  "message": "You can only view your own profile unless you are an admin"
}
```

### 4. **Token-Based Identity**

User ID is extracted from JWT token (`ClaimTypes.NameIdentifier`), preventing spoofing:

```csharp
var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
```

User cannot pretend to be someone else even if they know another user's ID.

---

## Usage Examples

### Scenario 1: User Updates Their Profile

```bash
# 1. Login to get token
curl -X POST https://gasli.runasp.net/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "john@example.com",
    "password": "SecurePass123"
  }'

# Response includes token:
# { "token": "eyJhbGc...", "userId": "...", ... }

# 2. Update profile using token
curl -X PUT https://gasli.runasp.net/api/users/me \
  -H "Authorization: Bearer eyJhbGc..." \
  -H "Content-Type: application/json" \
  -d '{
    "fullName": "John Smith",
    "phone": "+9876543210"
  }'

# Response: 200 OK with updated profile
```

---

### Scenario 2: User Views Their Profile

```bash
# Get own profile
curl -X GET https://gasli.runasp.net/api/users/me \
  -H "Authorization: Bearer eyJhbGc..."

# Response: 200 OK with profile data
```

---

### Scenario 3: User Tries to View Another User's Profile (Fails)

```bash
# Try to access another user by ID
curl -X GET https://gasli.runasp.net/api/users/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx \
  -H "Authorization: Bearer eyJhbGc..."

# Response: 403 Forbidden
# { "message": "You can only view your own profile unless you are an admin" }
```

---

### Scenario 4: Admin Views Any User's Profile (Succeeds)

```bash
# Admin can access any user by ID
curl -X GET https://gasli.runasp.net/api/users/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx \
  -H "Authorization: Bearer <admin_token>"

# Response: 200 OK with user's profile data
```

---

### Scenario 5: User Deletes Their Account

```bash
# Delete own account (permanent!)
curl -X DELETE https://gasli.runasp.net/api/users/me \
  -H "Authorization: Bearer eyJhbGc..."

# Response: 204 No Content (account deleted)
```

---

## Testing Checklist

### Self-Service Endpoints

- [ ] **GET /api/users/me**
  - [ ] Returns 401 without token
  - [ ] Returns 200 with valid token and user's profile data
  - [ ] Profile data matches authenticated user's ID

- [ ] **PUT /api/users/me**
  - [ ] Returns 401 without token
  - [ ] Returns 400 if trying to change `role`
  - [ ] Returns 400 if trying to change `isActive`
  - [ ] Returns 200 when updating `email`, `fullName`, or `phone`
  - [ ] Changes are persisted in database

- [ ] **DELETE /api/users/me**
  - [ ] Returns 401 without token
  - [ ] Returns 204 and deletes account
  - [ ] User cannot login after deletion
  - [ ] Token becomes invalid after deletion

### Modified Endpoint

- [ ] **GET /api/users/{id}**
  - [ ] Returns 401 without token
  - [ ] Returns 200 when user requests their own ID
  - [ ] Returns 403 when user requests another user's ID
  - [ ] Returns 200 when admin requests any user's ID

### Admin Endpoints (Unchanged)

- [ ] **PUT /api/users/{id}** (Admin)
  - [ ] Allows admin to change `role`
  - [ ] Allows admin to change `isActive`
  - [ ] Denies access to regular users

- [ ] **DELETE /api/users/{id}** (Admin)
  - [ ] Allows admin to delete any user
  - [ ] Denies access to regular users

---

## Implementation Details

### Code Changes in `UsersController.cs`

1. **Added `using System.Security.Claims;`** - Required for accessing JWT claims

2. **Modified `GetUserById` endpoint:**
   - Changed from `[Authorize(Roles = "Admin")]` to `[Authorize]`
   - Added logic to check if user is viewing own profile or is admin
   - Returns 403 Forbidden if regular user tries to view another user's profile

3. **Added `GetMyProfile` endpoint:**
   - Route: `GET /api/users/me`
   - Extracts user ID from JWT token
   - Returns authenticated user's profile

4. **Added `UpdateMyProfile` endpoint:**
   - Route: `PUT /api/users/me`
   - Extracts user ID from JWT token
   - **Blocks** attempts to change `role` or `isActive` status
   - Allows updating `email`, `fullName`, and `phone`

5. **Added `DeleteMyAccount` endpoint:**
   - Route: `DELETE /api/users/me`
   - Extracts user ID from JWT token
   - Permanently deletes the user's account

---

## Security Considerations

### ? What's Protected

1. **Token-Based Authentication**
   - User ID extracted from JWT token (cannot be spoofed)
   - Token validated by ASP.NET Core middleware

2. **Role Escalation Prevention**
   - Users cannot change their own role via `/api/users/me`
   - Admin-only endpoint `/api/users/{id}` required for role changes

3. **Account Status Protection**
   - Users cannot deactivate/reactivate their own account
   - Admin-only endpoint required for status changes

4. **Profile Privacy**
   - Users cannot view other users' profiles via `/api/users/{id}`
   - Only admins have access to all user profiles

### ?? Considerations

1. **Account Deletion is Permanent**
   - Consider adding a "soft delete" with grace period
   - Consider requiring password confirmation before deletion
   - Consider sending confirmation email after deletion

2. **Email Changes**
   - Consider requiring email verification after email change
   - Consider requiring password confirmation for email changes

3. **No Password Change Endpoint**
   - This implementation doesn't include password change
   - Consider adding `/api/users/me/password` endpoint
   - Should require current password for security

---

## Suggested Enhancements

### 1. Add Password Change Endpoint

```csharp
/// <summary>
/// Change current user's password
/// </summary>
[HttpPut("me/password")]
[Authorize]
public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
{
    var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    
    // Verify current password
    // Change to new password
    // Return success
}
```

### 2. Add Email Verification After Email Change

```csharp
[HttpPut("me")]
public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateUserRequest request)
{
    // ... existing code ...
    
    if (request.Email != null && request.Email != currentUser.Email)
    {
        // Send verification email
        // Mark email as unverified
        // Require confirmation before email becomes active
    }
}
```

### 3. Add Soft Delete with Grace Period

```csharp
[HttpDelete("me")]
public async Task<IActionResult> DeleteMyAccount()
{
    // Instead of immediate deletion:
    // 1. Mark account for deletion (IsActive = false, DeleteScheduledFor = 30 days from now)
    // 2. Send confirmation email
    // 3. Allow user to cancel within grace period
    // 4. Background job deletes after grace period expires
}
```

### 4. Add Account Deactivation (Instead of Deletion)

```csharp
/// <summary>
/// Deactivate account (can be reactivated later)
/// </summary>
[HttpPut("me/deactivate")]
[Authorize]
public async Task<IActionResult> DeactivateAccount()
{
    // Set IsActive = false
    // User can reactivate by logging in
}
```

---

## Related Documentation

- `USER_REGISTRATION_PUBLIC_ACCESS.md` - Public registration functionality
- `TEST_USER_REGISTRATION_SECURITY.md` - Security testing guide
- `USER_DTO_REFACTORING_COMPLETE.md` - DTO implementation details

---

## Comparison: Admin vs Self-Service

### Admin Endpoints (`/api/users/{id}`)

**Purpose:** Admin manages any user account

**Capabilities:**
- ? View any user's profile
- ? Update any user's email, name, phone
- ? **Change any user's role**
- ? **Change any user's active status**
- ? Delete any user account

**Authorization:** Requires `Admin` role

---

### Self-Service Endpoints (`/api/users/me`)

**Purpose:** User manages their own account

**Capabilities:**
- ? View own profile
- ? Update own email, name, phone
- ? **Cannot change own role**
- ? **Cannot change own active status**
- ? Delete own account

**Authorization:** Requires authentication (any role)

---

## Files Modified

1. **`GhseeliApis\Controllers\UsersController.cs`**
   - Added `using System.Security.Claims;`
   - Modified `GetUserById` to allow users to view own profile
   - Added `GetMyProfile` endpoint (`GET /api/users/me`)
   - Added `UpdateMyProfile` endpoint (`PUT /api/users/me`)
   - Added `DeleteMyAccount` endpoint (`DELETE /api/users/me`)

---

## Build Status

- ? Build successful
- ? No compilation errors
- ? Ready for testing

---

## Summary

Users can now:
1. ? **View** their own profile (`GET /api/users/me`)
2. ? **Update** their email, name, and phone (`PUT /api/users/me`)
3. ? **Delete** their account permanently (`DELETE /api/users/me`)

Security measures:
- ? Cannot change their role (admin only)
- ? Cannot change their active status (admin only)
- ? Cannot view other users' profiles (admin only)
- ? Identity verified via JWT token (cannot spoof)

The API now supports both:
- **Self-service** user management (common for customer-facing apps)
- **Admin** user management (for support and moderation)
