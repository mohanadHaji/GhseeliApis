# Step 17B - Exhaustive Full-System Acceptance Test Plan

Status: Passed for all automated and safe-local scope; real Stripe deferred

Depends on:

- Step 17 quality/security gate at commit `13f2056`;
- Step 17A vertical-ready schema at commit `79345d8`.

This gate revalidates the complete deployed behavior after the Step 17A schema
change. It does not treat a previous pass, a test name, a raw manifest count,
or an unexecuted scenario as current evidence.

## 1. Acceptance integrity contract

"All possible tests" means complete coverage of the finite, caller-observable
contract and meaningful equivalence classes. It cannot honestly mean every
mathematically possible string, timestamp, interleaving, or JSON document.

This gate therefore requires:

1. every retained Customer and Business operation;
2. every body, route, query, and relevant header input;
3. every authentication scheme, role, assignment, and ownership boundary;
4. every allowed and rejected state transition;
5. every persistence, migration, idempotency, replay, and concurrency
   invariant;
6. every cross-API journey and compensating/recovery path;
7. every safe local dependency failure and restart path;
8. exact disclosure of all external, impossible, not-applicable, deferred,
   failed, or stopped-before-execution cases.

No aggregate count is allowed to hide a missing row. A scenario counts as
passed only after current-run execution reaches all of its assertions.

## 2. Frozen system inventory

### 2.1 HTTP operation ledger

`STEP_16_HTTP_TEST_PLAN.md` sections 2.1 and 2.2 remain the exact canonical
route, verb, host, and authentication ledger.

Source inspection currently finds:

| Host | Controller-declared `[Http*]` action methods |
|---|---:|
| Customer API | 55 |
| Business API | 47 |
| **Total** | **102** |

Explicit or framework-generated `HEAD` behavior, wrong verbs, `OPTIONS`,
unknown routes, unmatched internal-booking guards, removed routes, and
wrong-host probes are additional route contracts. They are not counted as new
domain endpoints and must still be executed through the Step 15-17 assets.

The ledger is complete only when runtime endpoint metadata and both OpenAPI
documents agree with the frozen list and every listed operation has:

- one success or intentionally non-success contract;
- missing/invalid authentication;
- wrong host and wrong credential type;
- applicable role and ownership cases;
- malformed binding and validation cases;
- localization and standard response/header assertions;
- persistence/no-side-effect assertions;
- deletion, version, state, idempotency, concurrency, and dependency cases
  when the operation exposes those behaviors.

### 2.2 Request input ledger

The current source contains 52 request declarations across 20 DTO/contract
files, including nested checkout, catalog, reservation, status, and payment
inputs. Controller primitive route/query values and security headers are part
of the same ledger even when no request DTO exists.

Each property is classified and covered using the following matrix. A cell may
be marked not applicable only with a type- or domain-specific reason.

| Input kind | Required equivalence and boundary cases |
|---|---|
| Required string | omitted, JSON `null`, empty, whitespace-only, one valid character, exact maximum, maximum + 1, unsafe control characters, valid Arabic, valid Hebrew |
| Optional string | omitted, `null`, blank normalization, valid value, exact maximum, maximum + 1 |
| Email/password/token | absent, malformed, valid, exact configured bounds, over-bound, wrong/expired/rotated/revoked credential, no response/log leakage |
| GUID/public reference | omitted/default, malformed text, empty GUID, unknown valid GUID, owned ID, foreign-owned ID, deleted ID |
| Integer/decimal/money | wrong JSON type, below minimum, minimum, ordinary value, maximum, above maximum, excessive precision, overflow |
| Boolean | omitted/default, `true`, `false`, wrong JSON type |
| Enum | omitted/default where invalid, each supported value, numeric/string wire form as documented, unknown value |
| Date/time/date-only | malformed, past, boundary-now, earliest allowed, latest allowed, too-far future, offset normalization, conflicting range |
| Collection | omitted, `null`, empty, one item, exact maximum, maximum + 1, duplicate IDs, foreign IDs, incompatible combination |
| Nested object | omitted, `null`, valid complete object, partially invalid object, mutually exclusive/snapshot ownership variants |
| Language | absent, `ar`, `he`, regional forms, mixed case, blank, duplicate, comma-delimited, unsupported, malformed quality values, all `q=0` |
| Concurrency/version | omitted, current, stale, future, negative/zero where forbidden, same-version simultaneous writers |
| Idempotency/correlation/nonce | absent, blank, valid, exact maximum, over-maximum, duplicate same body, duplicate different body, expired/replayed, concurrent duplicate |
| Device/JWT/HMAC/Stripe headers | absent, malformed, valid, wrong host/scheme/service/grant/signing key, expired, rotated key, replay, oversized |
| Body/media/transport | no body, malformed JSON, wrong content type, exact size limit, limit + 1, chunked limit + 1, safe UTF-8 |

Evidence may be unit, validator, repository, TestServer, contract, SQL, or live
HTTP according to `HTTP_TEST_PLAN_STANDARD.md`; caller-visible binding,
authorization, serialization, and response behavior require HTTP-level proof.

### 2.3 Roles and ownership

The following identities must be exercised against every applicable route:

- anonymous;
- registered device, missing device, expired device, inactive device, rotated
  old device token, and another installation;
- Customer `User` for self, another customer, owned and foreign saved
  vehicle/address/draft/booking/payment;
- Customer `Admin`, including routes where Admin is and is not sufficient;
- Business `Owner`, `Employee`, and `Admin`;
- same-company/same-branch, same-company/different-branch, different-company,
  inactive assignment, and unassigned business identities;
- opposite-host Customer and Business JWTs;
- internal HMAC clients with the correct grant, wrong operation grant, wrong
  service, active key, next key, stale key, bad signature, stale timestamp,
  replayed nonce, and reused idempotency key;
- valid and invalid Stripe signatures over the exact raw body.

Authentication does not replace authorization, assignment, ownership, state,
or request validation. Rejections must have the intended precedence and zero
unexpected side effects.

### 2.4 State and workflow ledger

Required state coverage:

- device registration, re-registration/rotation, inactivity, and expiry;
- catalog refresh current, changed, stale-usable, stale-unusable, malformed,
  unavailable, and recovered;
- checkout draft create/read/update/reprice, version conflict, expiration,
  incompatible catalog, foreign ownership, and anonymous-to-customer use;
- availability settings, schedules, overrides, service area, capacity, lead
  time, closure, conflict, and deletion/read-after-delete;
- Business catalog category/offering/add-on group/add-on choice create, read,
  update, deactivate/delete, invalid selection/default constraints, publication
  versioning, and read-after-delete;
- reservation/work-order and Customer booking transitions through pending,
  confirmed, in-progress, completed, cancelled, and no-show, including every
  forbidden transition, duplicate, out-of-order, stale sequence, and
  reconciliation result;
- payment eligibility, deterministic intent creation, duplicate request,
  provider failure/timeout/ambiguous commit, locally signed success/failure/
  cancellation/refund webhooks, duplicate/out-of-order event, and recovery;
- durable callback outbox pending, claimed/leased, retried, dead-lettered,
  requeued, delivered, lease lost, restart recovery, and concurrent workers.

The complete chained journey remains:

`device -> browse -> draft -> reprice -> customer login -> reserve ->
Business work order -> Customer booking -> business transitions -> callbacks
-> deterministic payment/webhook -> reconciliation`.

It must also run with a second customer, second business owner/company, worker,
branch, vehicle, and address to prove isolation rather than only success.

### 2.5 Schema and vertical-readiness ledger

Step 17A added behavior that is intentionally not exposed as a new HTTP
vertical-selection contract. HTTP requests remain car-wash-only.

Required acceptance evidence:

- clean and populated Customer migrations preserve/backfill immutable booking
  vertical snapshots;
- clean and populated Business migrations seed `car_wash`, create
  `BusinessVerticals`, `CompanyBusinessVerticals`, and
  `VehicleWorkOrderDetails`, preserve vehicle values, and support rollback;
- new companies receive exactly one active primary `car_wash` assignment;
- disabled registration, invalid primary assignments, inactive/future
  vertical reads, and snapshot mutation are rejected;
- reservation, work-order, and Customer booking snapshots match `car_wash`;
- vehicle extension create/reload/compatibility/cascade behavior persists;
- current Swagger and DTOs expose no future-vertical controls.

The Step 16 exact-schema live verifier must be updated with these three
Business-owned tables. That update is acceptance-infrastructure maintenance,
not permission to weaken the owned/foreign table check.

## 3. Evidence registry

### 3.1 Existing repeatable assets

| Asset | Registry size | Required current disposition |
|---|---:|---|
| Step 6-16 live manifests | 1,295 | 1,294 safe-local passed; real Stripe 057 external/deferred |
| Step 17 new scenarios | 118 | 58 live passed and 60 deterministic/contract passed |
| Step 16 lifecycle manifest | 790 | all passed in the fresh run |
| Automated .NET tests after Step 17A | 1,789 | all passed in the final run |
| HTTP harness self-tests | 30 | all passed |

These counts describe registered/executed rows, not necessarily unique
business behaviors. Traceability is proved by IDs and assertions, not by
summing overlapping orchestration.

### 3.2 Step 17B gap and regression register

| ID | Level | Observable contract and current status |
|---|---|---|
| `STEP17B-INFRA-SCHEMA-001` | Live-local + harness self-test | **Resolved and passed.** Expected-red was captured when the stale verifier reported the three Step 17A tables as extra. The verifier now includes the exact owned tables, permits only the required active registration-enabled `car_wash` reference seed during zero-domain verification, and retains exact-table rejection. The fixture lifecycle self-test and the final fresh Step 16/17 run passed the updated invariants. |
| `STEP17B-INFRA-HEARTBEAT-002` | Automated | **Resolved and passed.** Repeated stress reproduced the scheduler-sensitive attempt bound and showed that UTC clock movement could extend the retry window. Production now converts the absolute lease safety deadline to a monotonic retry budget. The test uses a controllable `TimeProvider`, proves exact 50 ms retry pacing and 16 bounded attempts, and passed 30 consecutive reruns before the final complete solution run. |
| `STEP17B-VERTICAL-CURRENT-003` | Contract + TestServer + SQL | **Passed.** Model, repository publication, relational catalog refresh, route-boundary, OpenAPI, and full live-local evidence prove current surfaces remain car-wash-only and future/inactive vertical rows do not leak. |
| `STEP17B-VERTICAL-SNAPSHOT-004` | Relational + chained HTTP | **Passed.** Reservation-service, Business and Customer persistence-invariant, migration/backfill, booking confirmation, callback, reconciliation, and full chained live-local evidence prove matching immutable `car_wash` snapshots. |
| `STEP17B-VERTICAL-VEHICLE-005` | Migration + relational + chained HTTP | **Passed.** Populated upgrade, downgrade restoration, relational create/reload/cascade, reservation creation, and chained live-local evidence prove vehicle extension compatibility and persistence. |
| `STEP17B-INFRA-FIXTURE-006` | Live-local + asset self-test | **Resolved and passed.** Expected-red was captured when the second run reached the category/assignment composite foreign key. Direct SQL catalog fixtures now create the required active primary `car_wash` assignments before vertical-scoped categories, and the Step 17 asset validator freezes both the assignment insert and stable vertical ID. The final full live-local run passed. |
| `STEP17B-VERTICAL-CUSTOMER-INSERT-007` | Automated persistence | **Resolved and passed.** Expected-red proved that a newly added Customer booking could initially use an unsupported snapshot even though later mutation was blocked. `SaveChanges_NewBookingWithNonCarWashVerticalSnapshot_RejectsWithoutPersistence` now proves fail-closed insertion and no persistence. |
| `STEP17B-VERTICAL-WORKORDER-INSERT-008` | Automated persistence | **Resolved and passed.** Expected-red proved that a standalone added work order could use an inconsistent vertical snapshot. `SaveChanges_AddedStandaloneWorkOrderWithMismatchedVerticalSnapshot_RejectsWithoutPersistence` now proves rejection, no work-order or vehicle-detail persistence, and no reservation mutation. |
| `STEP17B-VERTICAL-REGISTRATION-009` | Automated repository | **Passed.** `CreateForOwnerAsync_WhenCarWashVerticalIsIneligible_RejectsWithoutPersistence` now covers disabled registration, inactive `car_wash`, and a stable ID mapped to a non-`car_wash` code, with no company, user assignment, or company-vertical residue. |
| `STEP17B-VERTICAL-DOWNGRADE-010` | Automated SQL migration | **Passed.** `VerticalReadinessMigration_DowngradeRestoresLegacyVehicleColumnsAndData` upgrades populated legacy data, changes the extension row, downgrades, proves all five vehicle values are restored from the extension, verifies vertical tables/columns are removed, preserves unrelated rows, and checks exact migration history. |

Additional rows discovered by the independent audit are appended without
renumbering these IDs.

## 4. Execution phases

1. **Static reconciliation**
   - compare controller metadata, OpenAPI, boundaries, DTOs, validators, tests,
     scenario IDs, manifests, and result documents;
   - fail on duplicate/orphan scenario IDs, route drift, stale schema lists,
     missing validators, or unclassified fields.
2. **Expected-red**
   - preserve the stale Step 16 schema-verifier failure as
     `STEP17B-INFRA-SCHEMA-001`;
   - add the smallest tests for each independently audited gap and prove they
     fail for the intended reason before production behavior changes.
3. **Targeted fixes**
   - fix only defects or stale acceptance infrastructure exposed by the
     expected-red cases;
   - never change an expected result merely to fit existing behavior.
4. **Automated regression**
   - targeted new tests;
   - Business suite;
   - Customer suite;
   - complete solution;
   - clean/populated/down/up migration tests;
   - deterministic concurrency/replay/failpoint tests;
   - asset validators and harness self-tests;
   - Release build and static secret/PII/boundary checks.
5. **Fresh safe-local HTTP**
   - generate ignored cryptographic credentials and fixture references;
   - use unique disposable LocalDB databases and independent API processes;
   - run all Step 17 phases and all 1,294 inherited safe-local selections;
   - verify databases, restart/recovery, cleanup, and no surviving listener or
     local artifact.
6. **Independent review**
   - reconcile the audit findings and this ledger against current evidence;
   - publish exact pass/fail/deferred/external/not-applicable/missed counts.

## 5. Safety and exclusions

- Never use production hosts, databases, credentials, or Stripe live mode.
- Runtime passwords, JWT keys, HMAC keys, webhook secrets, tokens, fixture
  overlays, connection strings, and raw result JSON remain ignored and local.
- The deterministic fake payment gateway is explicitly enabled only for this
  local acceptance run.
- OAuth provider round-trips requiring external provider credentials may be
  contract/TestServer covered; any real-provider execution is listed as
  external and not passed.
- Infinite input combinations and scheduler interleavings are impossible to
  enumerate. Equivalence classes, boundaries, deterministic races, repeated
  runs, and explicit residual risk are required instead.

## Code Test Plan

- Add/update acceptance-infrastructure self-tests before changing stale
  verifier expectations.
- Add all behavior gaps found by the independent audit test-first.
- Run targeted, per-project, complete-solution, migration, harness, and Release
  gates described in section 4.

## HTTP Test Plan

- HTTP required: **Yes** - this is the complete two-host pre-deployment
  acceptance gate.
- HTTP required for `STEP17B-VERTICAL-CUSTOMER-INSERT-007` through
  `STEP17B-VERTICAL-DOWNGRADE-010`: **No** - these are EF persistence guards,
  repository eligibility branches, and migration mechanics. Current HTTP DTOs
  intentionally expose no vertical-selection input, so HTTP cannot directly
  construct these invalid internal states.
- Execution levels: contract, repository/SQL, TestServer, and disposable
  live-local HTTPS.
- Stable IDs: all retained Step 6-17 IDs plus appended `STEP17B-*` rows.

## HTTP Test Results

All automated and safe-local phases passed on 2026-08-26.

- Static Step 17 assets: **passed** - 118 new IDs, 58 live entries, 60
  deterministic/contract mappings, 1,295 inherited entries, and 1,294 safe
  inherited selections.
- Runtime-prerequisite self-test: **passed**.
- HTTP harness self-tests: **30 passed, 0 failed**.
- Final automated solution run: **1,789 passed, 0 failed** - 1,232 Customer
  and 557 Business tests.
- Lease-heartbeat deterministic class: **3 passed, 0 failed**; the formerly
  flaky repeated-renewal case then passed **30 consecutive reruns**, followed
  by a green complete solution run.
- Release build: **passed**, 0 warnings and 0 errors.
- Final fresh Step 17 live-local run:
  - **58 of 58** new live scenarios passed;
  - **1,294 of 1,294** safe inherited selections passed; the Step 9
    orchestration performed its documented additional support execution;
  - **790 of 790** Step 16 clean-schema lifecycle entries passed;
  - exact schema, ownership, principal, seed, migration-order, reset,
    outage, recovery, restart, concurrency, missing-table, production-mode,
    and cross-host invariants passed;
  - the deliberately unreachable Customer callback emitted the expected
    connection-refused support result while its enclosing outage/recovery
    scenario passed;
  - run-scoped databases and state were removed, and both Step 16 and Step 17
    reported no owned process leak.
- The first two stopped runs remain expected-red evidence for
  `STEP17B-INFRA-SCHEMA-001` and `STEP17B-INFRA-FIXTURE-006`; they are
  superseded as release evidence by the successful fresh third run.
- Real Stripe `STEP14-INTENT-REAL-STRIPE-057`: **not executed**; external test
  credentials are unavailable and the Stripe release gate remains blocked.
- Independent endpoint/field gap reconciliation: **complete**. Four concrete
  residual gaps were identified and added as
  `STEP17B-VERTICAL-CUSTOMER-INSERT-007` through
  `STEP17B-VERTICAL-DOWNGRADE-010`. Their focused gate passed 20 of 20 tests;
  the two expected-red insertion cases required the persistence-invariant
  fixes recorded above.
