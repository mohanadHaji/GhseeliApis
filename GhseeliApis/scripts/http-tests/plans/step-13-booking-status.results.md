# Step 13 booking-status HTTP results

Execution date: 2026-08-23
Environment: fresh isolated SQL Server LocalDB databases; Customer
`https://localhost:50690`; Business `https://localhost:50691`; HTTPS development
certificate. Reverse-direction HMAC credentials and the short-lived fixture JWT
were loaded from user secrets into process environment only. No credential,
token, raw body, database name, or personal data is committed.

## Final shipping-gate result

- Clean Release solution build: **passed, 0 warnings / 0 errors**.
- Customer tests: **949/949 passed**.
- Business tests: **332/332 passed**.
- Combined tests: **1281/1281 passed**.
- Business startup/migration tests: **7/7 passed** against an isolated LocalDB.
- Customer renewable-lease option validation tests: **10/10 passed**; both live
  hosts started with valid configured lease bounds and served protected routes.
- Live-safe invalid computed-interval startup checks: **2/2 passed**; intervals
  below the practical minimum and at the lease safety deadline both terminated
  startup with the expected options-validation error.
- Lease lifecycle focused tests: **21/21 passed**, including relational
  TestServer client cancellation after domain commit, replay with one execution,
  transient renewal recovery, repeated failure lease loss, changed ownership,
  paced retries, ambiguous cancellation preservation, and stale completion
  rejection.
- HTTP harness self-tests: **18/18 passed**.
- Corrected signed two-host manifest: **90/90 passed**.
- Customer and Business databases: **zero pending migrations**; repeat
  `database update` checks reported already up to date, and both model snapshots
  reported no pending model changes.
- Exact database invariant verifier: **passed**.
- Completion gate: **passed**.
- Sanitized raw evidence:
  `scripts/http-tests/artifacts/step-13-lifecycle-final.json` (ignored).

The final run used databases created from clean source clones immediately before
execution and applied every Customer and Business migration. Both hosts were
rebuilt from the latest worktree and restarted before the run. The superseded
71/78/85-case and prior 90-case evidence are not shipping evidence.

## Live contract coverage

The preserved 85 cases plus five recovery-hardening cases (**90 total**) cover
the implemented routes:

- `POST /api/v1/internal/bookings/status`
- `GET /api/v1/internal/bookings/{reference}`
- `POST /api/v1/internal/bookings/{reference}/reconcile`
- `GET /api/v1/internal/reservations/{reference}`
- `POST /api/v1/internal/reservations`
- `POST /api/v1/business/work-orders/{id}/transitions`
- `POST /api/v1/business/admin/booking-status-outbox/{eventId}/requeue`

Verified live behavior includes:

- Customer and Business Swagger/startup availability over HTTPS.
- Known wrong verb 405 and signed unknown route 404 without state consumption.
- Customer JWT rejection; callback-only operation forbid; typed Business
  anonymous 401 `business_authentication_required`.
- Missing, empty, malformed, unknown, stale, future, wrong-secret,
  wrong-direction, active/next-secret, nonce, signature, method/path/body
  tamper, replay, and canonical HMAC behavior.
- Missing/empty/malformed transport idempotency; replay and conflict semantics.
- Transport identity includes method, normalized path/query, bounded normalized
  content type, and exact body: identical `text/plain` same-key requests replay
  deterministic 415; corrected content type with that key conflicts 409; a new
  key with corrected content type reaches normal 200 processing. Malformed and
  overlong values use bounded class/length/SHA-256 identities, so exact invalid
  values replay while distinct values conflict without raw header persistence
  or logging.
- Missing and malformed Business name-identifier claims return typed, no-store
  401 responses without mutation for owner transition and admin requeue.
- A dead-letter requeue increments durable delivery generation from 0 to 1,
  returns `Requeued`, and records a complete audit/history marker. Repeating
  the same admin request returns `AlreadyRequeued` at generation 1 without a
  second increment or history row. The generation-1 delivery uses its new
  transport identity and is independently dead-lettered after the intentional
  fixture callback rejection.
- Content type, empty/null/malformed JSON, fixed-length and chunked 64 KiB
  boundary enforcement, version/event/status/sequence/time validation.
- Callback application, same-event replay, event/idempotency conflicts,
  sequence gap, unknown booking, immutable reservation/work-order references.
- Initial `Pending` sequence 0; all applicable transitions; repeated transition
  conflict; live ordered outbox delivery.
- Customer/Business reads, unknown references, reconciliation, reconcile-then-
  real equal-sequence callback persistence, equal-sequence conflict, and
  non-poisoning.
- Arabic/Hebrew localization, precedence, invalid explicit language, correlation
  propagation, typed Problem Details, and `no-store`.
- Capacity: `Pending`, legacy `Reserved`, `Confirmed`, and `InProgress` each
  consume all four places and reject the fifth request with 409; `Completed`,
  `Cancelled`, and `NoShow` release capacity and accept a new Pending request.

## Exact database invariants

- Customer bookings: **14**.
- Customer inbox rows: **10**.
- Fixtures 1, 2, 3, 5, and 6: `Confirmed`, sequence 1, one inbox row each.
- Fixture 4: `Confirmed`, sequence 2, one inbox row.
- Fixtures 7 and 8: `Confirmed`, sequence 1, two inbox identities each, with
  exactly one persisted real callback event identity.
- Payments: **0 source / 0 isolated**.
- Booking items: **3 source / 3 isolated**.
- Booking selections: **2 source / 2 isolated**.
- Customer legacy `Reserved`: **0**.
- Customer transport idempotency: **28 completed / 0 incomplete / 0 still
  owned or leased**; successful completion/release cleared every owner token
  and lease expiry.
- Cross-database references: **12/12 matched**.
- Business reservations: **3 source / 46 isolated**.
- Business work orders: **3 source / 18 isolated**.
- Business transition fixture: reservation/work order `Confirmed`, sequence 1.
- Business outbox: **2 total / 1 delivered / 1 dead-letter**.
- Recovery outbox: `DeadLetter`, generation **1**, attempt count **1**,
  `HTTP_400`, exactly **1** generation-history row, with complete admin,
  request-id, and timestamp audit fields.
- Business legacy `Reserved`: **4**, all deliberate capacity fixtures.
- Active capacity slots: exactly **4** rows each; no rejected request persisted.
- Terminal capacity slots: exactly **5** rows each—four terminal fixtures and
  one newly accepted `Pending` reservation/work order.

## Automated-only deterministic coverage

The sequential live harness does not claim controlled concurrency, transaction
ambiguity, lease-window timing, cleanup clock advancement, worker disablement,
or injected dispatcher/database failures. These requirements passed under the
following exact automated tests.

### Transport cleanup, limits, and concurrency

- `CustomerInternalServiceOptionsValidatorTests.Validate_WhenLeaseCannotSupportHeartbeat_Fails`
- `CustomerInternalServiceOptionsValidatorTests.Validate_WhenLeaseAndRenewalFractionAreSafe_Succeeds`
- `CustomerInternalServiceOptionsValidatorTests.Validate_WhenRenewalFractionIsNotFinite_Fails`
- `CustomerInternalServiceOptionsValidatorTests.Validate_WhenComputedRenewalIntervalIsBelowPracticalMinimum_Fails`
- `CustomerInternalServiceOptionsValidatorTests.Validate_WhenComputedRenewalIntervalRoundsToZero_Fails`
- `CustomerInternalServiceOptionsValidatorTests.Validate_WhenRenewalIntervalReachesLeaseSafetyDeadline_Fails`
- `CustomerInternalServiceOptionsValidatorTests.Validate_WhenComputedRenewalIntervalIsExactlyMinimum_Succeeds`
- `CustomerInternalIdempotencyLeaseRelationalTests.LongEndpoint_RenewsWhileDuplicateAndCleanupRun_ExecutesOnce`
- `CustomerInternalIdempotencyLeaseRelationalTests.RenewedLease_SurvivesMultipleLeasePeriodsDuplicateAndCleanup`
- `CustomerInternalIdempotencyLeaseRelationalTests.ForcedReclaim_RejectsStaleCompletionAndOnlyNewOwnerCanComplete`
- `CustomerInternalIdempotencyLeaseRelationalTests.ExpiredOrphan_ConcurrentReclaimHasExactlyOneNewOwner`
- `CustomerInternalIdempotencyLeaseRelationalTests.CrashWithoutHeartbeat_EventuallyReclaimsOnlyOnce`
- `CustomerInternalIdempotencyLeaseRelationalTests.EndpointCancellationAfterExecutionBegins_PreservesOwnedClaim`
- `CustomerInternalIdempotencyLeaseRelationalTests.CompletionAfterLeaseExpiry_IsRejectedAsStale`
- `CustomerInternalLeaseHeartbeatTests.TransientRenewalFailure_RetriesThenRecoversBeforeDeadline`
- `CustomerInternalLeaseHeartbeatTests.RepeatedRenewalFailures_CancelLeaseTokenWithoutBusyLoop`
- `CustomerInternalLeaseHeartbeatTests.OwnershipChanged_StopsImmediatelyAndSignalsLeaseLoss`
- `BookingStatusCallbackSecurityIntegrationTests.Callback_ClientCancellationAfterDomainCommit_CompletesAndRetryReplaysOnce`
- `BookingStatusCallbackSecurityIntegrationTests.Callback_ExpiredNonceCanBeReusedAndExpiredRowsAreCleaned`
- `BookingStatusCallbackSecurityIntegrationTests.Callback_ExpiredInProgressTransportRecord_IsRecoverable`
- `BookingStatusCallbackSecurityIntegrationTests.Callback_RelationalConcurrentIdenticalTransportRequests_ApplyOnce`
- `BookingStatusCallbackSecurityIntegrationTests.Callback_RelationalConcurrentConflictingTransportRequests_HaveOneWinner`
- `BookingStatusCallbackSecurityIntegrationTests.Callback_RelationalConcurrentNonceReuse_AllowsOnlyOneRequest`
- `BookingStatusCallbackSecurityIntegrationTests.Callback_ContentTypeParticipatesInTransportIdempotencyIdentity`
- `BookingStatusCallbackSecurityIntegrationTests.Callback_ExactlySixtyFourKilobytesOfValidJson_IsAccepted`
- `BookingStatusCallbackSecurityIntegrationTests.Callback_BodyOverSixtyFourKilobytes_IsRejectedBeforeBinding`
- `BookingStatusCallbackSecurityIntegrationTests.ReadBodyAsync_ChunkedBodyOverLimit_StopsAfterMaximumPlusOne`
- `BookingStatusCallbackSecurityIntegrationTests.Callback_MalformedContentType_PreservesBoundedExactTransportIdentity`
- `BookingStatusCallbackSecurityIntegrationTests.Callback_DifferentOversizedContentTypes_ConflictWithoutPersistingRawHeader`
- `CustomerInternalIdempotencyCleanupServiceTests.CleanupExpiredAsync_DeletesOnlyBoundedExpiredCompletedOrAbandonedRecords`

### Transition authorization, commit verification, and concurrency

- `BookingStatusServiceTests.TransitionAsync_LegacyReservedStatus_IsNormalizedToPending`
- `BookingStatusServiceTests.TransitionAsync_AssignmentScopeAndActivity_AuthorizesWithoutEnumeration`
- `BookingStatusServiceTests.TransitionAsync_AmbiguousSaveAfterPersistence_ReturnsCommittedOutboxEvent`
- `BookingStatusServiceTests.TransitionAsync_SaveFailure_DoesNotAcknowledgePartialTransition`
- `BookingStatusServiceTests.TransitionAsync_SaveFailure_DoesNotAcknowledgeUnrelatedMatchingOutbox`
- `BookingStatusTransitionRelationalConcurrencyTests.ConcurrentTransitionWrites_SecondSaveThrowsDbUpdateConcurrencyException`
- `WorkOrdersControllerTests.Transition_WhenPersistenceDetectsConcurrency_ReturnsStableConflictProblem`
- `WorkOrdersControllerTests.Transition_WhenNameIdentifierIsMissingOrMalformed_ReturnsTypedUnauthorizedWithoutMutation`
- `BookingStatusOutboxAdminControllerTests.Requeue_WhenNameIdentifierIsMissingOrMalformed_ReturnsTypedUnauthorizedWithoutMutation`
- `BookingStatusOutboxAdminControllerTests.Requeue_MissingRequestIdempotencyKey_ReturnsBadRequestWithoutMutation`
- `BookingStatusDeadLetterServiceTests.RequeueAsync_SameRequest_AuditsNoOpWithoutChangingPersistentAudit`
- `BookingStatusDeadLetterServiceTests.RequeueAsync_SecondDeadLetterWithNewRequest_IncrementsGenerationAgain`
- `WorkOrderTransitionAuthorizationIntegrationTests.Transition_WhenJwtIsExpired_ReturnsAuthenticationRequiredWithoutMutation`

### Capacity and atomic reservation creation

- `ReservationServiceTests.CreateAsync_LegacyReservedRecord_StillConsumesCapacity`
- `ReservationServiceTests.CreateAsync_ActiveReservationStatus_ConsumesCapacity`
- `ReservationServiceTests.CreateAsync_TerminalReservationStatus_ReleasesCapacity`
- `ReservationServiceTests.CreateAsync_WhenCapacityOneIsRequestedConcurrently_OnlyOneAtomicWorkOrderIsCreated`

### Outbox lease, ordering, retry, dead-letter, and requeue

- `BookingStatusOutboxDispatcherTests.DeliverNextAsync_TransientFailure_RetriesWithStableIdentity`
- `BookingStatusOutboxDispatcherTests.DeliverNextAsync_RequeuedGeneration_UsesNewStableIdentityAcrossRetries`
- `BookingStatusOutboxDispatcherTests.DeliverNextAsync_CachedGenerationZero4xx_RequeueGenerationOneCanSucceed`
- `BookingStatusOutboxDispatcherTests.DeliverNextAsync_Permanent4xx_DeadLettersImmediately`
- `BookingStatusOutboxDispatcherTests.DeliverNextAsync_MaxAttempts_DeadLettersTransientFailure`
- `BookingStatusOutboxDispatcherTests.DeliverNextAsync_HeadOfLineBackoff_BlocksLaterReservationEvent`
- `BookingStatusOutboxDispatcherTests.DeliverNextAsync_DeadLetterBlocksLaterUntilRequeuedThenDeliversInSequence`
- `BookingStatusOutboxDispatcherTests.DeliverNextAsync_ActiveLease_BlocksSecondDispatcher`
- `BookingStatusOutboxDispatcherTests.DeliverNextAsync_ExpiredLease_IsRecovered`
- `BookingStatusOutboxDispatcherTests.DeliverNextAsync_LostLease_DoesNotOverwriteNewOwnerState`
- `BookingStatusDeadLetterRelationalConcurrencyTests.RequeueAsync_WorkerLeasesThenDelivers_RepeatsRemainNoOpAndUnrelatedLeaseConflicts`
- `BookingStatusDeadLetterRelationalConcurrencyTests.RequeueAsync_ConcurrentSameRequest_IncrementsGenerationExactlyOnce`
- `BookingStatusOutboxAdminApiIntegrationTests.Requeue_AdminDeadLetter_RequeuesAndRepeatIsIdempotent`
- `BookingStatusOutboxAdminApiIntegrationTests.Requeue_AdminLeasedEvent_ReturnsConflictWithoutMutation`

### Reconciliation, matrix, and startup

- `CustomerBookingRelationalIntegrationTests.StatusInbox_SimultaneousReconciliationAndRealCallback_RecordsRealIdentityOnce`
- `BookingStatusInboxServiceTests.ApplyAsync_RealCallbackAfterSyntheticReconciliation_IsRecordedAsNoOp`
- `BookingStatusInboxServiceTests.ApplyAsync_EqualSequenceAfterReconciliationWithConflict_IsRejected`
- `BookingStatusInboxServiceTests.ApplyAsync_TransitionMatrix_EnforcesAllThirtySixEdges`
- `BookingStatusServiceTests.TransitionMatrix_OnlyAllowsForwardDomainTransitions`
- `BusinessApiStartupTests.Startup_WhenCustomerCallbackBaseUrlIsMissingOutsideTesting_ThrowsOptionsValidationException`
- `BookingStatusOutboxWorkerTests.Worker_WhenExplicitlyDisabledForTesting_DoesNotResolveDispatcher`

All must-ship live cases passed; no assertion was weakened and no product defect
was masked.
