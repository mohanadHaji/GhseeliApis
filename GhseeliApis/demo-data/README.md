# Frontend demo data

`frontend-demo-data.json` is the canonical, deterministic fixture package for
local frontend development. Every record is fictional and visibly marked as
demo data.

## Safety

- The dataset metadata says `datasetType: "demo"` and
  `localDevelopmentOnly: true`.
- Visible companies, customers, branches, addresses, and plates contain
  `[DEMO]`, `تجريبي`, or `DEMO-` markers.
- All account emails use the reserved `example.test` domain.
- The seeder accepts only LocalDB/localhost databases whose names contain
  `Demo`, and rejects names containing `Production`.
- The seeder is not called by either API startup or any deployment workflow.
- No real credentials, payment instruments, customer details, or production
  provider transactions are included.

## Contents

| Data | Count |
|---|---:|
| Companies | 3 |
| Business users | 5 |
| Branches | 5 |
| Categories | 4 |
| Offerings | 12 |
| Add-on groups / choices | 6 / 18 |
| Customers | 5 |
| Devices | 6 |
| Vehicles / addresses | 8 / 7 |
| Checkout drafts | 4 |
| Customer bookings | 8 |
| Business reservations / work orders | 8 / 8 |
| Payment examples | 5 |

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
are listed under `businessUsers`. The JSON also includes valid local device
tokens for `X-Device-Token`. These credentials are intentionally obvious test
fixtures and are rejected from non-demo database destinations by the seeder.

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
string to its matching LocalDB database using user secrets. Customer catalog
browsing also requires the three `companies[].id` values from the JSON under:

```text
CatalogReadModel__Providers__0__SourceCompanyId
CatalogReadModel__Providers__0__Enabled=true
CatalogReadModel__Providers__0__Order=0
```

Repeat the same three settings for indexes `1` and `2`. This is intentional:
the Customer API exposes only explicitly configured Business providers. Do not
copy demo connection strings, provider IDs, or fixture credentials into
production configuration.
