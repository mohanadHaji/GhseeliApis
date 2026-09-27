# Development Environment Follow-up

Status: Deferred until the two additional databases and Development hosting are
ready.

This note is intentionally separate from the current frontend API work. It
records the environment split required once dedicated Customer and Business
Development databases can be used.

## Target state

- Production Customer API uses only the Production Customer database.
- Production Business API uses only the Production Business database.
- Development Customer API uses the new Development Customer database.
- Development Business API uses the new Development Business database.
- Deterministic frontend/demo seed data exists only in Development.
- Production contains no seeded Demo accounts, devices, companies, catalog,
  drafts, bookings, reservations, work orders, payments, reviews, favorites, or
  banners.

## Required work

1. Provision separate Development Customer and Business hosts or deployment
   slots with distinct URLs.
2. Create a GitHub `Development` environment with separate protected secrets:
   database connections, JWT keys, both HMAC directions, SMTP/test delivery
   configuration, Lahza test credentials, and Web Deploy credentials.
3. Add a Development deployment workflow, or parameterize the existing
   workflow, without allowing Development secrets or URLs to fall back to
   Production values.
4. Apply each API's migrations to its owning Development database and verify
   database health before deploying application artifacts.
5. Point Customer-to-Business and Business-to-Customer internal clients at the
   Development hosts and use Development-only HMAC secrets.
6. Move `seed-hosted-demo.yml` to the GitHub `Development` environment, rename
   its confirmation and concurrency group accordingly, and hard-block
   Production connection strings/hostnames.
7. Seed and verify the deterministic dataset only in Development. Update
   `demo-data/FRONTEND_DEMO_HANDOFF.md` and frontend configuration to use the
   Development URLs.
8. Add an explicitly approved, narrow Production Demo-removal operation that:
   - verifies the exact Production targets;
   - takes or confirms restorable backups;
   - deletes only records with the trusted Demo partition;
   - runs Customer and Business cleanup in dependency-safe order;
   - reports exact sanitized deletion counts;
   - verifies normal Production records are unchanged.
9. Remove or disable the Production hosted-demo workflow after cleanup so Demo
   data cannot be recreated in Production accidentally.
10. Re-run Production catalog, authentication, booking, payment, internal HMAC,
    migration, and health smoke tests using approved non-fabricated Production
    accounts/data.
11. Add a Development-to-Production promotion runbook covering migration order,
    rollback, secret rotation, monitoring, and acceptance evidence.

## Safety requirements

- Never copy Production customer or business data into Development.
- Never use a public request field to select Production versus Demo/Development.
- Never reuse Production JWT, HMAC, SMTP, payment, or database credentials in
  Development.
- Never point the Demo seeder or destructive cleanup at Production without an
  exact manual confirmation, target verification, backups, and a reviewed
  deletion manifest.
- Keep production payment side effects disabled in Development and use only
  provider-approved test credentials.

## Completion evidence

The environment split is complete only when:

- both Development APIs and databases pass migrations and health checks;
- the full deterministic Demo fixture and frontend scenarios pass in
  Development;
- Production Demo rows are removed with reviewed count evidence;
- Production smoke tests pass afterward;
- the Production seed path is disabled; and
- the repository handoff and production-readiness ledger identify Development
  as the sole home of seeded frontend data.
