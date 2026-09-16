# Frontend demo data

`frontend-demo-data.json` is the canonical, deterministic fixture package for
frontend development. Every record is fictional, visibly marked, and intended
to live only in the APIs' isolated `Demo` data partition.

## Safety

- The dataset metadata says `datasetType: "demo"` and
  `localDevelopmentOnly: false` because the same manifest supports both local
  Demo databases and the controlled Demo partition in the hosted databases.
- Visible companies, customers, branches, addresses, and plates contain
  `[DEMO]`, `تجريبي`, or `DEMO-` markers.
- All account emails use the reserved `example.test` domain.
- The seeder accepts only LocalDB/localhost databases whose names contain
  `Demo`, and rejects names containing `Production`. Hosted seeding remains a
  separate manual GitHub Actions operation and is never enabled by weakening
  this guard.
- The seeder is not called by either API startup or any deployment workflow.
- Public API request bodies, headers, and query strings cannot select the Demo
  partition. It is derived from seeded demo devices/accounts and signed
  cross-API requests.
- Demo OTP uses `111111` without sending email. Demo payment and push delivery
  must remain disabled so fixture use cannot trigger real side effects.
- No real credentials, payment instruments, customer details, or production
  provider transactions are included.

## Contents

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

The booking set covers `Pending`, `Confirmed`, `InProgress`, `Completed`,
`Cancelled`, and `NoShow`. Payment examples cover pending, completed, failed,
and refunded records. Drafts include priced, reprice-required, active, and
expired examples.

## Local accounts

All seeded Customer and Business accounts use the local demo password:

```text
Demo123!
```

Customer emails are listed under `customers`; Business owner/employee emails
are listed under `businessUsers`. The JSON also includes valid device tokens
for `X-Device-Token`. Customer password login uses `Demo123!`; email OTP login
uses `111111`. These credentials can access only Demo records.

## Generate and seed

From the solution directory:

```powershell
.\scripts\Seed-FrontendDemoData.ps1
```

This exports the JSON and migrates/seeds:

- `GhseeliCustomer_FrontendDemo`
- `GhseeliBusiness_FrontendDemo`

Running the command again is idempotent and does not add duplicates. To
regenerate only the frontend JSON:

```powershell
.\scripts\Seed-FrontendDemoData.ps1 -ExportOnly
```

To run the APIs against these databases, set each API's development connection
string to its matching LocalDB database using user secrets. Customer catalog browsing uses `CatalogReadModel:DemoProviders`, which contains
the five deterministic `companies[].id` values. Production provider
configuration remains separate under `CatalogReadModel:Providers`.

```text
CatalogReadModel__DemoProviders__0__SourceCompanyId
CatalogReadModel__DemoProviders__0__Enabled=true
CatalogReadModel__DemoProviders__0__Order=0
```

Repeat for indexes `1` through `4` when overriding configuration. This is
intentional: the Customer API exposes only explicitly configured Business
providers, and Demo requests never synchronize Production providers.

The committed IDs are also the cleanup manifest. Future cleanup or migration
must target only IDs present in this file and must retain all Production rows.

## Hosted Demo seeding

`.github/workflows/seed-hosted-demo.yml` is the only supported remote seeding
path. It requires the protected `Production` environment, the repository's
existing Customer and Business database secrets, the `master` branch, GitHub
Actions, and the exact manual confirmation `SEED HOSTED DEMO`.

The hosted command supports seeding only. Remote cleanup is intentionally not
available. The local `seed` and `cleanup` commands retain their strict
localhost/LocalDB `Demo` database guard.
