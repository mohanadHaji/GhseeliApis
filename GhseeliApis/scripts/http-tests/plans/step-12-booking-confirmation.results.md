# Step 12 booking confirmation results

- Final execution: 2026-08-23T07:23:15.1099767Z to 2026-08-23T07:23:21.9846766Z
- Status: **done**
- Environment: isolated LocalDB databases and local HTTPS only
- Customer: `https://localhost:50692`
- Business: `https://localhost:50691`
- Manifest: `.\scripts\http-tests\plans\step-12-booking-confirmation.manifest.json`
- Raw result: gitignored `.\scripts\http-tests\artifacts\step-12-booking-confirmation.live.results.json`
- Secrets: process environment only; no credentials or signatures committed

## Results

- Reconciled live HTTP manifest: **64/64 passed**
- Focused Customer automated tests: **120/120 passed**
- Focused Business automated tests: **38/38 passed**
- HTTP harness self-tests: **18/18 passed**
- Solution build: passed with 0 warnings and 0 errors
- Exact SQL invariant verification: passed

The final run added exactly:

- 2 Customer bookings, 3 booking items, 2 selections, and 2 confirmation attempts
- 3 Business reservations, 3 work orders, 5 work-order items, and 4 selections
- 0 Payment rows
- 2 exact Customer/Business matches for order GUID, booking reference,
  reservation ID, work-order ID, total, duration, and status

The final databases were fresh isolated clones:
`GhseeliCustomer_Step12_20260823_102235` and
`GhseeliBusiness_Step12_20260823_102235`.

## Reconciled contract

- Customer route: `POST /api/v1/bookings/from-draft`
- Success: `200`
- Authentication: User JWT plus device token
- Required input: `X-Order-Guid`, positive `expectedVersion`, and
  `cancellationPolicyAcknowledged=true`
- Replay identity: authenticated owner plus order GUID
- No public `Idempotency-Key`, payment-method selection, Payment creation,
  Stripe call, Customer booking GET, or Business reservation GET contract
- Unknown money, identity, status, and payment fields are ignored; persisted and
  returned values remain server-derived
- A confirmed draft remains readable, while PUT and reprice return
  `409 checkout_draft_version_conflict`

## Commands

```powershell
dotnet build .\GhseeliApis.sln --verbosity minimal
dotnet test .\Ghseeli.CustomerApi.Tests\Ghseeli.CustomerApi.Tests.csproj --filter "FullyQualifiedName~BookingConfirmation|FullyQualifiedName~CustomerBookingRelational|FullyQualifiedName~BusinessApiClient|FullyQualifiedName~CheckoutDraftApiIntegration|FullyQualifiedName~CheckoutDraftService|FullyQualifiedName~CheckoutPricingService" --verbosity minimal
dotnet test .\Ghseeli.BusinessApi.Tests\Ghseeli.BusinessApi.Tests.csproj --filter "FullyQualifiedName~ReservationService|FullyQualifiedName~TimeZoneAvailabilityResolver|FullyQualifiedName~InternalServiceIdempotencyRelational" --verbosity minimal
powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Run-SelfTests.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Invoke-HttpTests.ps1 -ManifestPath .\scripts\http-tests\plans\step-12-booking-confirmation.manifest.json -BaseUrl https://localhost:50692 -ResultsPath .\scripts\http-tests\artifacts\step-12-booking-confirmation.live.results.json
powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Verify-Step12DatabaseInvariants.ps1 -CustomerDatabase GhseeliCustomer_Step12_20260823_102235 -BusinessDatabase GhseeliBusiness_Step12_20260823_102235 -ExpectedBookings 2 -ExpectedItems 3 -ExpectedSelections 2 -ExpectedBusinessReservations 3 -ExpectedBusinessItems 5 -ExpectedBusinessSelections 4
```

## Automated-only cases

- Confirmation claim versus draft mutation:
  `CustomerBookingRelationalIntegrationTests.ConfirmationClaim_BlocksConcurrentDraftMutationAndMakesItsWriteStale`
- Concurrent capacity reservation:
  `ReservationServiceTests.CreateAsync_WhenCapacityOneIsRequestedConcurrently_OnlyOneAtomicWorkOrderIsCreated`
- Multi-item rollback:
  `ReservationServiceTests.CreateAsync_WhenSecondItemIsRejected_PersistsNothing`
- Transactional authoritative validation and inactive override filtering:
  `ReservationServiceTests.CreateAsync_WhenInactiveClosureExists_UsesRecurringCapacity`,
  `ReservationServiceTests.CreateAsync_WhenInactiveCapacityOverrideExists_UsesRecurringCapacity`
- Mismatched/ambiguous accepted payload:
  `BookingConfirmationServiceTests.ConfirmAsync_WhenSuccessfulResponseHasMismatchedSelection_FailsClosedAndKeepsClaimForReplay`
- strict reordered/malformed multi-item Business response handling and claim
  recovery:
  `BookingConfirmationServiceTests.ConfirmAsync_WithReorderedMultiItemResponse_PersistsDisplayOrderAndReplays`,
  `BookingConfirmationServiceTests.ConfirmAsync_WhenRetryReceivesReorderedAcceptedResponse_RecoversClaimAndPersistsBooking`,
  and `BookingConfirmationServiceTests.ConfirmAsync_WithMalformedMultiItemResponse_FailsClosed`
- structured Business price conflict mapping and pricing invalidation:
  `BookingConfirmationServiceTests.ConfirmAsync_WhenBusinessRejectsPriceProof_InvalidatesPricingAndPreservesCode`
- Unavailable outcome and same-order retry:
  `BookingConfirmationServiceTests.ConfirmAsync_WhenBusinessOutcomeIsUnavailable_PersistsNothingAndAllowsSameKeyRetry`
- Internal same-key concurrency:
  `InternalServiceIdempotencyRelationalIntegrationTests.ValidateAppointment_WhenSameIdempotencyKeyAndBodyArriveConcurrently_ExecutesOnce`
- Internal retry after failed execution:
  `InternalServiceIdempotencyRelationalIntegrationTests.ValidateAppointment_WhenFirstExecutionThrows_AllowsSuccessfulRetryWithSameIdempotencyKey`
- Internal signature privacy:
  `InternalServiceRequestValidatorTests.ValidateAsync_WhenSignatureIsInvalid_DoesNotLogSecretSignatureOrBody`
- Malformed nested reservation payload rejection before validation or hashing:
  `ReservationServiceTests.CreateAsync_WhenItemsContainNull_RejectsBeforeValidationOrPersistence`,
  `ReservationServiceTests.CreateAsync_WhenSelectedAddonsIsNull_RejectsBeforeValidationOrHashing`,
  and `ReservationServiceTests.CreateAsync_WhenSelectedAddonsContainNull_RejectsBeforeValidationOrHashing`
- Stable HMAC HTTP 400 problem responses and idempotent replay for null item,
  null add-on collection, and null add-on entry:
  `InternalServiceIdempotencyRelationalIntegrationTests.CreateReservation_WhenNestedCollectionContentIsNull_ReturnsReplayableProblemInsteadOfSuccess`

There is no exact automated assertion covering all Customer booking log fields;
that remains a logging-privacy coverage gap. Injected post-commit connection loss
also lacks a dedicated failpoint test and remains an explicit ambiguous-outcome
gap.
