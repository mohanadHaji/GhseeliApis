# Step 25A - Hosted demo data partition HTTP test results

## Execution

- Date: 2026-09-15
- Customer API: `http://localhost:5112`
- Business API: `http://localhost:5111`
- Customer database: `GhseeliCustomer_HostedDemoHttp`
- Business database: `GhseeliBusiness_HostedDemoHttp`
- Dataset: `demo-data/frontend-demo-data.json`
- Result: 17 passed, 0 failed, 0 deferred locally
- Hosted execution: pending deployment and controlled hosted seeding

## Scenario results

| ID | Result | Evidence |
|---|---|---|
| STEP25A-DEMO-001 | Pass | A normally registered Production device received `200` with 0 businesses. |
| STEP25A-DEMO-002 | Pass | The canonical Demo device received `200` with exactly 5 Demo businesses. |
| STEP25A-DEMO-003 | Pass | Demo Customer password login returned `200`; the JWT contained `ghseeli_data_partition=Demo`. |
| STEP25A-DEMO-004 | Pass | A Production Customer JWT paired with the Demo device was rejected with `403`. |
| STEP25A-DEMO-005 | Pass | A Demo Customer JWT paired with a Production device was rejected with `403` and `customer_authorization_forbidden`. |
| STEP25A-DEMO-006 | Pass | The canonical Demo device read its seeded draft with `200`; the Production device received `404` for the same draft. Automated relational coverage verifies booking ownership and partition filters. |
| STEP25A-DEMO-007 | Pass | A Production Customer and Production device received `404` when creating a payment intent for a known Demo booking reference. |
| STEP25A-DEMO-008 | Pass | Demo Business login returned `200`; the JWT contained `ghseeli_data_partition=Demo`. |
| STEP25A-DEMO-009 | Pass | The Demo owner read their Demo company and a known Demo offering with `200`. Automated relational coverage verifies reservation and work-order partition filters. |
| STEP25A-DEMO-010 | Pass | A newly registered Production Business owner received `404` for the same known Demo offering. Automated relational coverage verifies Demo work orders are also excluded. |
| STEP25A-DEMO-011 | Pass | Customer catalog `GET ?refresh=true` returned `200`, exactly 5 businesses, and `isStale=false`, exercising signed Demo propagation to Business. |
| STEP25A-DEMO-012 | Pass | An unsigned internal catalog request attempting `dataPartition=Demo` was rejected with `401`; signature and canonical partition behavior also passed automated tests. |
| STEP25A-DEMO-013 | Pass | Demo OTP request returned `202` and confirmation with fixed code `111111` returned `200` while the local API had no SMTP dependency configured. |
| STEP25A-DEMO-014 | Pass | Payment initialization for an otherwise payable Demo booking returned `409` with `booking_not_payable` before Lahza initialization. |
| STEP25A-DEMO-015 | Pass | A repeat seed reported the complete deterministic dataset already existed and added no duplicates. |
| STEP25A-DEMO-016 | Pass | Cleanup deleted exactly 5 Demo companies, 8 Demo customers, 12 Customer bookings, and 12 Business reservations; Demo login then returned `401`. |
| STEP25A-DEMO-017 | Pass | The Production device remained valid after cleanup and still received `200`; reseeding restored 5 Demo businesses and Demo login returned `200`. |

## Automated validation

| Project | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Ghseeli.BusinessApi.Tests | 586 | 0 | 0 |
| Ghseeli.CustomerApi.Tests | 1,311 | 0 | 0 |
| Ghseeli.DemoData.Tests | 16 | 0 | 0 |
| Total | 1,913 | 0 | 0 |

Release build completed with 0 warnings and 0 errors.

## Hosted status

No hosted schema, application, or data was changed during this execution. The
same scenarios must be rerun against the hosted URLs after explicit approval,
deployment, and guarded insertion of the canonical Demo records.
