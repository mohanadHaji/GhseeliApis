#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CustomerDatabase,
    [Parameter(Mandatory)]
    [string]$VariablesPath,
    [string]$Server = '(localdb)\MSSQLLocalDB'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($CustomerDatabase -notmatch '^[A-Za-z0-9_]+$') {
    throw 'Unsafe database name.'
}
$variables = Get-Content -LiteralPath $VariablesPath -Raw -Encoding UTF8 |
    ConvertFrom-Json

function Invoke-Scalar([string]$Sql) {
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$Server;Database=$CustomerDatabase;Integrated Security=true;TrustServerCertificate=true")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        return $command.ExecuteScalar()
    }
    finally { $connection.Dispose() }
}

function Quote-Sql([string]$Value) {
    return "N'$($Value.Replace("'", "''"))'"
}

$bookingReferences = @(
    [string]$variables.primaryBookingId,
    [string]$variables.secondaryBookingId,
    [string]$variables.pendingVerifyBookingId,
    [string]$variables.partialRefundBookingId
)
foreach ($bookingReference in $bookingReferences) {
    $parsedReference = [guid]::Empty
    if (-not [guid]::TryParse($bookingReference, [ref]$parsedReference) -or
        $parsedReference -eq [guid]::Empty) {
        throw 'Step 18 fixture variables do not contain four booking references.'
    }
}
$bookingReferenceSql = ($bookingReferences | ForEach-Object {
        "CAST('$_' AS uniqueidentifier)"
    }) -join ','
$paymentIdSql = @"
SELECT p.Id
FROM dbo.CustomerPayments p
JOIN dbo.CustomerBookings b ON b.Id=p.CustomerBookingId
WHERE b.PublicReference IN ($bookingReferenceSql)
"@
if ([int](Invoke-Scalar `
        "SELECT COUNT(*) FROM ($paymentIdSql) expected") -ne 4) {
    throw 'Step 18 did not persist exactly four expected local payments.'
}
if ([int](Invoke-Scalar @"
SELECT COUNT(*)
FROM dbo.CustomerPaymentIdempotencyRecords
WHERE CustomerPaymentId IN ($paymentIdSql);
"@) -ne 4) {
    throw 'Step 18 replay/conflict handling changed the expected idempotency-row count.'
}

$expectedStatuses = @{
    ([string]$variables.primaryBookingId) = 3
    ([string]$variables.secondaryBookingId) = 3
    ([string]$variables.pendingVerifyBookingId) = 0
    ([string]$variables.partialRefundBookingId) = 1
}
foreach ($entry in $expectedStatuses.GetEnumerator()) {
    $actual = [int](Invoke-Scalar `
        ("SELECT p.Status FROM dbo.CustomerPayments p " +
         "JOIN dbo.CustomerBookings b ON b.Id=p.CustomerBookingId " +
         "WHERE b.PublicReference='$($entry.Key)'"))
    if ($actual -ne [int]$entry.Value) {
        throw "Payment $($entry.Key) has status $actual, expected $($entry.Value)."
    }
}

if ([int](Invoke-Scalar @"
SELECT COUNT(*)
FROM dbo.CustomerPayments p
JOIN dbo.CustomerBookings b ON b.Id=p.CustomerBookingId
WHERE p.Id IN ($paymentIdSql)
  AND (
    (p.Status=0 AND (b.IsPaid<>0 OR b.PaymentState<>N'Unpaid')) OR
    (p.Status=1 AND (b.IsPaid<>1 OR b.PaymentState<>N'Completed')) OR
    (p.Status=3 AND (b.IsPaid<>0 OR b.PaymentState<>N'Refunded'))
  );
"@) -ne 0) {
    throw 'Step 18 payment and booking states did not converge atomically.'
}

$eventIds = @(
    [string]$variables.unknownEventId,
    [string]$variables.successEventId,
    'evt_step18_quarantine',
    [string]$variables.refundPendingEventId,
    [string]$variables.refundProcessingEventId,
    [string]$variables.refundProcessedEventId,
    [string]$variables.deferredRefundEventId,
    [string]$variables.deferredSuccessEventId,
    [string]$variables.partialSuccessEventId,
    [string]$variables.partialRefundEventId,
    [string]$variables.refundFailedEventId
)
$eventIdSql = ($eventIds | ForEach-Object { Quote-Sql $_ }) -join ','
if ([int](Invoke-Scalar @"
SELECT COUNT(*)
FROM dbo.PaymentWebhookEvents
WHERE Provider=N'Lahza' AND EventId IN ($eventIdSql);
"@) -ne 11) {
    throw 'Step 18 webhook replay/conflict handling did not retain exactly eleven receipts.'
}
if ([int](Invoke-Scalar @"
SELECT COUNT(*)
FROM dbo.PaymentWebhookEvents
WHERE Provider=N'Lahza'
  AND EventId=$(Quote-Sql ([string]$variables.successEventId));
"@) -ne 1) {
    throw 'Step 18 identical webhook replay persisted more than one receipt.'
}
if ([string](Invoke-Scalar @"
SELECT State + N':' + ISNULL(DispositionReason,N'')
FROM dbo.PaymentWebhookEvents
WHERE Provider=N'Lahza' AND EventId=N'evt_step18_quarantine';
"@) -cne 'Quarantined:payment_reference_not_found') {
    throw 'Step 18 unknown payment webhook was not durably quarantined.'
}
if ([string](Invoke-Scalar @"
SELECT State + N':' + ISNULL(DispositionReason,N'')
FROM dbo.PaymentWebhookEvents
WHERE Provider=N'Lahza'
  AND EventId=$(Quote-Sql ([string]$variables.partialRefundEventId));
"@) -cne 'Completed:partial_refund_not_applied') {
    throw 'Step 18 partial refund did not preserve paid state with a durable disposition.'
}
if ([int](Invoke-Scalar @"
SELECT COUNT(*)
FROM dbo.CustomerPayments p
JOIN dbo.CustomerBookings b ON b.Id=p.CustomerBookingId
WHERE b.PublicReference=CAST('$([string]$variables.unconfiguredBookingId)' AS uniqueidentifier);
"@) -ne 0) {
    throw 'Unconfigured provider behavior persisted a payment unexpectedly.'
}

Write-Host 'Step 18 database invariants passed: 4 payments, 4 idempotency rows, 11 Lahza receipts.'
