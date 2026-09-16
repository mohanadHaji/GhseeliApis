# Ghseeli frontend Demo environment

This environment contains fictional Demo records isolated from Production
records. Use these hosted APIs directly from the frontend.

## Hosted APIs

```text
Customer API: https://ghseelicustomer.runasp.net
Business API: https://ghseelibusiness.runasp.net
```

## Quick-start accounts

All Demo accounts use:

```text
Password: Demo123!
OTP code: 111111
```

Recommended Customer account:

```text
Email: maya.demo@example.test
Device token: LgidmxXzLYNVtE2mZ53e16wylIlPGsttB6HGC8IxUTs
```

Recommended Business account:

```text
Email: owner.sparkle@example.test
Password: Demo123!
```

The complete list of accounts, IDs, devices, companies, branches, catalog
records, drafts, bookings, and payment examples is available in:

```text
demo-data/frontend-demo-data.json
```

The JSON is the canonical description of the seeded records. It is not a
mocked API response: the frontend should still use the hosted APIs and compare
the returned IDs, relationships, states, and values with this file.

## What the frontend should expect

| Action | Expected result |
|---|---|
| Customer password login with a listed Demo account | `200`; JWT partition is `Demo` |
| Customer OTP request | `202`; no email is sent |
| Customer OTP confirmation with `111111` | `200` |
| Business login with a listed Demo account | `200`; JWT partition is `Demo` |
| Catalog browse with a Demo device | `200`; 5 Demo businesses |
| Catalog browse with `?refresh=true` | `200`; 5 Demo businesses |
| Read a draft owned by the supplied Demo device | `200` |
| Read a draft owned by another partition/device | `404` or authorization rejection |
| Use a Demo JWT with a Production device, or the reverse | `403` |
| Initialize card payment for a Demo booking | `409`, code `booking_not_payable` |
| Send `dataPartition` from a public client | It does not select or override the partition |

The catalog contains five businesses. Each has four offerings:

| Business | Branches | Categories | Offerings |
|---|---:|---:|---:|
| `[DEMO] Sparkle Mobile Wash` | 2 | 2 | 4 |
| `[DEMO] Blue Wave Auto Care` | 2 | 1 | 4 |
| `[DEMO] Green Garage Wash` | 1 | 1 | 4 |
| `[DEMO] City Shine Express` | 2 | 1 | 4 |
| `[DEMO] Royal Auto Spa` | 1 | 1 | 4 |

Expected booking-state distribution:

| State | Count |
|---|---:|
| Pending | 2 |
| Confirmed | 2 |
| InProgress | 2 |
| Completed | 3 |
| Cancelled | 2 |
| NoShow | 1 |

Expected payment-state distribution:

| State | Count |
|---|---:|
| Pending | 2 |
| Completed | 3 |
| Failed | 2 |
| Refunded | 1 |

Expected checkout-draft distribution:

| State | Count |
|---|---:|
| active | 2 |
| priced | 2 |
| needsReprice | 1 |
| expired | 1 |

The recommended Customer, `maya.demo@example.test`, owns two devices, two
vehicles, one address, one checkout draft, and two bookings. Other customers
have different combinations so list, empty-state, ownership, status, and
detail screens can be tested.

## Customer API usage

Customer routes that require a device must include:

```http
X-Device-Token: LgidmxXzLYNVtE2mZ53e16wylIlPGsttB6HGC8IxUTs
```

Password login:

```http
POST https://ghseelicustomer.runasp.net/api/Auth/login
Content-Type: application/json

{
  "email": "maya.demo@example.test",
  "password": "Demo123!"
}
```

OTP login:

```http
POST https://ghseelicustomer.runasp.net/api/Auth/otp/request
Content-Type: application/json

{
  "email": "maya.demo@example.test"
}
```

Confirm using:

```http
POST https://ghseelicustomer.runasp.net/api/Auth/otp/confirm
Content-Type: application/json

{
  "email": "maya.demo@example.test",
  "code": "111111"
}
```

Browse Demo businesses:

```http
GET https://ghseelicustomer.runasp.net/api/v1/catalog/businesses
X-Device-Token: LgidmxXzLYNVtE2mZ53e16wylIlPGsttB6HGC8IxUTs
```

Force a catalog refresh using the query parameter:

```http
GET https://ghseelicustomer.runasp.net/api/v1/catalog/businesses?refresh=true
X-Device-Token: LgidmxXzLYNVtE2mZ53e16wylIlPGsttB6HGC8IxUTs
```

Do not send `refresh=true` as a header.

## Catalog freshness

- `isStale: false`: display the catalog normally.
- `isStale: true`: the catalog is still usable, but the frontend may show a
  non-blocking localized stale-data notice.
- If mandatory backend refresh fails and no acceptable snapshot is available,
  the API returns `503 Service Unavailable`.

## Business API usage

Login:

```http
POST https://ghseelibusiness.runasp.net/api/v1/business/auth/login
Content-Type: application/json

{
  "email": "owner.sparkle@example.test",
  "password": "Demo123!"
}
```

Use the returned token:

```http
Authorization: Bearer <token>
```

Example company request:

```http
GET https://ghseelibusiness.runasp.net/api/v1/business/company
Authorization: Bearer <token>
```

## Dataset size

| Data | Count |
|---|---:|
| Companies | 5 |
| Business users | 8 |
| Branches | 8 |
| Categories | 6 |
| Offerings | 20 |
| Add-on groups / choices | 10 / 30 |
| Customers | 8 |
| Devices | 10 |
| Vehicles / addresses | 13 / 12 |
| Checkout drafts | 6 |
| Customer bookings | 12 |
| Business reservations / work orders | 12 / 12 |
| Payment examples | 8 |

## Safety rules

- Never send `isDemo`, `dataPartition`, or another environment-selection flag.
- The backend derives Demo access from the supplied Demo device or account.
- Demo and Production records are mutually isolated.
- Do not use real customer information with these Demo accounts.
- Demo OTP sends no email.
- Demo payment initialization does not contact Lahza.
- IDs and records are deterministic; do not rename or repurpose them.

## Additional Demo accounts

Customer emails:

```text
maya.demo@example.test
omar.demo@example.test
lina.demo@example.test
sami.demo@example.test
noor.demo@example.test
yousef.demo@example.test
rana.demo@example.test
adam.demo@example.test
```

Business emails:

```text
owner.sparkle@example.test
owner.bluewave@example.test
owner.green@example.test
employee.ramallah@example.test
employee.nablus@example.test
owner.cityshine@example.test
owner.royal@example.test
employee.jenin@example.test
```
