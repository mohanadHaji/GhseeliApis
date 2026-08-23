param(
    [Parameter(Mandatory = $true)]
    [string]$CustomerDatabase,

    [Parameter(Mandatory = $true)]
    [string]$BusinessDatabase,

    [int]$ExpectedBookings = 2,
    [int]$ExpectedItems = 3,
    [int]$ExpectedSelections = 2,

    [int]$ExpectedBusinessReservations = $ExpectedBookings,
    [int]$ExpectedBusinessItems = $ExpectedItems,
    [int]$ExpectedBusinessSelections = $ExpectedSelections
)

$ErrorActionPreference = 'Stop'
$server = '(localdb)\MSSQLLocalDB'

function Invoke-Query {
    param([string]$Database, [string]$Sql)

    $connection = [System.Data.SqlClient.SqlConnection]::new(
        "Server=$server;Database=$Database;Integrated Security=true;TrustServerCertificate=true")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $adapter = [System.Data.SqlClient.SqlDataAdapter]::new($command)
        $table = [System.Data.DataTable]::new()
        [void]$adapter.Fill($table)
        return ,$table
    }
    finally {
        $connection.Dispose()
    }
}

$customer = Invoke-Query $CustomerDatabase @'
SELECT
    (SELECT COUNT(*) FROM CustomerBookings) AS Bookings,
    (SELECT COUNT(*) FROM CustomerBookingItems) AS Items,
    (SELECT COUNT(*) FROM CustomerBookingSelections) AS Selections,
    (SELECT COUNT(*) FROM BookingConfirmationAttempts) AS Attempts,
    (SELECT COUNT(*) FROM Payments) AS Payments
'@

$business = Invoke-Query $BusinessDatabase @'
SELECT
    (SELECT COUNT(*) FROM AppointmentReservations) AS Reservations,
    (SELECT COUNT(*) FROM WorkOrders) AS WorkOrders,
    (SELECT COUNT(*) FROM WorkOrderItems) AS Items,
    (SELECT COUNT(*) FROM WorkOrderSelections) AS Selections
'@

$crossReferences = Invoke-Query $CustomerDatabase @"
SELECT COUNT(*) AS MatchingRows
FROM CustomerBookings customer
WHERE EXISTS (
    SELECT 1
    FROM [$BusinessDatabase].dbo.AppointmentReservations reservation
    JOIN [$BusinessDatabase].dbo.WorkOrders workOrder
      ON workOrder.AppointmentReservationId = reservation.Id
    WHERE reservation.OrderGuid = customer.OrderGuid
      AND reservation.CustomerBookingReference = customer.PublicReference
      AND reservation.PublicId = customer.BusinessReservationId
      AND workOrder.PublicId = customer.BusinessWorkOrderId
      AND reservation.ItemSubtotal = customer.ItemSubtotal
      AND reservation.TotalDurationMinutes = customer.TotalDurationMinutes
      AND reservation.Status = customer.Status
)
"@

$actual = [ordered]@{
    customerBookings = [int]$customer.Rows[0].Bookings
    customerItems = [int]$customer.Rows[0].Items
    customerSelections = [int]$customer.Rows[0].Selections
    confirmationAttempts = [int]$customer.Rows[0].Attempts
    payments = [int]$customer.Rows[0].Payments
    businessReservations = [int]$business.Rows[0].Reservations
    businessWorkOrders = [int]$business.Rows[0].WorkOrders
    businessItems = [int]$business.Rows[0].Items
    businessSelections = [int]$business.Rows[0].Selections
    matchingCrossReferences = [int]$crossReferences.Rows[0].MatchingRows
}

$expected = [ordered]@{
    customerBookings = $ExpectedBookings
    customerItems = $ExpectedItems
    customerSelections = $ExpectedSelections
    confirmationAttempts = $ExpectedBookings
    payments = 0
    businessReservations = $ExpectedBusinessReservations
    businessWorkOrders = $ExpectedBusinessReservations
    businessItems = $ExpectedBusinessItems
    businessSelections = $ExpectedBusinessSelections
    matchingCrossReferences = $ExpectedBookings
}

$failures = foreach ($key in $expected.Keys) {
    if ($actual[$key] -ne $expected[$key]) {
        "$key expected $($expected[$key]) but was $($actual[$key])"
    }
}

[ordered]@{
    passed = @($failures).Count -eq 0
    expected = $expected
    actual = $actual
    failures = @($failures)
} | ConvertTo-Json -Depth 4

if (@($failures).Count -ne 0) {
    exit 1
}
