param(
    [Parameter(Mandatory = $true)]
    [string]$CustomerDatabase,
    [Parameter(Mandatory = $true)]
    [string]$SourceCustomerDatabase,
    [Parameter(Mandatory = $true)]
    [string]$VariablesPath,
    [switch]$StripeExecuted,
    [switch]$SignedWebhooksExecuted
)

$ErrorActionPreference = 'Stop'
$server = '(localdb)\MSSQLLocalDB'
foreach ($name in @($CustomerDatabase, $SourceCustomerDatabase)) {
    if ($name -notmatch '^[A-Za-z0-9_]+$') { throw 'Unsafe database name.' }
}
if ([string]::Equals(
    $CustomerDatabase,
    $SourceCustomerDatabase,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source and target customer database names must be different.'
}

$variables = Get-Content -LiteralPath $VariablesPath -Raw | ConvertFrom-Json
if ([string]$variables.immutableBookingSnapshotHash -notmatch '^[0-9A-F]{64}$') {
    throw 'Fixture booking snapshot hash is invalid.'
}

function Query([string]$database, [string]$sql) {
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$server;Database=$database;Integrated Security=true;TrustServerCertificate=true")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $sql
        $adapter = [Data.SqlClient.SqlDataAdapter]::new($command)
        $table = [Data.DataTable]::new()
        [void]$adapter.Fill($table)
        return ,$table
    }
    finally { $connection.Dispose() }
}

function Scalar([string]$database, [string]$sql) {
    return (Query $database $sql).Rows[0].Value
}

function Get-LegacyPaymentHash([string]$database) {
    return [string](Scalar $database @"
SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varbinary(max), (
    SELECT * FROM dbo.Payments ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES
))), 2) Value
"@)
}

$failures = [Collections.Generic.List[string]]::new()
function Fail([string]$message) {
    $failures.Add($message)
}
function Expect-Count([string]$name, [int]$actual, [int]$expected) {
    if ($actual -ne $expected) {
        Fail "$name`: expected exactly $expected, found $actual."
    }
}

$sourceLegacyCount = [int](Scalar $SourceCustomerDatabase `
    'SELECT COUNT(*) FROM dbo.Payments')
$targetLegacyCount = [int](Scalar $CustomerDatabase `
    'SELECT COUNT(*) FROM dbo.Payments')
Expect-Count 'Legacy Payment row count' $targetLegacyCount $sourceLegacyCount
$sourceLegacyHash = Get-LegacyPaymentHash $SourceCustomerDatabase
$targetLegacyHash = Get-LegacyPaymentHash $CustomerDatabase
if ($sourceLegacyHash -ne $targetLegacyHash) {
    Fail 'Legacy Payment row content differs from the source database.'
}

$summary = Query $CustomerDatabase @"
SELECT
 (SELECT COUNT(*) FROM CustomerPayments) CustomerPayments,
 (SELECT COUNT(*) FROM CustomerPaymentIdempotencyRecords) IdempotencyRecords,
 (SELECT COUNT(*) FROM StripeWebhookEvents) WebhookEvents,
 (SELECT COUNT(*) FROM CustomerPayments
  WHERE IntentLeaseOwnerToken IS NOT NULL OR IntentLeaseExpiresAtUtc IS NOT NULL) ActiveLeases,
 (SELECT COUNT(*) FROM CustomerPayments p
  JOIN CustomerBookings b ON b.Id=p.CustomerBookingId
  WHERE p.Amount<>b.GrandTotal OR p.Currency<>b.Currency
     OR p.MinorAmount<>CONVERT(bigint,b.GrandTotal*100)) MoneyMismatches,
 (SELECT COUNT(*) FROM (
    SELECT CustomerBookingId FROM CustomerPayments
    GROUP BY CustomerBookingId HAVING COUNT(*)>1) d) DuplicateBookingPayments,
 (SELECT COUNT(*) FROM (
    SELECT PaymentIntentId FROM CustomerPayments WHERE PaymentIntentId IS NOT NULL
    GROUP BY PaymentIntentId HAVING COUNT(*)>1) d) DuplicateIntentIds,
 (SELECT COUNT(*) FROM (
    SELECT StripeIdempotencyKey FROM CustomerPayments
    GROUP BY StripeIdempotencyKey HAVING COUNT(*)>1) d) DuplicateProviderKeys,
 (SELECT COUNT(*) FROM (
    SELECT UserId,OwnerDeviceId,IdempotencyKey
    FROM CustomerPaymentIdempotencyRecords
    GROUP BY UserId,OwnerDeviceId,IdempotencyKey HAVING COUNT(*)>1) d)
    DuplicateScopedIdempotency,
 (SELECT COUNT(*) FROM CustomerPaymentIdempotencyRecords i
  JOIN CustomerPayments p ON p.Id=i.CustomerPaymentId
  WHERE i.UserId<>p.UserId OR i.OwnerDeviceId<>p.OwnerDeviceId
     OR i.RequestHash<>p.RequestHash) DeviceIdempotencyMismatches,
 (SELECT COUNT(*) FROM CustomerPayments p
  JOIN CustomerBookings b ON b.Id=p.CustomerBookingId
  WHERE (p.Status=0 AND (b.IsPaid<>0 OR b.PaymentState<>'Pending'))
     OR (p.Status=1 AND (b.IsPaid<>1 OR b.PaymentState<>'Completed'))
     OR (p.Status=2 AND (b.IsPaid<>0 OR b.PaymentState<>'Failed'))
     OR (p.Status=3 AND (b.IsPaid<>0 OR b.PaymentState<>'Refunded'))
     OR p.Status NOT IN (0,1,2,3)) PaidStateMismatches,
 (SELECT COUNT(*) FROM StripeWebhookEvents
  WHERE State NOT IN ('Processing','Completed','Quarantined','Deferred')
     OR BodyHash NOT LIKE REPLICATE('[0-9A-F]',64)
     OR LEN(EventId)=0 OR LEN(EventType)=0) InvalidWebhookReceipts
"@
$row = $summary.Rows[0]
foreach ($check in @{
    ActiveLeases = 'An incomplete or stale creation lease remained'
    MoneyMismatches = 'Payment money differs from immutable booking money'
    DuplicateBookingPayments = 'More than one logical payment exists for a booking'
    DuplicateIntentIds = 'Provider intent identity is not unique'
    DuplicateProviderKeys = 'Provider idempotency identity is not unique'
    DuplicateScopedIdempotency = 'Scoped idempotency identity is not unique'
    DeviceIdempotencyMismatches = 'Idempotency ownership/hash differs from its payment'
    PaidStateMismatches = 'Payment and booking state mapping is not exact'
    InvalidWebhookReceipts = 'Webhook receipt identity/state is invalid'
}.GetEnumerator()) {
    if ([int]$row.($check.Key) -ne 0) {
        Fail "$($check.Value): found $($row.($check.Key))."
    }
}

$fixturePayments = @($variables.expectedPaymentFixtures)
if ($fixturePayments.Count -ne 11) {
    Fail "Variables must identify exactly 11 payment fixtures; found $($fixturePayments.Count)."
}
$actualFixturePayments = Query $CustomerDatabase @"
SELECT p.Id,p.CustomerBookingId,p.Status,p.ProviderStatus,p.IdempotencyKey,
       p.RequestHash,p.PaymentIntentId,b.PublicReference,b.IsPaid,b.PaymentState
FROM CustomerPayments p
JOIN CustomerBookings b ON b.Id=p.CustomerBookingId
"@
foreach ($expected in $fixturePayments) {
    $matches = @($actualFixturePayments.Rows | Where-Object {
        [string]$_.Id -eq [string]$expected.id
    })
    if ($matches.Count -ne 1) {
        Fail "Expected payment fixture '$($expected.id)' exactly once; found $($matches.Count)."
        continue
    }
    $actual = $matches[0]
    $expectedStatus = if ($SignedWebhooksExecuted) {
        [string]$expected.webhookFinalStatus
    } else { [string]$expected.status }
    $expectedProviderStatus = if ($SignedWebhooksExecuted) {
        [string]$expected.webhookFinalProviderStatus
    } else { [string]$expected.providerStatus }
    $expectedIsPaid = if ($SignedWebhooksExecuted) {
        [bool]$expected.webhookFinalIsPaid
    } else { [bool]$expected.isPaid }
    $expectedPaymentState = if ($SignedWebhooksExecuted) {
        [string]$expected.webhookFinalPaymentState
    } else { [string]$expected.paymentState }
    foreach ($comparison in @{
        CustomerBookingId = [string]$expected.bookingId
        PublicReference = [string]$expected.bookingReference
        Status = $expectedStatus
        ProviderStatus = $expectedProviderStatus
        IdempotencyKey = [string]$expected.idempotencyKey
        RequestHash = [string]$expected.requestHash
        PaymentIntentId = [string]$expected.paymentIntentId
        PaymentState = $expectedPaymentState
    }.GetEnumerator()) {
        if ([string]$actual.($comparison.Key) -cne $comparison.Value) {
            Fail "Payment fixture '$($expected.id)' has unexpected $($comparison.Key)."
        }
    }
    if ([bool]$actual.IsPaid -ne $expectedIsPaid) {
        Fail "Payment fixture '$($expected.id)' has unexpected IsPaid."
    }
    $idempotencyRows = @((
        Query $CustomerDatabase @"
SELECT CustomerPaymentId,IdempotencyKey,RequestHash
FROM CustomerPaymentIdempotencyRecords
"@
    ).Rows | Where-Object {
        [string]$_.CustomerPaymentId -eq [string]$expected.id -and
        [string]$_.IdempotencyKey -ceq [string]$expected.idempotencyKey -and
        [string]$_.RequestHash -ceq [string]$expected.requestHash
    })
    Expect-Count "Payment fixture '$($expected.id)' idempotency identity" `
        $idempotencyRows.Count $(if ([bool]$expected.hasIdempotency) { 1 } else { 0 })
}

$expectedBookingIds = @($variables.expectedBookingFixtureIds)
if ($expectedBookingIds.Count -ne 23 -or
    @($expectedBookingIds | Sort-Object -Unique).Count -ne 23) {
    Fail 'Variables must identify exactly 23 unique booking fixtures.'
}
else {
    $actualBookingIds = @((Query $CustomerDatabase `
        'SELECT Id FROM CustomerBookings').Rows | ForEach-Object {
        ([guid]$_.Id).ToString('D')
    })
    Expect-Count 'Expected booking fixture identities' `
        (@($actualBookingIds | Where-Object { $_ -in $expectedBookingIds }).Count) 23
}

$stripeEvidence = Query $CustomerDatabase @"
SELECT p.Id
FROM CustomerPayments p
JOIN CustomerBookings b ON b.Id=p.CustomerBookingId
WHERE b.PublicReference='$($variables.secondPayableBookingId)'
  AND p.IdempotencyKey='stripe-$($variables.runStamp)'
  AND p.PaymentIntentId LIKE 'pi_%'
  AND p.ClientSecret IS NOT NULL
  AND p.ProviderPublishableKey LIKE 'pk_test_%'
"@
$stripeEvidenceCount = $stripeEvidence.Rows.Count
if ($stripeEvidenceCount -gt 1) {
    Fail "Stripe execution evidence is ambiguous: found $stripeEvidenceCount matching payments."
}
$stripeWasExecuted = $stripeEvidenceCount -eq 1
if ($stripeWasExecuted) {
    $stripePaymentId = [string]$stripeEvidence.Rows[0].Id
    Expect-Count 'Stripe payment idempotency identity' `
        ([int](Scalar $CustomerDatabase @"
SELECT COUNT(*)
FROM CustomerPaymentIdempotencyRecords i
JOIN CustomerPayments p ON p.Id=i.CustomerPaymentId
WHERE p.Id='$stripePaymentId'
  AND i.IdempotencyKey=p.IdempotencyKey
  AND i.RequestHash=p.RequestHash
"@)) 1
}
$expectedPaymentTotal =
    [int]$variables.baselineCustomerPaymentCount + 11 + $stripeEvidenceCount
$seededNewKeyEvidenceCount = 0
if ($null -ne $variables.existingPaymentId) {
    $seededNewKeyEvidence = Query $CustomerDatabase @"
SELECT i.Id
FROM CustomerPaymentIdempotencyRecords i
JOIN CustomerPayments p ON p.Id=i.CustomerPaymentId
WHERE p.Id='$($variables.existingPaymentId)'
  AND i.IdempotencyKey='new-key-$($variables.runStamp)'
  AND i.RequestHash=p.RequestHash
"@
    $seededNewKeyEvidenceCount = $seededNewKeyEvidence.Rows.Count
    if ($seededNewKeyEvidenceCount -gt 1) {
        Fail "Seeded new-key replay evidence is ambiguous: found $seededNewKeyEvidenceCount rows."
    }
}
$expectedIdempotencyTotal =
    [int]$variables.baselineIdempotencyCount + 1 +
    $seededNewKeyEvidenceCount + $stripeEvidenceCount
Expect-Count 'Customer payment rows' ([int]$row.CustomerPayments) $expectedPaymentTotal
Expect-Count 'Customer payment idempotency rows' `
    ([int]$row.IdempotencyRecords) $expectedIdempotencyTotal

$expectedReceipts = if ($SignedWebhooksExecuted) {
    @($variables.expectedWebhookReceipts)
}
else {
    @()
}
if ($SignedWebhooksExecuted -and $expectedReceipts.Count -ne 23) {
    Fail "Variables must identify exactly 23 expected webhook receipts; found $($expectedReceipts.Count)."
}
$actualReceipts = Query $CustomerDatabase @"
SELECT EventId,EventType,BodyHash,State,DispositionReason,CustomerPaymentId
FROM StripeWebhookEvents
"@
Expect-Count 'Webhook receipt rows' $actualReceipts.Rows.Count `
    ([int]$variables.baselineWebhookCount + $expectedReceipts.Count)
if ([int]$variables.baselineWebhookCount -ne 0) {
    Fail 'The isolated fixture database unexpectedly contained baseline webhook receipts.'
}
foreach ($expected in $expectedReceipts) {
    $matches = @($actualReceipts.Rows | Where-Object {
        [string]$_.EventId -ceq [string]$expected.eventId
    })
    if ($matches.Count -ne 1) {
        Fail "Expected webhook '$($expected.eventId)' exactly once; found $($matches.Count)."
        continue
    }
    $actual = $matches[0]
    $expectedReason = if ($null -eq $expected.dispositionReason) {
        ''
    } else { [string]$expected.dispositionReason }
    $actualReason = if ($actual.IsNull('DispositionReason')) {
        ''
    } else { [string]$actual.DispositionReason }
    $expectedPaymentId = if ($null -eq $expected.customerPaymentId) {
        ''
    } else { [string]$expected.customerPaymentId }
    $actualPaymentId = if ($actual.IsNull('CustomerPaymentId')) {
        ''
    } else { [string]$actual.CustomerPaymentId }
    foreach ($comparison in @{
        EventType = [string]$expected.eventType
        BodyHash = [string]$expected.bodyHash
        State = [string]$expected.state
        DispositionReason = $expectedReason
        CustomerPaymentId = $expectedPaymentId
    }.GetEnumerator()) {
        $actualValue = switch ($comparison.Key) {
            'DispositionReason' { $actualReason }
            'CustomerPaymentId' { $actualPaymentId }
            default { [string]$actual.($comparison.Key) }
        }
        if ($actualValue -cne $comparison.Value) {
            Fail "Webhook '$($expected.eventId)' has unexpected $($comparison.Key)."
        }
    }
}

$actualBookingSnapshotHash = [string](Scalar $CustomerDatabase @"
SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', (
    SELECT Id, PublicReference, OrderGuid, UserId, OwnerDeviceId,
           BusinessReservationId, BusinessWorkOrderId, Status,
           GrandTotal, Currency, BaseSubtotal, AddonSubtotal, ItemSubtotal,
           ServiceFee, TaxableSubtotal, Tax
    FROM CustomerBookings
    ORDER BY Id
    FOR JSON PATH
)), 2) Value
"@)
if ($actualBookingSnapshotHash -ne [string]$variables.immutableBookingSnapshotHash) {
    Fail 'An immutable booking snapshot field changed during payment execution.'
}

$result = [ordered]@{
    passed = $failures.Count -eq 0
    stripeExecuted = $stripeWasExecuted
    signedWebhooksExecuted = [bool]$SignedWebhooksExecuted
    immutableBookingSnapshotPreserved =
        $actualBookingSnapshotHash -eq [string]$variables.immutableBookingSnapshotHash
    legacyPaymentContentPreserved = $sourceLegacyHash -eq $targetLegacyHash
    counts = [ordered]@{}
    failures = $failures
}
foreach ($column in $summary.Columns) {
    $result.counts[$column.ColumnName.Substring(0,1).ToLowerInvariant() +
        $column.ColumnName.Substring(1)] = [int]$row.($column.ColumnName)
}
$result | ConvertTo-Json -Depth 5
if (-not $result.passed) { exit 1 }
