param(
    [Parameter(Mandatory = $true)]
    [string]$CustomerDatabase,
    [Parameter(Mandatory = $true)]
    [string]$BusinessDatabase,
    [Parameter(Mandatory = $true)]
    [string]$SourceCustomerDatabase,
    [Parameter(Mandatory = $true)]
    [string]$SourceBusinessDatabase,
    [Parameter(Mandatory = $true)]
    [string]$VariablesPath
)

$ErrorActionPreference = 'Stop'
$server = '(localdb)\MSSQLLocalDB'
foreach ($name in @($CustomerDatabase, $BusinessDatabase, $SourceCustomerDatabase, $SourceBusinessDatabase)) {
    if ($name -notmatch '^[A-Za-z0-9_]+$') { throw 'Unsafe database name.' }
}
$variablesJson = Get-Content -LiteralPath $VariablesPath -Raw
$convertCommand = Get-Command ConvertFrom-Json
$variables = if ($convertCommand.Parameters.ContainsKey('DateKind')) {
    $variablesJson | ConvertFrom-Json -DateKind String
}
else {
    $variablesJson | ConvertFrom-Json
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

$expectedCustomer = @(
    @{ n = 1; status = 'Confirmed'; sequence = 1; inbox = 1 },
    @{ n = 2; status = 'Confirmed'; sequence = 1; inbox = 1 },
    @{ n = 3; status = 'Confirmed'; sequence = 1; inbox = 1 },
    @{ n = 4; status = 'Confirmed'; sequence = 2; inbox = 1 },
    @{ n = 5; status = 'Confirmed'; sequence = 1; inbox = 1 },
    @{ n = 6; status = 'Confirmed'; sequence = 1; inbox = 1 },
    @{ n = 7; status = 'Confirmed'; sequence = 1; inbox = 2; realEvent = $true },
    @{ n = 8; status = 'Confirmed'; sequence = 1; inbox = 2; realEvent = $true }
)

$failures = [Collections.Generic.List[string]]::new()
$customerStates = [Collections.Generic.List[object]]::new()
foreach ($expected in $expectedCustomer) {
    $reference = [string]$variables.("bookingReference$($expected.n)")
    $realEventId = [string]$variables.("realEventId$($expected.n)")
    $row = Query $CustomerDatabase @"
SELECT b.Status, b.BusinessStatusSequence,
       (SELECT COUNT(*) FROM ProcessedBookingStatusMessages m WHERE m.CustomerBookingId=b.Id) InboxCount,
       (SELECT COUNT(*) FROM ProcessedBookingStatusMessages m WHERE m.CustomerBookingId=b.Id AND m.EventId='$realEventId') RealEventCount
FROM CustomerBookings b WHERE b.PublicReference='$reference'
"@
    if ($row.Rows.Count -ne 1) {
        $failures.Add("Customer fixture $($expected.n) was not found.")
        continue
    }
    $actual = [ordered]@{
        fixture = $expected.n
        status = [string]$row.Rows[0].Status
        sequence = [long]$row.Rows[0].BusinessStatusSequence
        inbox = [int]$row.Rows[0].InboxCount
        realEvent = [int]$row.Rows[0].RealEventCount
    }
    $customerStates.Add($actual)
    foreach ($field in @('status', 'sequence', 'inbox')) {
        if ($actual[$field] -ne $expected[$field]) {
            $failures.Add("Customer fixture $($expected.n) $field expected $($expected[$field]) but was $($actual[$field]).")
        }
    }
    if ($expected.realEvent -and $actual.realEvent -ne 1) {
        $failures.Add("Customer fixture $($expected.n) expected exactly one persisted real event identity but found $($actual.realEvent).")
    }
}

$businessReference = [string]$variables.bookingReference6
$business = Query $BusinessDatabase @"
SELECT r.Status, r.StatusSequence, w.Status WorkOrderStatus,
       (SELECT COUNT(*) FROM BookingStatusOutboxMessages o WHERE o.AppointmentReservationId=r.Id) OutboxCount,
       (SELECT COUNT(*) FROM BookingStatusOutboxMessages o WHERE o.AppointmentReservationId=r.Id AND o.DeliveredAtUtc IS NOT NULL) DeliveredCount
FROM AppointmentReservations r
JOIN WorkOrders w ON w.AppointmentReservationId=r.Id
WHERE r.CustomerBookingReference='$businessReference'
"@
if ($business.Rows.Count -ne 1) {
    $failures.Add('Business transition fixture was not found.')
}
else {
    $b = $business.Rows[0]
    if ([string]$b.Status -ne 'Confirmed') { $failures.Add("Business reservation status expected Confirmed but was $($b.Status).") }
    if ([long]$b.StatusSequence -ne 1) { $failures.Add("Business sequence expected 1 but was $($b.StatusSequence).") }
    if ([string]$b.WorkOrderStatus -ne 'Confirmed') { $failures.Add("Business work-order status expected Confirmed but was $($b.WorkOrderStatus).") }
    if ([int]$b.OutboxCount -ne 1) { $failures.Add("Business outbox count expected 1 but was $($b.OutboxCount).") }
    if ([int]$b.DeliveredCount -ne 1) { $failures.Add("Business delivered outbox count expected 1 but was $($b.DeliveredCount).") }
}

$counts = Query $CustomerDatabase @"
SELECT
 (SELECT COUNT(*) FROM CustomerBookings) CustomerBookings,
 (SELECT COUNT(*) FROM ProcessedBookingStatusMessages) InboxMessages,
 (SELECT COUNT(*) FROM Payments) Payments,
 (SELECT COUNT(*) FROM [$SourceCustomerDatabase].dbo.Payments) SourcePayments,
 (SELECT COUNT(*) FROM CustomerBookingItems) BookingItems,
 (SELECT COUNT(*) FROM [$SourceCustomerDatabase].dbo.CustomerBookingItems) SourceBookingItems,
 (SELECT COUNT(*) FROM CustomerBookingSelections) BookingSelections,
 (SELECT COUNT(*) FROM [$SourceCustomerDatabase].dbo.CustomerBookingSelections) SourceBookingSelections,
 (SELECT COUNT(*) FROM CustomerBookings WHERE Status='Reserved') LegacyReservedBookings,
 (SELECT COUNT(*) FROM CustomerInternalIdempotencyRecords) TransportIdempotencyRecords,
 (SELECT COUNT(*) FROM CustomerInternalIdempotencyRecords WHERE State<>1) IncompleteTransportRecords,
 (SELECT COUNT(*) FROM CustomerInternalIdempotencyRecords
  WHERE OwnerToken IS NOT NULL OR LeaseExpiresAtUtc IS NOT NULL) OwnedTransportRecords
"@
$countRow = $counts.Rows[0]
foreach ($pair in @(
    @('Payments', 'SourcePayments'),
    @('BookingItems', 'SourceBookingItems'),
    @('BookingSelections', 'SourceBookingSelections')
)) {
    if ([int]$countRow.($pair[0]) -ne [int]$countRow.($pair[1])) {
        $failures.Add("$($pair[0]) changed from source fixture.")
    }
}
if ([int]$countRow.LegacyReservedBookings -ne 0) {
    $failures.Add("Legacy Reserved customer bookings expected 0 but was $($countRow.LegacyReservedBookings).")
}
if ([int]$countRow.TransportIdempotencyRecords -ne 28) {
    $failures.Add("Completed transport idempotency records expected 28 but was $($countRow.TransportIdempotencyRecords).")
}
if ([int]$countRow.IncompleteTransportRecords -ne 0) {
    $failures.Add("Incomplete transport idempotency records expected 0 but was $($countRow.IncompleteTransportRecords).")
}
if ([int]$countRow.OwnedTransportRecords -ne 0) {
    $failures.Add("Owned or leased transport idempotency records expected 0 but was $($countRow.OwnedTransportRecords).")
}

$businessCounts = Query $BusinessDatabase @"
SELECT
 (SELECT COUNT(*) FROM AppointmentReservations) Reservations,
 (SELECT COUNT(*) FROM [$SourceBusinessDatabase].dbo.AppointmentReservations) SourceReservations,
 (SELECT COUNT(*) FROM WorkOrders) WorkOrders,
 (SELECT COUNT(*) FROM [$SourceBusinessDatabase].dbo.WorkOrders) SourceWorkOrders,
 (SELECT COUNT(*) FROM BookingStatusOutboxMessages) OutboxMessages,
 (SELECT COUNT(*) FROM BookingStatusOutboxMessages WHERE DeliveryState='Delivered') DeliveredMessages,
 (SELECT COUNT(*) FROM BookingStatusOutboxMessages WHERE DeliveryState='DeadLetter') DeadLetterMessages,
 (SELECT COUNT(*) FROM AppointmentReservations WHERE Status='Reserved') LegacyReservedReservations
"@
$businessCountRow = $businessCounts.Rows[0]
if ([int]$businessCountRow.Reservations -ne [int]$businessCountRow.SourceReservations + 43) {
    $failures.Add('Business reservation count did not equal source plus 12 status and 31 capacity fixtures/results.')
}
if ([int]$businessCountRow.WorkOrders -ne [int]$businessCountRow.SourceWorkOrders + 15) {
    $failures.Add('Business work-order count did not equal source plus 12 status fixtures and 3 released-capacity results.')
}
if ([int]$businessCountRow.OutboxMessages -ne 2) {
    $failures.Add("Business total outbox count expected 2 but was $($businessCountRow.OutboxMessages).")
}
if ([int]$businessCountRow.DeliveredMessages -ne 1) {
    $failures.Add("Business total delivered outbox count expected 1 but was $($businessCountRow.DeliveredMessages).")
}
if ([int]$businessCountRow.DeadLetterMessages -ne 1) {
    $failures.Add("Business dead-letter count expected 1 but was $($businessCountRow.DeadLetterMessages).")
}
if ([int]$businessCountRow.LegacyReservedReservations -ne 4) {
    $failures.Add("Legacy Reserved capacity fixtures expected 4 but was $($businessCountRow.LegacyReservedReservations).")
}

$capacityStates = @()
foreach ($capacityCase in @(
    @('Pending', $true),
    @('Reserved', $true),
    @('Confirmed', $true),
    @('InProgress', $true),
    @('Completed', $false),
    @('Cancelled', $false),
    @('NoShow', $false)
)) {
    $capacityName = [string]$capacityCase[0]
    $occupies = [bool]$capacityCase[1]
    $slot = [string]$variables.("capacity${capacityName}SlotUtc")
    $capacityRows = Query $BusinessDatabase @"
SELECT COUNT(*) Total,
       SUM(CASE WHEN Status='$capacityName' THEN 1 ELSE 0 END) SeedStatus,
       SUM(CASE WHEN Status='Pending' THEN 1 ELSE 0 END) Pending
FROM AppointmentReservations
WHERE RequestedSlotStartUtc=CAST('$slot' AS datetimeoffset)
"@
    $capacityRow = $capacityRows.Rows[0]
    $expectedTotal = if ($occupies) { 4 } else { 5 }
    if ([int]$capacityRow.Total -ne $expectedTotal) {
        $failures.Add("Capacity $capacityName slot expected $expectedTotal reservations but was $($capacityRow.Total).")
    }
    if ([int]$capacityRow.SeedStatus -ne 4) {
        $failures.Add("Capacity $capacityName slot expected four seeded status rows but was $($capacityRow.SeedStatus).")
    }
    if (-not $occupies -and [int]$capacityRow.Pending -ne 1) {
        $failures.Add("Capacity $capacityName slot expected one newly accepted Pending reservation but was $($capacityRow.Pending).")
    }
    $capacityStates += [ordered]@{
        status = $capacityName
        occupies = $occupies
        total = [int]$capacityRow.Total
        pending = [int]$capacityRow.Pending
    }
}

$crossReferences = 0
for ($n = 1; $n -le 12; $n++) {
    $reference = [string]$variables.("bookingReference$n")
    $match = Query $CustomerDatabase @"
SELECT COUNT(*) Matching
FROM CustomerBookings c
JOIN [$BusinessDatabase].dbo.AppointmentReservations r ON r.CustomerBookingReference=c.PublicReference
JOIN [$BusinessDatabase].dbo.WorkOrders w ON w.AppointmentReservationId=r.Id
WHERE c.PublicReference='$reference'
  AND c.BusinessReservationId=r.PublicId
  AND c.BusinessWorkOrderId=w.PublicId
  AND c.OrderGuid=r.OrderGuid
"@
    $crossReferences += [int]$match.Rows[0].Matching
}
if ($crossReferences -ne 12) { $failures.Add("Cross-reference matches expected 12 but was $crossReferences.") }

$recoveryEventId = [string]$variables.recoveryOutboxEventId
$recoveryRows = Query $BusinessDatabase @"
SELECT o.DeliveryState, o.DeliveryGeneration, o.AttemptCount, o.LastErrorCode,
       o.RequeuedAtUtc, o.RequeuedByAdminUserId, o.RequeueRequestId,
       (SELECT COUNT(*) FROM BookingStatusRequeueHistory h
        WHERE h.BookingStatusOutboxMessageId=o.Id) HistoryCount,
       (SELECT COUNT(*) FROM BookingStatusRequeueHistory h
        WHERE h.BookingStatusOutboxMessageId=o.Id AND h.Generation=1) GenerationOneCount
FROM BookingStatusOutboxMessages o
WHERE o.Id='$recoveryEventId'
"@
$recovery = if ($recoveryRows.Rows.Count -eq 1) { $recoveryRows.Rows[0] } else { $null }
if ($null -eq $recovery) {
    $failures.Add('Recovery outbox fixture was not found.')
}
else {
    if ([string]$recovery.DeliveryState -ne 'DeadLetter') {
        $failures.Add("Recovery outbox state expected DeadLetter but was $($recovery.DeliveryState).")
    }
    if ([int]$recovery.DeliveryGeneration -ne 1) {
        $failures.Add("Recovery delivery generation expected 1 but was $($recovery.DeliveryGeneration).")
    }
    if ([int]$recovery.AttemptCount -ne 1) {
        $failures.Add("Recovery attempt count expected 1 but was $($recovery.AttemptCount).")
    }
    if ([string]$recovery.LastErrorCode -ne 'HTTP_400') {
        $failures.Add("Recovery error expected HTTP_400 but was $($recovery.LastErrorCode).")
    }
    if ([int]$recovery.HistoryCount -ne 1 -or [int]$recovery.GenerationOneCount -ne 1) {
        $failures.Add('Recovery requeue history expected exactly one generation-1 audit row.')
    }
    if ($recovery.RequeuedAtUtc -is [DBNull] -or
        $recovery.RequeuedByAdminUserId -is [DBNull] -or
        [string]::IsNullOrWhiteSpace([string]$recovery.RequeueRequestId)) {
        $failures.Add('Recovery requeue audit marker was incomplete.')
    }
}

$result = [ordered]@{
    passed = $failures.Count -eq 0
    customerStates = $customerStates
    counts = [ordered]@{
        customerBookings = [int]$countRow.CustomerBookings
        inboxMessages = [int]$countRow.InboxMessages
        payments = [int]$countRow.Payments
        sourcePayments = [int]$countRow.SourcePayments
        bookingItems = [int]$countRow.BookingItems
        sourceBookingItems = [int]$countRow.SourceBookingItems
        bookingSelections = [int]$countRow.BookingSelections
        sourceBookingSelections = [int]$countRow.SourceBookingSelections
        legacyReservedBookings = [int]$countRow.LegacyReservedBookings
        transportIdempotencyRecords = [int]$countRow.TransportIdempotencyRecords
        incompleteTransportRecords = [int]$countRow.IncompleteTransportRecords
        ownedTransportRecords = [int]$countRow.OwnedTransportRecords
        matchingCrossReferences = $crossReferences
        businessReservations = [int]$businessCountRow.Reservations
        sourceBusinessReservations = [int]$businessCountRow.SourceReservations
        businessWorkOrders = [int]$businessCountRow.WorkOrders
        sourceBusinessWorkOrders = [int]$businessCountRow.SourceWorkOrders
        businessOutboxMessages = [int]$businessCountRow.OutboxMessages
        businessDeliveredMessages = [int]$businessCountRow.DeliveredMessages
        businessDeadLetterMessages = [int]$businessCountRow.DeadLetterMessages
        legacyReservedReservations = [int]$businessCountRow.LegacyReservedReservations
    }
    business = if ($business.Rows.Count -eq 1) {
        [ordered]@{
            reservationStatus = [string]$business.Rows[0].Status
            sequence = [long]$business.Rows[0].StatusSequence
            workOrderStatus = [string]$business.Rows[0].WorkOrderStatus
            outbox = [int]$business.Rows[0].OutboxCount
            delivered = [int]$business.Rows[0].DeliveredCount
        }
    } else { $null }
    recovery = if ($null -ne $recovery) {
        [ordered]@{
            eventId = $recoveryEventId
            state = [string]$recovery.DeliveryState
            generation = [int]$recovery.DeliveryGeneration
            attempts = [int]$recovery.AttemptCount
            history = [int]$recovery.HistoryCount
        }
    } else { $null }
    capacityStates = $capacityStates
    failures = $failures
}
$result | ConvertTo-Json -Depth 6
if (-not $result.passed) { exit 1 }
