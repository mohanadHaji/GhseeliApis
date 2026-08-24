#requires -Version 5.1

$ErrorActionPreference = 'Stop'
$server = '(localdb)\MSSQLLocalDB'
$suffix = [guid]::NewGuid().ToString('N')
$sourceDatabase = "Step14VerifierSelfTestSource_$suffix"
$targetDatabase = "Step14VerifierSelfTestTarget_$suffix"
$variablesPath = Join-Path $PSScriptRoot "artifacts\step14-verifier-selftest-$suffix.local.json"
$verifierPath = Join-Path $PSScriptRoot 'Verify-Step14DatabaseInvariants.ps1'
$initializerPath = Join-Path $PSScriptRoot 'Initialize-Step14Fixtures.ps1'
$shell = (Get-Process -Id $PID).Path

function Invoke-Sql([string]$database, [string]$sql) {
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$server;Database=$database;Integrated Security=true;TrustServerCertificate=true")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $sql
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}

function Scalar([string]$database, [string]$sql) {
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$server;Database=$database;Integrated Security=true;TrustServerCertificate=true")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $sql
        return $command.ExecuteScalar()
    }
    finally { $connection.Dispose() }
}

function Invoke-Verifier([bool]$expectSuccess) {
    $output = & $shell -NoProfile -ExecutionPolicy Bypass -File $verifierPath `
        -CustomerDatabase $targetDatabase `
        -SourceCustomerDatabase $sourceDatabase `
        -VariablesPath $variablesPath 2>&1
    $succeeded = $LASTEXITCODE -eq 0
    if ($succeeded -ne $expectSuccess) {
        throw "Verifier success was '$succeeded', expected '$expectSuccess'. Output: $output"
    }
}

function Assert-VerifierRejects([string]$name, [scriptblock]$corrupt, [scriptblock]$restore) {
    & $corrupt
    try {
        Invoke-Verifier $false
        Write-Host "[PASS] $name"
    }
    finally {
        & $restore
    }
}

$createdDatabases = [Collections.Generic.List[string]]::new()
try {
    $sameNameRejected = $false
    try {
        & $initializerPath `
            -SourceCustomerDatabase $sourceDatabase `
            -CustomerDatabase $sourceDatabase `
            -VariablesPath $variablesPath | Out-Null
    }
    catch {
        $sameNameRejected =
            $_.Exception.Message -match 'must be different'
    }
    if (-not $sameNameRejected) {
        throw 'Initializer did not reject normalized identical database names before SQL.'
    }
    Write-Host '[PASS] Initializer rejects identical source and target names before SQL'

    Invoke-Sql master "CREATE DATABASE [$sourceDatabase];"
    $createdDatabases.Add($sourceDatabase)
    Invoke-Sql master "CREATE DATABASE [$targetDatabase];"
    $createdDatabases.Add($targetDatabase)
    Invoke-Sql $sourceDatabase @"
CREATE TABLE Payments (Id uniqueidentifier NOT NULL PRIMARY KEY, Value nvarchar(100) NULL);
INSERT INTO Payments (Id,Value) VALUES
('11111111-1111-1111-1111-111111111111',N'legacy-preserved');
"@
    Invoke-Sql $targetDatabase @"
CREATE TABLE Payments (Id uniqueidentifier NOT NULL PRIMARY KEY, Value nvarchar(100) NULL);
INSERT INTO Payments (Id,Value) VALUES
('11111111-1111-1111-1111-111111111111',N'legacy-preserved');
CREATE TABLE CustomerBookings (
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 PublicReference uniqueidentifier NOT NULL,
 OrderGuid uniqueidentifier NOT NULL,
 UserId uniqueidentifier NOT NULL,
 OwnerDeviceId uniqueidentifier NOT NULL,
 BusinessReservationId uniqueidentifier NOT NULL,
 BusinessWorkOrderId uniqueidentifier NOT NULL,
 Status nvarchar(32) NOT NULL,
 GrandTotal decimal(18,2) NOT NULL,
 Currency nvarchar(3) NOT NULL,
 BaseSubtotal decimal(18,2) NOT NULL,
 AddonSubtotal decimal(18,2) NOT NULL,
 ItemSubtotal decimal(18,2) NOT NULL,
 ServiceFee decimal(18,2) NOT NULL,
 TaxableSubtotal decimal(18,2) NOT NULL,
 Tax decimal(18,2) NOT NULL,
 IsPaid bit NOT NULL,
 PaymentState nvarchar(16) NOT NULL
);
CREATE TABLE CustomerPayments (
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 CustomerBookingId uniqueidentifier NOT NULL,
 UserId uniqueidentifier NOT NULL,
 OwnerDeviceId uniqueidentifier NOT NULL,
 Amount decimal(18,2) NOT NULL,
 MinorAmount bigint NOT NULL,
 Currency nvarchar(3) NOT NULL,
 Status int NOT NULL,
 IdempotencyKey nvarchar(128) NOT NULL,
 RequestHash char(64) NOT NULL,
 StripeIdempotencyKey nvarchar(200) NOT NULL,
 PaymentIntentId nvarchar(200) NULL,
 ProviderStatus nvarchar(64) NULL,
 ClientSecret nvarchar(500) NULL,
 ProviderPublishableKey nvarchar(200) NULL,
 IntentLeaseOwnerToken uniqueidentifier NULL,
 IntentLeaseExpiresAtUtc datetimeoffset NULL
);
CREATE TABLE CustomerPaymentIdempotencyRecords (
 Id uniqueidentifier NOT NULL PRIMARY KEY,
 CustomerPaymentId uniqueidentifier NOT NULL,
 UserId uniqueidentifier NOT NULL,
 OwnerDeviceId uniqueidentifier NOT NULL,
 IdempotencyKey nvarchar(128) NOT NULL,
 RequestHash char(64) NOT NULL
);
CREATE TABLE StripeWebhookEvents (
 EventId nvarchar(200) NOT NULL PRIMARY KEY,
 EventType nvarchar(100) NOT NULL,
 BodyHash char(64) NOT NULL,
 State nvarchar(16) NOT NULL,
 DispositionReason nvarchar(100) NULL,
 CustomerPaymentId uniqueidentifier NULL
);
"@

    $userId = [guid]::NewGuid()
    $deviceId = [guid]::NewGuid()
    $bookingIds = @()
    $bookingReferences = @()
    for ($index = 0; $index -lt 23; $index++) {
        $bookingId = [guid]::NewGuid()
        $reference = [guid]::NewGuid()
        $bookingIds += $bookingId
        $bookingReferences += $reference
        Invoke-Sql $targetDatabase @"
INSERT INTO CustomerBookings
(Id,PublicReference,OrderGuid,UserId,OwnerDeviceId,BusinessReservationId,
 BusinessWorkOrderId,Status,GrandTotal,Currency,BaseSubtotal,AddonSubtotal,
 ItemSubtotal,ServiceFee,TaxableSubtotal,Tax,IsPaid,PaymentState)
VALUES
('$bookingId','$reference','$([guid]::NewGuid())','$userId','$deviceId',
 '$([guid]::NewGuid())','$([guid]::NewGuid())',N'Pending',10,N'ILS',
 10,0,0,0,10,0,0,N'Pending');
"@
    }

    $statuses = @(0,1,2,3,0,0,0,1,0,0,1)
    $fixtures = @()
    for ($index = 0; $index -lt 11; $index++) {
        $paymentId = [guid]::NewGuid()
        $status = $statuses[$index]
        $paymentState = @('Pending','Completed','Failed','Refunded')[$status]
        $isPaid = if ($status -eq 1) { 1 } else { 0 }
        $key = "fixture-$index"
        $requestHash = ([char](65 + $index)).ToString() * 64
        $intentId = "pi_fixture_$index"
        $providerStatus = "provider-$index"
        Invoke-Sql $targetDatabase @"
UPDATE CustomerBookings
SET IsPaid=$isPaid,PaymentState=N'$paymentState'
WHERE Id='$($bookingIds[$index])';
INSERT INTO CustomerPayments
(Id,CustomerBookingId,UserId,OwnerDeviceId,Amount,MinorAmount,Currency,Status,
 IdempotencyKey,RequestHash,StripeIdempotencyKey,PaymentIntentId,ProviderStatus,
 ClientSecret,ProviderPublishableKey,IntentLeaseOwnerToken,IntentLeaseExpiresAtUtc)
VALUES
('$paymentId','$($bookingIds[$index])','$userId','$deviceId',10,1000,N'ILS',
 $status,N'$key','$requestHash',N'provider-key-$index',N'$intentId',
 N'$providerStatus',NULL,NULL,NULL,NULL);
"@
        if ($index -eq 0) {
            Invoke-Sql $targetDatabase @"
INSERT INTO CustomerPaymentIdempotencyRecords
(Id,CustomerPaymentId,UserId,OwnerDeviceId,IdempotencyKey,RequestHash)
VALUES
('$([guid]::NewGuid())','$paymentId','$userId','$deviceId',N'$key','$requestHash');
"@
        }
        $fixtures += [ordered]@{
            id = $paymentId.ToString('D')
            bookingId = $bookingIds[$index].ToString('D')
            bookingReference = $bookingReferences[$index].ToString('D')
            status = $status
            providerStatus = $providerStatus
            isPaid = $status -eq 1
            paymentState = $paymentState
            hasIdempotency = $index -eq 0
            idempotencyKey = $key
            requestHash = $requestHash
            paymentIntentId = $intentId
        }
    }

    $snapshotHash = [string](Scalar $targetDatabase @"
SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', (
    SELECT Id, PublicReference, OrderGuid, UserId, OwnerDeviceId,
           BusinessReservationId, BusinessWorkOrderId, Status,
           GrandTotal, Currency, BaseSubtotal, AddonSubtotal, ItemSubtotal,
           ServiceFee, TaxableSubtotal, Tax
    FROM CustomerBookings ORDER BY Id FOR JSON PATH
)), 2)
"@)
    Invoke-Sql $targetDatabase @"
INSERT INTO CustomerPaymentIdempotencyRecords
(Id,CustomerPaymentId,UserId,OwnerDeviceId,IdempotencyKey,RequestHash)
VALUES
('$([guid]::NewGuid())','$($fixtures[0].id)','$userId','$deviceId',
 N'new-key-selftest',N'$($fixtures[0].requestHash)');
"@
    $variables = [ordered]@{
        immutableBookingSnapshotHash = $snapshotHash
        expectedPaymentFixtures = $fixtures
        expectedBookingFixtureIds = @($bookingIds | ForEach-Object {
            $_.ToString('D')
        })
        baselineCustomerPaymentCount = 0
        baselineIdempotencyCount = 0
        baselineWebhookCount = 0
        existingPaymentId = $fixtures[0].id
        secondPayableBookingId = $bookingReferences[22].ToString('D')
        runStamp = 'selftest'
        expectedWebhookReceipts = @()
    }
    [IO.Directory]::CreateDirectory((Split-Path $variablesPath)) | Out-Null
    [IO.File]::WriteAllText(
        $variablesPath,
        ($variables | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))

    Invoke-Verifier $true
    Write-Host '[PASS] Verifier accepts the exact isolated baseline'

    $firstBookingId = $bookingIds[0]
    Assert-VerifierRejects 'Verifier rejects an incorrect Pending booking mapping' {
        Invoke-Sql $targetDatabase `
            "UPDATE CustomerBookings SET PaymentState=N'Failed' WHERE Id='$firstBookingId';"
    } {
        Invoke-Sql $targetDatabase `
            "UPDATE CustomerBookings SET PaymentState=N'Pending' WHERE Id='$firstBookingId';"
    }
    Assert-VerifierRejects 'Verifier compares legacy row content, not only count' {
        Invoke-Sql $targetDatabase @"
UPDATE Payments SET Value=N'corrupt'
WHERE Id='11111111-1111-1111-1111-111111111111';
"@
    } {
        Invoke-Sql $targetDatabase @"
UPDATE Payments SET Value=N'legacy-preserved'
WHERE Id='11111111-1111-1111-1111-111111111111';
"@
    }
    Assert-VerifierRejects 'Verifier rejects an unexpected webhook receipt' {
        Invoke-Sql $targetDatabase @"
INSERT INTO StripeWebhookEvents
(EventId,EventType,BodyHash,State,DispositionReason,CustomerPaymentId)
VALUES (N'evt_unexpected',N'customer.created',
N'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
N'Completed',NULL,NULL);
"@
    } {
        Invoke-Sql $targetDatabase `
            "DELETE FROM StripeWebhookEvents WHERE EventId=N'evt_unexpected';"
    }
    $firstPaymentId = [guid]$fixtures[0].id
    Assert-VerifierRejects 'Verifier rejects a corrupted fixture identity field' {
        Invoke-Sql $targetDatabase `
            "UPDATE CustomerPayments SET ProviderStatus=N'wrong' WHERE Id='$firstPaymentId';"
    } {
        Invoke-Sql $targetDatabase `
            "UPDATE CustomerPayments SET ProviderStatus=N'provider-0' WHERE Id='$firstPaymentId';"
    }

    Invoke-Verifier $true
    Write-Host '[PASS] Verifier returns to green after targeted restoration'
}
finally {
    Remove-Item -LiteralPath $variablesPath -Force -ErrorAction SilentlyContinue
    $databasesToRemove = $createdDatabases.ToArray()
    [array]::Reverse($databasesToRemove)
    foreach ($database in $databasesToRemove) {
        Invoke-Sql master @"
IF DB_ID(N'$database') IS NOT NULL
BEGIN
    ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$database];
END
"@
    }
}
