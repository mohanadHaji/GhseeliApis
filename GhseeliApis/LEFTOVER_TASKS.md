# Leftover Tasks

## Missing API Endpoints

Models and DB tables exist but no controller or handler yet.

### Wallet / WalletTransaction
- `GET /api/wallet/me` — Get current user's wallet balance
- `GET /api/wallet/me/transactions` — Get transaction history
- `POST /api/wallet/me/topup` — Add funds to wallet
- Wallet payment method in bookings should deduct from balance

### Notification
- `GET /api/notifications/me` — Get current user's notifications
- `PUT /api/notifications/{id}/read` — Mark as read
- `PUT /api/notifications/me/read-all` — Mark all as read
- `DELETE /api/notifications/{id}` — Delete notification

## Missing User Features

### Password Change
- `PUT /api/users/me/password` — Requires current password + new password
- Use `UserManager.ChangePasswordAsync()`

### Email Verification After Email Change
- When user changes email via `PUT /api/users/me`, send verification email
- New email only becomes active after confirmation

### Soft Delete with Grace Period
- `DELETE /api/users/me` currently hard-deletes immediately
- Replace with: set `IsActive = false`, schedule permanent deletion (e.g., 30 days)
- Allow reactivation within grace period

## Testing Gaps

### Integration Tests
- End-to-end API tests using `WebApplicationFactory<Program>`
- Auth flow tests (register → login → access protected endpoint)
- Booking flow tests (create booking → pay → confirm → complete)

### Stripe Integration Tests
- Test with Stripe test-mode API and test cards (`4242 4242 4242 4242`)
- Webhook event simulation

## Documentation

### README.md Outdated References
- Inner `GhseeliApis/README.md` still references "Google Cloud SQL" in the badges and architecture diagram
- Should say "SQL Server" since the MySQL→MSSQL migration

## External Setup (Manual)

- Configure Google OAuth app in Google Cloud Console with correct redirect URIs
- Configure Facebook OAuth app with correct redirect URIs
- Configure Stripe production webhook endpoint pointing to `/api/stripe/webhook`
