param(
    [Parameter(Mandatory = $true)]
    [string]$SourceCustomerDatabase,
    [Parameter(Mandatory = $true)]
    [string]$SourceBusinessDatabase,
    [Parameter(Mandatory = $true)]
    [string]$CustomerDatabase,
    [Parameter(Mandatory = $true)]
    [string]$BusinessDatabase,
    [string]$VariablesPath = '.\scripts\http-tests\step-13.variables.local.json',
    [int]$FixtureCount = 12
)

$ErrorActionPreference = 'Stop'
$server = '(localdb)\MSSQLLocalDB'
$solution = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

foreach ($name in @($SourceCustomerDatabase, $SourceBusinessDatabase, $CustomerDatabase, $BusinessDatabase)) {
    if ($name -notmatch '^[A-Za-z0-9_]+$') {
        throw "Unsafe database name."
    }
}
if ($FixtureCount -lt 2 -or $FixtureCount -gt 50) {
    throw 'FixtureCount must be between 2 and 50.'
}

function Open-Connection([string]$database) {
    $connection = [System.Data.SqlClient.SqlConnection]::new(
        "Server=$server;Database=$database;Integrated Security=true;TrustServerCertificate=true")
    $connection.Open()
    return $connection
}

function Invoke-Scalar([string]$database, [string]$sql) {
    $connection = Open-Connection $database
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $sql
        return $command.ExecuteScalar()
    }
    finally {
        $connection.Dispose()
    }
}

function Invoke-Sql([string]$database, [string]$sql) {
    $connection = Open-Connection $database
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $sql
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $connection.Dispose()
    }
}

function Copy-Database([string]$source, [string]$target) {
    $backup = Join-Path $artifacts "$target.bak"
    $escapedBackup = $backup.Replace("'", "''")
    Invoke-Sql master "IF DB_ID(N'$target') IS NOT NULL BEGIN ALTER DATABASE [$target] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$target]; END; BACKUP DATABASE [$source] TO DISK=N'$escapedBackup' WITH INIT, COPY_ONLY;"

    $connection = Open-Connection master
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = "RESTORE FILELISTONLY FROM DISK=N'$escapedBackup'"
        $reader = $command.ExecuteReader()
        $files = @()
        while ($reader.Read()) {
            $files += [pscustomobject]@{ LogicalName = [string]$reader['LogicalName']; Type = [string]$reader['Type'] }
        }
        $reader.Close()
        $dataRoot = [string](Invoke-Scalar master "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000))")
        if ([string]::IsNullOrWhiteSpace($dataRoot)) {
            $dataRoot = Split-Path -Parent ([string](Invoke-Scalar master "SELECT physical_name FROM sys.master_files WHERE database_id=1 AND file_id=1"))
        }
        $moves = foreach ($file in $files) {
            $extension = if ($file.Type -eq 'L') { '.ldf' } else { '.mdf' }
            ", MOVE N'$($file.LogicalName.Replace("'", "''"))' TO N'$((Join-Path $dataRoot ($target + $extension)).Replace("'", "''"))'"
        }
        Invoke-Sql master "RESTORE DATABASE [$target] FROM DISK=N'$escapedBackup' WITH REPLACE$($moves -join '')"
    }
    finally {
        $connection.Dispose()
    }
}

function Get-CloneSql(
    [string]$database,
    [string]$table,
    [string]$where,
    [hashtable]$replacements
) {
    $connection = Open-Connection $database
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = @"
SELECT name
FROM sys.columns
WHERE object_id = OBJECT_ID(N'dbo.$table')
  AND is_computed = 0
  AND system_type_id <> 189
ORDER BY column_id
"@
        $reader = $command.ExecuteReader()
        $columns = @()
        while ($reader.Read()) { $columns += [string]$reader['name'] }
        $reader.Close()
    }
    finally {
        $connection.Dispose()
    }
    $select = foreach ($column in $columns) {
        if ($replacements.ContainsKey($column)) { $replacements[$column] } else { "[$column]" }
    }
    return "INSERT INTO dbo.[$table] ($($columns.ForEach({"[$_]"} ) -join ',')) SELECT $($select -join ',') FROM dbo.[$table] WHERE $where;"
}

Copy-Database $SourceCustomerDatabase $CustomerDatabase
Copy-Database $SourceBusinessDatabase $BusinessDatabase

$env:ConnectionStrings__RemoteTest = "Server=$server;Database=$CustomerDatabase;Integrated Security=true;TrustServerCertificate=true"
& dotnet ef database update --project (Join-Path $solution 'GhseeliApis\GhseeliApis.csproj') --startup-project (Join-Path $solution 'GhseeliApis\GhseeliApis.csproj') --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Customer migration failed.' }
$env:ConnectionStrings__BusinessConnection = "Server=$server;Database=$BusinessDatabase;Integrated Security=true;TrustServerCertificate=true"
& dotnet ef database update --project (Join-Path $solution 'Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj') --startup-project (Join-Path $solution 'Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj') --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Business migration failed.' }
Remove-Item Env:\ConnectionStrings__BusinessConnection

$sourceReference = [guid](Invoke-Scalar $CustomerDatabase "SELECT TOP (1) PublicReference FROM CustomerBookings ORDER BY CreatedAtUtc")
$sourceReservationId = [guid](Invoke-Scalar $BusinessDatabase "SELECT Id FROM AppointmentReservations WHERE CustomerBookingReference='$sourceReference'")
$sourceWorkOrderId = [guid](Invoke-Scalar $BusinessDatabase "SELECT Id FROM WorkOrders WHERE AppointmentReservationId='$sourceReservationId'")
$fixtureOwnerId = [guid]'88888888-8888-4888-8888-888888888888'
$fixtureAssignmentId = [guid]'99999999-9999-4999-8999-999999999999'
$fixtureCompanyId = [guid](Invoke-Scalar $BusinessDatabase "SELECT b.CompanyId FROM AppointmentReservations r JOIN Branches b ON b.Id=r.BranchId WHERE r.Id='$sourceReservationId'")
Invoke-Sql $BusinessDatabase @"
IF NOT EXISTS (SELECT 1 FROM AspNetUsers WHERE Id='$fixtureOwnerId')
BEGIN
    INSERT INTO AspNetUsers
        (Id, FullName, IsActive, CreatedAt, UserName, NormalizedUserName, Email, NormalizedEmail,
         EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
    VALUES
        ('$fixtureOwnerId', N'Step 13 Fixture Owner', 1, SYSUTCDATETIME(),
         N'step13-fixture-owner', N'STEP13-FIXTURE-OWNER', NULL, NULL, 0, 0, 0, 0, 0);
END;
IF NOT EXISTS (SELECT 1 FROM BusinessUserAssignments WHERE Id='$fixtureAssignmentId')
BEGIN
    INSERT INTO BusinessUserAssignments
        (Id, UserId, CompanyId, BranchId, Role, IsActive, CreatedAt)
    VALUES
        ('$fixtureAssignmentId', '$fixtureOwnerId', '$fixtureCompanyId', NULL, 0, 1, SYSUTCDATETIME());
END;
"@

$variables = [ordered]@{}
$variables['businessOwnerUserId'] = $fixtureOwnerId.ToString('D')
for ($index = 1; $index -le $FixtureCount; $index++) {
    $bookingId = [guid]::NewGuid()
    $bookingReference = [guid]::NewGuid()
    $orderGuid = [guid]::NewGuid()
    $reservationId = [guid]::NewGuid()
    $reservationPublicId = [guid]::NewGuid()
    $workOrderId = [guid]::NewGuid()
    $workOrderPublicId = [guid]::NewGuid()
    $realEventId = [guid]::NewGuid()
    $changed = "CAST('2030-01-15T09:00:00+00:00' AS datetimeoffset)"
    $authoritativeStatus = if ($index -in 7, 8) { "N'Confirmed'" } else { "N'Pending'" }
    $authoritativeSequence = if ($index -in 7, 8) { 'CAST(1 AS bigint)' } else { 'CAST(0 AS bigint)' }

    $customerSql = Get-CloneSql $CustomerDatabase 'CustomerBookings' "PublicReference='$sourceReference'" @{
        Id = "CAST('$bookingId' AS uniqueidentifier)"
        PublicReference = "CAST('$bookingReference' AS uniqueidentifier)"
        OrderGuid = "CAST('$orderGuid' AS uniqueidentifier)"
        BusinessReservationId = "CAST('$reservationPublicId' AS uniqueidentifier)"
        BusinessWorkOrderId = "CAST('$workOrderPublicId' AS uniqueidentifier)"
        Status = "N'Pending'"
        BusinessStatusSequence = 'CAST(0 AS bigint)'
        StatusChangedAtUtc = $changed
    }
    Invoke-Sql $CustomerDatabase $customerSql

    $reservationSql = Get-CloneSql $BusinessDatabase 'AppointmentReservations' "Id='$sourceReservationId'" @{
        Id = "CAST('$reservationId' AS uniqueidentifier)"
        PublicId = "CAST('$reservationPublicId' AS uniqueidentifier)"
        CustomerBookingReference = "CAST('$bookingReference' AS uniqueidentifier)"
        OrderGuid = "CAST('$orderGuid' AS uniqueidentifier)"
        RequestHash = "LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256', '$bookingReference'), 2))"
        Status = $authoritativeStatus
        StatusSequence = $authoritativeSequence
        StatusChangedAtUtc = $changed
    }
    Invoke-Sql $BusinessDatabase $reservationSql

    $workOrderSql = Get-CloneSql $BusinessDatabase 'WorkOrders' "Id='$sourceWorkOrderId'" @{
        Id = "CAST('$workOrderId' AS uniqueidentifier)"
        PublicId = "CAST('$workOrderPublicId' AS uniqueidentifier)"
        AppointmentReservationId = "CAST('$reservationId' AS uniqueidentifier)"
        Status = $authoritativeStatus
    }
    Invoke-Sql $BusinessDatabase $workOrderSql

    $variables["bookingReference$index"] = $bookingReference.ToString('D')
    $variables["reservationId$index"] = $reservationPublicId.ToString('D')
    $variables["workOrderId$index"] = $workOrderPublicId.ToString('D')
    $variables["realEventId$index"] = $realEventId.ToString('D')
}

$recoveryEventId = [guid]::NewGuid()
$recoveryCreatedAt = "CAST('2026-08-23T12:00:00+00:00' AS datetimeoffset)"
Invoke-Sql $BusinessDatabase @"
INSERT INTO BookingStatusOutboxMessages
    (Id, AppointmentReservationId, RequestJson, RequestHash, WorkOrderPublicId,
     Status, Sequence, CorrelationId, DeliveryState, DeliveryGeneration,
     AttemptCount, CreatedAtUtc, NextAttemptAtUtc, DeliveredAtUtc,
     DeadLetteredAtUtc, LeaseToken, LeaseOwner, LeaseExpiresAtUtc, LastErrorCode,
     RequeuedAtUtc, RequeuedByAdminUserId, RequeueRequestId)
VALUES
    ('$recoveryEventId', '$reservationId', N'{}',
     LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256', N'{}'), 2)),
     '$workOrderPublicId', N'Confirmed', 1, N'step13-recovery-fixture',
     N'DeadLetter', 0, 1, $recoveryCreatedAt, $recoveryCreatedAt, NULL,
     $recoveryCreatedAt, NULL, NULL, NULL, N'HTTP_400', NULL, NULL, NULL);
"@
$variables['recoveryOutboxEventId'] = $recoveryEventId.ToString('D')

$capacityStatuses = @(
    [pscustomobject]@{ Name = 'Pending'; Status = 'Pending'; Day = 21; Occupies = $true },
    [pscustomobject]@{ Name = 'Reserved'; Status = 'Reserved'; Day = 22; Occupies = $true },
    [pscustomobject]@{ Name = 'Confirmed'; Status = 'Confirmed'; Day = 23; Occupies = $true },
    [pscustomobject]@{ Name = 'InProgress'; Status = 'InProgress'; Day = 24; Occupies = $true },
    [pscustomobject]@{ Name = 'Completed'; Status = 'Completed'; Day = 25; Occupies = $false },
    [pscustomobject]@{ Name = 'Cancelled'; Status = 'Cancelled'; Day = 26; Occupies = $false },
    [pscustomobject]@{ Name = 'NoShow'; Status = 'NoShow'; Day = 27; Occupies = $false }
)
foreach ($capacityCase in $capacityStatuses) {
    $capacityDay = $capacityCase.Day - 20
    $slotStart = "2026-09-$($capacityDay.ToString('00'))T12:00:00+00:00"
    $slotEnd = "2026-09-$($capacityDay.ToString('00'))T12:30:00+00:00"
    for ($occupiedIndex = 1; $occupiedIndex -le 4; $occupiedIndex++) {
        $reservationId = [guid]::NewGuid()
        $reservationPublicId = [guid]::NewGuid()
        $bookingReference = [guid]::NewGuid()
        $orderGuid = [guid]::NewGuid()
        $capacitySql = Get-CloneSql $BusinessDatabase 'AppointmentReservations' "Id='$sourceReservationId'" @{
            Id = "CAST('$reservationId' AS uniqueidentifier)"
            PublicId = "CAST('$reservationPublicId' AS uniqueidentifier)"
            CustomerBookingReference = "CAST('$bookingReference' AS uniqueidentifier)"
            OrderGuid = "CAST('$orderGuid' AS uniqueidentifier)"
            RequestHash = "LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256', '$bookingReference'), 2))"
            RequestedSlotStartUtc = "CAST('$slotStart' AS datetimeoffset)"
            RequestedSlotEndUtc = "CAST('$slotEnd' AS datetimeoffset)"
            Status = "N'$($capacityCase.Status)'"
            StatusSequence = 'CAST(0 AS bigint)'
            StatusChangedAtUtc = "CAST('$slotStart' AS datetimeoffset)"
        }
        Invoke-Sql $BusinessDatabase $capacitySql
    }

    $requestBookingReference = [guid]::NewGuid()
    $requestOrderGuid = [guid]::NewGuid()
    $variables["capacity$($capacityCase.Name)SlotUtc"] = $slotStart
    $variables["capacity$($capacityCase.Name)BookingReference"] = $requestBookingReference.ToString('D')
    $variables["capacity$($capacityCase.Name)OrderGuid"] = $requestOrderGuid.ToString('D')
}

$variables['capacityBranchId'] = '22222222-2222-4222-8222-222222222222'
$variables['capacityOfferingId'] = '44444444-4444-4444-8444-444444444444'

$resolvedVariablesPath = if ([IO.Path]::IsPathRooted($VariablesPath)) {
    $VariablesPath
} else {
    Join-Path $solution $VariablesPath
}
[IO.File]::WriteAllText(
    $resolvedVariablesPath,
    ($variables | ConvertTo-Json -Depth 3),
    [Text.UTF8Encoding]::new($false))

[ordered]@{
    customerDatabase = $CustomerDatabase
    businessDatabase = $BusinessDatabase
    fixtures = $FixtureCount
    variablesPath = $resolvedVariablesPath
} | ConvertTo-Json
