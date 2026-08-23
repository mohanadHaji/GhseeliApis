# Step 12 HTTP Test Plan — Priced Draft Confirmation

Status: executed against the finalized Step 12 contract on 2026-08-23.

## Implemented contract

Customer confirmation is:

```http
POST /api/v1/bookings/from-draft?language={ar|he}
Authorization: Bearer <User JWT>
X-Device-Token: <device token>
X-Order-Guid: <draft order GUID>
Content-Type: application/json

{
  "expectedVersion": 2,
  "cancellationPolicyAcknowledged": true
}
```

Success is `200`, not `201`. The response contains Customer booking, Business
reservation/work-order, order, status, accepted slot, localized provider/branch,
currency, total, duration, and item references. It has `Cache-Control: no-store`.

The public route has no payment-method or client `Idempotency-Key` contract.
Unknown JSON fields are ignored. Replay identity is authenticated ownership plus
the order GUID, persisted in `BookingConfirmationAttempts`; Customer derives the
downstream Business idempotency key. Step 12 creates no Payment or Stripe object.

Business exposes HMAC-only `POST /api/v1/internal/reservations`. No Customer
booking GET or Business reservation GET was added in Step 12.

## Executable coverage

Manifest:
`scripts/http-tests/plans/step-12-booking-confirmation.manifest.json`

Fixture:
`scripts/http-tests/plans/step-12-malformed.request.txt`

The 64 independently asserted live scenarios cover:

- Customer and Business Swagger routes.
- missing, empty, malformed, invalid, and expired JWTs; missing, empty,
  malformed, unknown, and rotated device tokens.
- ownership/not-found indistinguishability.
- explicit-language precedence, `Accept-Language`, default language, malformed
  language values, and safe correlation replacement/propagation.
- content type, malformed JSON, and 64 KiB boundary expectations.
- missing, empty, malformed, and empty order GUIDs; missing, zero, negative,
  and stale versions; missing/false policy acknowledgement; and null bodies.
- unpriced/reprice-required and stale-version rejection.
- successful single- and multi-item confirmation.
- every returned public/cross-system reference; exact localized names, item and
  aggregate totals, currency, duration, and slot timestamps; same-order replay
  with the original and stale positive versions; ignored money, identity,
  status, and payment fields; and no Payment/Stripe output or rows.
- post-confirmation draft terminal-state expectations.
- Business stale-catalog, invalid-contract, and unavailable-slot mappings.
- direct Business multi-item creation, canonical same-order replay under a new
  transport idempotency key with reordered equivalent semantic input, and
  rejection when that order is reused with changed semantic content.
- HMAC-signed rejection of a null Business reservation item with a stable
  `application/problem+json` 400 response.
- `no-store` expectations on success and errors.
- executable SQL row counts, uniqueness, cross-reference equality, and zero
  Payment rows via `scripts/http-tests/Verify-Step12DatabaseInvariants.ps1`.

Expired drafts require a controlled clock or direct fixture mutation and remain
automated-only. Injected Business timeout/unavailable, post-accept ambiguity,
malformed upstream responses, deterministic concurrency, and log capture also
remain automated-only.

## Automated-only cross-references

- Immutable snapshots and replay:
  `BookingConfirmationServiceTests.ConfirmAsync_WithPricedOwnedDraft_PersistsImmutableSnapshotsAndReplays`.
- Ownership without a Business call:
  `BookingConfirmationServiceTests.ConfirmAsync_FromDifferentDevice_DoesNotCallBusiness`.
- requires-reprice, unpriced, and stale-version states:
  `BookingConfirmationServiceTests.ConfirmAsync_WithInvalidDraftState_FailsClosed`
  with its three named `InlineData` cases.
- injected Business unavailable/retry:
  `BookingConfirmationServiceTests.ConfirmAsync_WhenBusinessOutcomeIsUnavailable_PersistsNothingAndAllowsSameKeyRetry`.
- Customer uniqueness:
  `CustomerBookingRelationalIntegrationTests.CustomerBookings_WithDuplicateOrderGuid_SaveFailsAtDatabaseBoundary`.
- Business replay:
  `ReservationServiceTests.CreateAsync_WithMatchingProof_PersistsOneReservationAndReplays`
  and live scenarios `STEP12-BUSINESS-CANONICAL-CREATE-037` /
  `STEP12-BUSINESS-CANONICAL-REPLAY-038`.
- authoritative transactional validation and inactive override filtering:
  `ReservationServiceTests.CreateAsync_WhenInactiveClosureExists_UsesRecurringCapacity`,
  `ReservationServiceTests.CreateAsync_WhenInactiveCapacityOverrideExists_UsesRecurringCapacity`,
  and `TimeZoneAvailabilityResolverTests.Resolve_WhenInactiveOverrideExists_UsesRecurringSchedule`.
- authoritative price rejection:
  `ReservationServiceTests.CreateAsync_WhenAuthoritativePriceChanged_RejectsWithoutPersistence`.
- capacity conflict:
  `ReservationServiceTests.CreateAsync_WhenCapacityIsConsumed_RejectsWithoutDuplicateWorkOrder`.
- changed Business request for one order:
  `ReservationServiceTests.CreateAsync_WhenOrderGuidIsReusedWithDifferentContent_Rejects`
  and live scenario `STEP12-BUSINESS-ORDER-CONFLICT-039`.
- atomic multi-item persistence:
  `ReservationServiceTests.CreateAsync_WithMultipleItems_PersistsCanonicalItemAndSelectionOrderAtomically`
  and `ReservationServiceTests.CreateAsync_WhenSecondItemIsRejected_PersistsNothing`.
- ambiguous commit replay is implemented by reloading the committed order after
  an unexpected persistence exception, but still lacks a dedicated failpoint
  test and remains an automated-only coverage gap.
- HMAC/idempotency persistence:
  `InternalServiceIdempotencyRelationalIntegrationTests.CreateReservation_WhenSameOrderAndIdempotencyKeyAreReplayed_ExecutesOnce`.
- deterministic same-key concurrency:
  `InternalServiceIdempotencyRelationalIntegrationTests.ValidateAppointment_WhenSameIdempotencyKeyAndBodyArriveConcurrently_ExecutesOnce`.
- failed first execution retry:
  `InternalServiceIdempotencyRelationalIntegrationTests.ValidateAppointment_WhenFirstExecutionThrows_AllowsSuccessfulRetryWithSameIdempotencyKey`.
- null reservation items, null `SelectedAddons`, and null add-on entries:
  `ReservationServiceTests.CreateAsync_WhenItemsContainNull_RejectsBeforeValidationOrPersistence`,
  `ReservationServiceTests.CreateAsync_WhenSelectedAddonsIsNull_RejectsBeforeValidationOrHashing`,
  and `ReservationServiceTests.CreateAsync_WhenSelectedAddonsContainNull_RejectsBeforeValidationOrHashing`.
- stable HTTP problem responses and idempotent 400 replay for all three malformed
  nested shapes:
  `InternalServiceIdempotencyRelationalIntegrationTests.CreateReservation_WhenNestedCollectionContentIsNull_ReturnsReplayableProblemInsteadOfSuccess`.

Injected timeout, concurrent confirmation, and ambiguous-outcome cases must not
be simulated by weakening or replacing the live Business API.

## Result

Final live local HTTPS: **64 total, 64 passed, 0 failed**.

The SQL verifier accepts independent expected Business counts for the additional
direct reservation. Exact evidence and remaining automated-only gaps are
recorded in the results file.
