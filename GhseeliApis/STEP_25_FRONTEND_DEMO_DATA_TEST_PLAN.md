# Step 25 - Frontend demo data test plan

## Scope

Create a moderate, deterministic, clearly labeled fixture package for frontend
development and load matching records into isolated Customer and Business
LocalDB databases.

**HTTP required: No -** this step adds development tooling and data only; it
does not add or change an HTTP endpoint or runtime API behavior. Representative
API visibility remains an execution check after database seeding.

## Required behavior

- Export one canonical JSON file with explicit demo-only metadata.
- Include localized companies/catalog, customers, devices, vehicles,
  addresses, drafts, correlated bookings/reservations/work orders, and varied
  payment states.
- Use deterministic IDs and stable cross-system references.
- Use only fictional `.test` identities and obvious demo labels.
- Seed only LocalDB/localhost databases whose names contain `Demo`.
- Reject production-like, remote, or ambiguously named destinations.
- Repeat execution without duplicates.
- Keep the tooling outside both API runtime startup and deployment paths.

## Automated scenarios

| ID | Scenario |
|---|---|
| STEP25-DEMO-001 | Dataset has explicit demo/local-only metadata and moderate counts |
| STEP25-DEMO-002 | Visible labels, emails, phones, and payment references are fictional |
| STEP25-DEMO-003 | Repeated definition creation produces byte-stable JSON |
| STEP25-DEMO-004 | All customer/business/draft/booking references resolve |
| STEP25-DEMO-005 | LocalDB and localhost `Demo` destinations are accepted |
| STEP25-DEMO-006 | Remote, non-demo, production-named, and blank destinations are rejected |
| STEP25-DEMO-007 | Clean Customer and Business migrations seed successfully |
| STEP25-DEMO-008 | Repeated database seeding adds no duplicates |
| STEP25-DEMO-009 | Customer booking and Business reservation counts/correlation match |

## Execution

1. Run `Ghseeli.DemoData.Tests`.
2. Run `scripts\Seed-FrontendDemoData.ps1` against the named local demo
   databases.
3. Query representative table counts and cross-system references.
4. Run the complete solution tests and Release build.
5. Confirm no deployment workflow or API startup references the seeder.

## Results

- Focused demo-data tests: 11 passed, 0 failed, 0 skipped.
- Business API tests: 584 passed, 0 failed, 0 skipped.
- Customer API tests: 1,308 passed, 0 failed, 0 skipped.
- Complete automated total: 1,903 passed, 0 failed, 0 skipped.
- Release build: 0 warnings, 0 errors.
- Persistent LocalDB seed:
  - `GhseeliCustomer_FrontendDemo`
  - `GhseeliBusiness_FrontendDemo`
- Repeat seed: passed; no duplicates were added and stored cross-system
  references were revalidated.
- Representative local HTTP checks: 3 passed, 0 failed, 0 deferred:
  - Customer password login with seeded device token: `200`.
  - Business owner password login: `200`.
  - Arabic Customer catalog browse: `200`, 3 businesses, explicit demo label.
- No production or deployed environment was accessed.
