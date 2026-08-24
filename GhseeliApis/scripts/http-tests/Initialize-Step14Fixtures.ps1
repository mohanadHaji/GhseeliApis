param(
    [Parameter(Mandatory = $true)]
    [string]$SourceCustomerDatabase,
    [Parameter(Mandatory = $true)]
    [string]$CustomerDatabase,
    [string]$VariablesPath =
        '.\scripts\http-tests\artifacts\step-14.variables.local.json'
)

$ErrorActionPreference = 'Stop'
$server = '(localdb)\MSSQLLocalDB'
$solution = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifacts = Join-Path $PSScriptRoot 'artifacts'

foreach ($name in @($SourceCustomerDatabase, $CustomerDatabase)) {
    if ($name -notmatch '^[A-Za-z0-9_]+$') {
        throw 'Unsafe database name.'
    }
}
if ([string]::Equals(
    $SourceCustomerDatabase,
    $CustomerDatabase,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source and target customer database names must be different.'
}

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

function Open-Connection([string]$database) {
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$server;Database=$database;Integrated Security=true;TrustServerCertificate=true")
    $connection.Open()
    return $connection
}

function Invoke-Sql([string]$database, [string]$sql) {
    $connection = Open-Connection $database
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $sql
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}

function Invoke-Scalar([string]$database, [string]$sql) {
    $connection = Open-Connection $database
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $sql
        return $command.ExecuteScalar()
    }
    finally { $connection.Dispose() }
}

function Copy-Database([string]$source, [string]$target) {
    $backup = Join-Path $artifacts "$target.bak"
    $escapedBackup = $backup.Replace("'", "''")
    Invoke-Sql master @"
IF DB_ID(N'$target') IS NOT NULL
BEGIN
    ALTER DATABASE [$target] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$target];
END;
BACKUP DATABASE [$source] TO DISK=N'$escapedBackup' WITH INIT, COPY_ONLY;
"@

    $connection = Open-Connection master
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = "RESTORE FILELISTONLY FROM DISK=N'$escapedBackup'"
        $reader = $command.ExecuteReader()
        $files = @()
        while ($reader.Read()) {
            $files += [pscustomobject]@{
                LogicalName = [string]$reader['LogicalName']
                Type = [string]$reader['Type']
            }
        }
        $reader.Close()
        $dataRoot = [string](Invoke-Scalar master `
            "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000))")
        if ([string]::IsNullOrWhiteSpace($dataRoot)) {
            $dataRoot = Split-Path -Parent ([string](Invoke-Scalar master `
                "SELECT physical_name FROM sys.master_files WHERE database_id=1 AND file_id=1"))
        }
        $moves = foreach ($file in $files) {
            $extension = if ($file.Type -eq 'L') { '.ldf' } else { '.mdf' }
            $path = Join-Path $dataRoot ($target + $extension)
            ", MOVE N'$($file.LogicalName.Replace("'", "''"))' TO N'$($path.Replace("'", "''"))'"
        }
        Invoke-Sql master `
            "RESTORE DATABASE [$target] FROM DISK=N'$escapedBackup' WITH REPLACE$($moves -join '')"
    }
    finally { $connection.Dispose() }
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
    finally { $connection.Dispose() }
    $select = foreach ($column in $columns) {
        if ($replacements.ContainsKey($column)) { $replacements[$column] }
        else { "[$column]" }
    }
    $insertColumns = $columns.ForEach({ "[$_]" }) -join ','
    return "INSERT INTO dbo.[$table] ($insertColumns) SELECT $($select -join ',') FROM dbo.[$table] WHERE $where;"
}

function New-Base64Url([byte[]]$bytes) {
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-RandomBytes([int]$count) {
    $bytes = [byte[]]::new($count)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) }
    finally { $generator.Dispose() }
    return $bytes
}

function Get-Sha256([byte[]]$bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return $sha.ComputeHash($bytes) }
    finally { $sha.Dispose() }
}

function ConvertTo-Hex([byte[]]$bytes) {
    return -join $bytes.ForEach({ $_.ToString('X2') })
}

function New-FixtureToken {
    return New-Base64Url (Get-RandomBytes 32)
}

function New-Jwt(
    [guid]$userId,
    [string]$secret,
    [string]$issuer,
    [string]$audience,
    [string]$role = 'User',
    [long]$notBeforeOffsetSeconds = -30,
    [long]$expiresOffsetSeconds = 7200
) {
    $header = New-Base64Url ([Text.Encoding]::UTF8.GetBytes(
        '{"alg":"HS256","typ":"JWT"}'))
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $payloadObject = [ordered]@{
        'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' =
            $userId.ToString('D')
        'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' = $role
        iss = $issuer
        aud = $audience
        nbf = $now + $notBeforeOffsetSeconds
        iat = $now
        exp = $now + $expiresOffsetSeconds
    }
    $payload = New-Base64Url ([Text.Encoding]::UTF8.GetBytes(
        ($payloadObject | ConvertTo-Json -Compress)))
    $unsigned = "$header.$payload"
    $hmac = [Security.Cryptography.HMACSHA256]::new(
        [Text.Encoding]::UTF8.GetBytes($secret))
    try {
        $signature = New-Base64Url ($hmac.ComputeHash(
            [Text.Encoding]::ASCII.GetBytes($unsigned)))
    }
    finally { $hmac.Dispose() }
    return "$unsigned.$signature"
}

Copy-Database $SourceCustomerDatabase $CustomerDatabase

$env:ConnectionStrings__RemoteTest =
    "Server=$server;Database=$CustomerDatabase;Integrated Security=true;TrustServerCertificate=true"
try {
    & dotnet ef database update `
        --project (Join-Path $solution 'GhseeliApis\GhseeliApis.csproj') `
        --startup-project (Join-Path $solution 'GhseeliApis\GhseeliApis.csproj') `
        --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Customer migration failed.' }
}
finally { Remove-Item Env:\ConnectionStrings__RemoteTest -ErrorAction SilentlyContinue }

$baselineCustomerPaymentCount = [int](Invoke-Scalar $CustomerDatabase `
    'SELECT COUNT(*) FROM CustomerPayments')
$baselineIdempotencyCount = [int](Invoke-Scalar $CustomerDatabase `
    'SELECT COUNT(*) FROM CustomerPaymentIdempotencyRecords')
$baselineWebhookCount = [int](Invoke-Scalar $CustomerDatabase `
    'SELECT COUNT(*) FROM StripeWebhookEvents')

$sourceBookingId = [guid](Invoke-Scalar $CustomerDatabase `
    "SELECT TOP (1) Id FROM CustomerBookings WHERE GrandTotal > 0 AND Currency='ILS' ORDER BY CreatedAtUtc")
$ownerUserId = [guid](Invoke-Scalar $CustomerDatabase `
    "SELECT UserId FROM CustomerBookings WHERE Id='$sourceBookingId'")
$userRoleId = [guid](Invoke-Scalar $CustomerDatabase `
    "SELECT Id FROM AspNetRoles WHERE NormalizedName='USER'")

$secondUserId = [guid]::NewGuid()
$secondSuffix = $secondUserId.ToString('N')
$cloneUserSql = Get-CloneSql $CustomerDatabase 'AspNetUsers' "Id='$ownerUserId'" @{
    Id = "CAST('$secondUserId' AS uniqueidentifier)"
    UserName = "N'step14-second-$secondSuffix'"
    NormalizedUserName = "N'STEP14-SECOND-$($secondSuffix.ToUpperInvariant())'"
    Email = "N'step14-second-$secondSuffix@example.invalid'"
    NormalizedEmail = "N'STEP14-SECOND-$($secondSuffix.ToUpperInvariant())@EXAMPLE.INVALID'"
    FullName = "N'Step 14 Secondary Fixture'"
    SecurityStamp = "N'$([guid]::NewGuid().ToString('N'))'"
    ConcurrencyStamp = "N'$([guid]::NewGuid().ToString('N'))'"
}
Invoke-Sql $CustomerDatabase $cloneUserSql
Invoke-Sql $CustomerDatabase @"
INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES ('$secondUserId', '$userRoleId');
"@

$ownerToken = New-FixtureToken
$ownerOtherToken = New-FixtureToken
$secondToken = New-FixtureToken
$ownerDeviceId = [guid]::NewGuid()
$ownerOtherDeviceId = [guid]::NewGuid()
$secondDeviceId = [guid]::NewGuid()
$now = [DateTimeOffset]::UtcNow
$expires = $now.AddDays(2)

foreach ($device in @(
    @($ownerDeviceId, $ownerToken),
    @($ownerOtherDeviceId, $ownerOtherToken),
    @($secondDeviceId, $secondToken)
)) {
    $hash = Get-Sha256 ([Text.Encoding]::ASCII.GetBytes([string]$device[1]))
    $hashSql = '0x' + (ConvertTo-Hex $hash)
    $installationId = [guid]::NewGuid()
    Invoke-Sql $CustomerDatabase @"
INSERT INTO CustomerDevices
    (Id, InstallationId, Platform, AppVersion, TokenHash, CreatedAt, UpdatedAt,
     LastSeenAt, ExpiresAt)
VALUES
    ('$($device[0])', '$installationId', N'android', N'step14', $hashSql,
     '$($now.ToString('o'))', '$($now.ToString('o'))', NULL, '$($expires.ToString('o'))');
"@
}

$expiredDeviceToken = New-FixtureToken
$expiredDeviceId = [guid]::NewGuid()
$expiredHash = '0x' + (ConvertTo-Hex (Get-Sha256 (
    [Text.Encoding]::ASCII.GetBytes($expiredDeviceToken))))
Invoke-Sql $CustomerDatabase @"
INSERT INTO CustomerDevices
    (Id, InstallationId, Platform, AppVersion, TokenHash, CreatedAt, UpdatedAt,
     LastSeenAt, ExpiresAt)
VALUES
    ('$expiredDeviceId', '$([guid]::NewGuid())', N'android', N'step14-expired',
     $expiredHash, '$($now.AddDays(-3).ToString('o'))',
     '$($now.AddDays(-2).ToString('o'))', NULL, '$($now.AddDays(-1).ToString('o'))');
"@
$rotatedDeviceToken = New-FixtureToken

function Add-BookingFixture(
    [string]$status,
    [guid]$deviceId,
    [guid]$userId = $ownerUserId,
    [string]$currency = 'ILS',
    [Nullable[decimal]]$grandTotal = $null,
    [bool]$isPaid = $false,
    [string]$paymentState = 'Unpaid'
) {
    $id = [guid]::NewGuid()
    $reference = [guid]::NewGuid()
    $orderGuid = [guid]::NewGuid()
    $reservationId = [guid]::NewGuid()
    $workOrderId = [guid]::NewGuid()
    $replacements = @{
        Id = "CAST('$id' AS uniqueidentifier)"
        PublicReference = "CAST('$reference' AS uniqueidentifier)"
        OrderGuid = "CAST('$orderGuid' AS uniqueidentifier)"
        UserId = "CAST('$userId' AS uniqueidentifier)"
        OwnerDeviceId = "CAST('$deviceId' AS uniqueidentifier)"
        BusinessReservationId = "CAST('$reservationId' AS uniqueidentifier)"
        BusinessWorkOrderId = "CAST('$workOrderId' AS uniqueidentifier)"
        Status = "N'$status'"
        BusinessStatusSequence = 'CAST(0 AS bigint)'
        IsPaid = "CAST('$([int]$isPaid)' AS bit)"
        PaymentState = "N'$paymentState'"
        Currency = "N'$currency'"
        CreatedAtUtc = "CAST('$($now.ToString('o'))' AS datetimeoffset)"
    }
    if ($null -ne $grandTotal) {
        $replacements.GrandTotal = "CAST($grandTotal AS decimal(18,2))"
    }
    $sql = Get-CloneSql $CustomerDatabase 'CustomerBookings' `
        "Id='$sourceBookingId'" $replacements
    Invoke-Sql $CustomerDatabase $sql
    return [pscustomobject]@{ Id = $id; Reference = $reference }
}

$payable = Add-BookingFixture 'Pending' $ownerDeviceId
$secondPayable = Add-BookingFixture 'Confirmed' $ownerDeviceId
$inProgress = Add-BookingFixture 'InProgress' $ownerDeviceId
$completedBooking = Add-BookingFixture 'Completed' $ownerDeviceId
$cancelled = Add-BookingFixture 'Cancelled' $ownerDeviceId
$noShow = Add-BookingFixture 'NoShow' $ownerDeviceId
$zeroTotal = Add-BookingFixture 'Pending' $ownerDeviceId $ownerUserId 'ILS' 0
$negativeTotal = Add-BookingFixture 'Pending' $ownerDeviceId $ownerUserId 'ILS' -1
$malformedCurrency = Add-BookingFixture 'Pending' $ownerDeviceId $ownerUserId 'ils'
$unsupportedCurrency = Add-BookingFixture 'Pending' $ownerDeviceId $ownerUserId 'GBP'
$alreadyPaid = Add-BookingFixture 'Pending' $ownerDeviceId $ownerUserId 'ILS' $null $true 'Completed'
$alreadyRefunded = Add-BookingFixture 'Pending' $ownerDeviceId $ownerUserId 'ILS' $null $false 'Refunded'

function Add-PaymentFixture(
    [object]$booking,
    [int]$status,
    [string]$providerStatus,
    [bool]$withConfirmation = $false,
    [bool]$withIdempotency = $false,
    [string]$chargeId = ''
) {
    $paymentId = [guid]::NewGuid()
    $key = "step14-existing-$($paymentId.ToString('N'))"
    $hash = ConvertTo-Hex (Get-Sha256 ([Text.Encoding]::UTF8.GetBytes(
        "$($booking.Reference.ToString('D'))`nCard")))
    $amount = [decimal](Invoke-Scalar $CustomerDatabase `
        "SELECT GrandTotal FROM CustomerBookings WHERE Id='$($booking.Id)'")
    $currency = [string](Invoke-Scalar $CustomerDatabase `
        "SELECT Currency FROM CustomerBookings WHERE Id='$($booking.Id)'")
    $minorAmount = [decimal]::ToInt64($amount * 100)
    $intentId = "pi_step14_fixture_$($paymentId.ToString('N'))"
    $clientSecret = if ($withConfirmation) {
        "'${intentId}_secret_local_fixture'"
    } else { 'NULL' }
    $publishableKey = if ($withConfirmation) { "'pk_test_step14_local_fixture'" } else { 'NULL' }
    $chargeValue = if ([string]::IsNullOrWhiteSpace($chargeId)) { 'NULL' } else { "'$chargeId'" }
    Invoke-Sql $CustomerDatabase @"
INSERT INTO CustomerPayments
    (Id, CustomerBookingId, UserId, OwnerDeviceId, Amount, MinorAmount, Currency,
     Method, Status, IdempotencyKey, RequestHash, StripeIdempotencyKey,
     PaymentIntentId, ChargeId, ProviderStatus, ClientSecret, ProviderPublishableKey,
     IntentLeaseOwnerToken, IntentLeaseExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc)
VALUES
    ('$paymentId', '$($booking.Id)', '$ownerUserId', '$ownerDeviceId',
     $amount, $minorAmount, '$currency', 0, $status, '$key', '$hash',
     'ghseeli-fixture-$($paymentId.ToString('N'))', '$intentId', $chargeValue,
     '$providerStatus', $clientSecret, $publishableKey, NULL, NULL,
     '$($now.ToString('o'))', '$($now.ToString('o'))');
"@
    $expectedPaymentState = @('Pending', 'Completed', 'Failed', 'Refunded')[$status]
    $expectedIsPaid = if ($status -eq 1) { 1 } else { 0 }
    Invoke-Sql $CustomerDatabase @"
UPDATE CustomerBookings
SET IsPaid=$expectedIsPaid, PaymentState=N'$expectedPaymentState'
WHERE Id='$($booking.Id)';
"@
    if ($withIdempotency) {
        Invoke-Sql $CustomerDatabase @"
INSERT INTO CustomerPaymentIdempotencyRecords
    (Id, CustomerPaymentId, UserId, OwnerDeviceId, IdempotencyKey, RequestHash, CreatedAtUtc)
VALUES
    ('$([guid]::NewGuid())', '$paymentId', '$ownerUserId', '$ownerDeviceId',
     '$key', '$hash', '$($now.ToString('o'))');
"@
    }
    return [pscustomobject]@{
        Id = $paymentId
        BookingId = $booking.Id
        BookingReference = $booking.Reference
        Key = $key
        RequestHash = $hash
        IntentId = $intentId
        ChargeId = $chargeId
        Amount = $amount
        MinorAmount = $minorAmount
        Currency = $currency
        Status = $status
        ProviderStatus = $providerStatus
        HasIdempotency = $withIdempotency
        ExpectedIsPaid = $status -eq 1
        ExpectedPaymentState = $expectedPaymentState
    }
}

$pendingPayment = Add-PaymentFixture (Add-BookingFixture 'Pending' $ownerDeviceId) 0 `
    'requires_payment_method' $true $true
$completedPayment = Add-PaymentFixture (
    Add-BookingFixture 'Pending' $ownerDeviceId $ownerUserId 'ILS' $null $true 'Completed'
) 1 'payment_intent.succeeded'
$failedPayment = Add-PaymentFixture (Add-BookingFixture 'Pending' $ownerDeviceId) 2 `
    'payment_intent.payment_failed'
$refundedPayment = Add-PaymentFixture (
    Add-BookingFixture 'Pending' $ownerDeviceId $ownerUserId 'ILS' $null $false 'Refunded'
) 3 'charge.refunded' $false $false "ch_step14_refunded_$([guid]::NewGuid().ToString('N'))"
$webhookPendingPayment = Add-PaymentFixture (Add-BookingFixture 'Pending' $ownerDeviceId) 0 `
    'requires_payment_method' $true
$webhookFailurePayment = Add-PaymentFixture (Add-BookingFixture 'Pending' $ownerDeviceId) 0 `
    'requires_payment_method' $true
$webhookCancelPayment = Add-PaymentFixture (Add-BookingFixture 'Pending' $ownerDeviceId) 0 `
    'requires_payment_method' $true
$webhookRefundPayment = Add-PaymentFixture (
    Add-BookingFixture 'Pending' $ownerDeviceId $ownerUserId 'ILS' $null $true 'Completed'
) 1 'payment_intent.succeeded' $false $false "ch_step14_refund_$([guid]::NewGuid().ToString('N'))"
$webhookPartialRefundPayment = Add-PaymentFixture (
    Add-BookingFixture 'Pending' $ownerDeviceId $ownerUserId 'ILS' $null $true 'Completed'
) 1 'payment_intent.succeeded' $false $false "ch_step14_partial_$([guid]::NewGuid().ToString('N'))"
$webhookDeferredPayment = Add-PaymentFixture (Add-BookingFixture 'Pending' $ownerDeviceId) 0 `
    'requires_payment_method' $true
$webhookAnonymousPayment = Add-PaymentFixture (Add-BookingFixture 'Pending' $ownerDeviceId) 0 `
    'requires_payment_method' $true

$immutableBookingSnapshotHash = [string](Invoke-Scalar $CustomerDatabase @"
SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', (
    SELECT Id, PublicReference, OrderGuid, UserId, OwnerDeviceId,
           BusinessReservationId, BusinessWorkOrderId, Status,
           GrandTotal, Currency, BaseSubtotal, AddonSubtotal, ItemSubtotal,
           ServiceFee, TaxableSubtotal, Tax
    FROM CustomerBookings
    ORDER BY Id
    FOR JSON PATH
)), 2)
"@)

$jwtSecret = New-Base64Url (Get-RandomBytes 64)
$issuer = 'GhseeliApis'
$audience = 'GhseeliApis'
$webhookSecret = "whsec_$(New-Base64Url (Get-RandomBytes 48))"
$fixtureRunStamp = $now.ToString('yyyyMMddHHmmssfff')
$unknownBookingId = [guid]::NewGuid()
$unknownPaymentId = [guid]::NewGuid()
$paymentFixtures = @(
    $pendingPayment,
    $completedPayment,
    $failedPayment,
    $refundedPayment,
    $webhookPendingPayment,
    $webhookFailurePayment,
    $webhookCancelPayment,
    $webhookRefundPayment,
    $webhookPartialRefundPayment,
    $webhookDeferredPayment,
    $webhookAnonymousPayment
)
$bookingFixtures = @(
    $payable,
    $secondPayable,
    $inProgress,
    $completedBooking,
    $cancelled,
    $noShow,
    $zeroTotal,
    $negativeTotal,
    $malformedCurrency,
    $unsupportedCurrency,
    $alreadyPaid,
    $alreadyRefunded
) + @($paymentFixtures | ForEach-Object {
    [pscustomobject]@{ Id = $_.BookingId; Reference = $_.BookingReference }
})
$variables = [ordered]@{
    customerDatabase = $CustomerDatabase
    sourceCustomerDatabase = $SourceCustomerDatabase
    jwtSecret = $jwtSecret
    jwtIssuer = $issuer
    jwtAudience = $audience
    ownerJwt = New-Jwt $ownerUserId $jwtSecret $issuer $audience
    secondJwt = New-Jwt $secondUserId $jwtSecret $issuer $audience
    expiredJwt = New-Jwt $ownerUserId $jwtSecret $issuer $audience 'User' -7200 -3600
    wrongRoleJwt = New-Jwt $ownerUserId $jwtSecret $issuer $audience 'Admin'
    malformedJwt = 'not.a.valid.jwt'
    ownerDeviceToken = $ownerToken
    ownerOtherDeviceToken = $ownerOtherToken
    secondDeviceToken = $secondToken
    expiredDeviceToken = $expiredDeviceToken
    rotatedDeviceToken = $rotatedDeviceToken
    malformedDeviceToken = 'not valid device token'
    runStamp = $fixtureRunStamp
    correlationId = "step14-$fixtureRunStamp"
    unknownBookingId = $unknownBookingId.ToString('D')
    unknownPaymentId = $unknownPaymentId.ToString('D')
    ownerUserId = $ownerUserId.ToString('D')
    secondUserId = $secondUserId.ToString('D')
    ownerDeviceId = $ownerDeviceId.ToString('D')
    ownerOtherDeviceId = $ownerOtherDeviceId.ToString('D')
    secondDeviceId = $secondDeviceId.ToString('D')
    payableBookingId = $payable.Reference.ToString('D')
    secondPayableBookingId = $secondPayable.Reference.ToString('D')
    inProgressBookingId = $inProgress.Reference.ToString('D')
    completedBookingId = $completedBooking.Reference.ToString('D')
    cancelledBookingId = $cancelled.Reference.ToString('D')
    noShowBookingId = $noShow.Reference.ToString('D')
    zeroTotalBookingId = $zeroTotal.Reference.ToString('D')
    negativeTotalBookingId = $negativeTotal.Reference.ToString('D')
    malformedCurrencyBookingId = $malformedCurrency.Reference.ToString('D')
    unsupportedCurrencyBookingId = $unsupportedCurrency.Reference.ToString('D')
    alreadyPaidBookingId = $alreadyPaid.Reference.ToString('D')
    alreadyRefundedBookingId = $alreadyRefunded.Reference.ToString('D')
    existingPaymentId = $pendingPayment.Id.ToString('D')
    existingPaymentBookingId = $pendingPayment.BookingReference.ToString('D')
    existingPaymentIdempotencyKey = $pendingPayment.Key
    completedPaymentId = $completedPayment.Id.ToString('D')
    completedPaymentBookingId = $completedPayment.BookingReference.ToString('D')
    failedPaymentId = $failedPayment.Id.ToString('D')
    failedPaymentBookingId = $failedPayment.BookingReference.ToString('D')
    refundedPaymentId = $refundedPayment.Id.ToString('D')
    refundedPaymentBookingId = $refundedPayment.BookingReference.ToString('D')
    webhookPendingPaymentId = $webhookPendingPayment.Id.ToString('D')
    webhookPendingBookingInternalId = $webhookPendingPayment.BookingId.ToString('D')
    webhookPendingBookingId = $webhookPendingPayment.BookingReference.ToString('D')
    webhookPendingIntentId = $webhookPendingPayment.IntentId
    webhookPendingMinorAmount = $webhookPendingPayment.MinorAmount
    webhookPendingCurrency = $webhookPendingPayment.Currency.ToLowerInvariant()
    webhookFailurePaymentId = $webhookFailurePayment.Id.ToString('D')
    webhookFailureBookingInternalId = $webhookFailurePayment.BookingId.ToString('D')
    webhookFailureBookingId = $webhookFailurePayment.BookingReference.ToString('D')
    webhookFailureIntentId = $webhookFailurePayment.IntentId
    webhookFailureMinorAmount = $webhookFailurePayment.MinorAmount
    webhookFailureCurrency = $webhookFailurePayment.Currency.ToLowerInvariant()
    webhookCancelPaymentId = $webhookCancelPayment.Id.ToString('D')
    webhookCancelBookingInternalId = $webhookCancelPayment.BookingId.ToString('D')
    webhookCancelBookingId = $webhookCancelPayment.BookingReference.ToString('D')
    webhookCancelIntentId = $webhookCancelPayment.IntentId
    webhookCancelMinorAmount = $webhookCancelPayment.MinorAmount
    webhookCancelCurrency = $webhookCancelPayment.Currency.ToLowerInvariant()
    webhookRefundPaymentId = $webhookRefundPayment.Id.ToString('D')
    webhookRefundBookingInternalId = $webhookRefundPayment.BookingId.ToString('D')
    webhookRefundBookingId = $webhookRefundPayment.BookingReference.ToString('D')
    webhookRefundIntentId = $webhookRefundPayment.IntentId
    webhookRefundChargeId = $webhookRefundPayment.ChargeId
    webhookRefundMinorAmount = $webhookRefundPayment.MinorAmount
    webhookRefundCurrency = $webhookRefundPayment.Currency.ToLowerInvariant()
    webhookPartialRefundPaymentId = $webhookPartialRefundPayment.Id.ToString('D')
    webhookPartialRefundBookingInternalId = $webhookPartialRefundPayment.BookingId.ToString('D')
    webhookPartialRefundBookingId = $webhookPartialRefundPayment.BookingReference.ToString('D')
    webhookPartialRefundIntentId = $webhookPartialRefundPayment.IntentId
    webhookPartialRefundChargeId = $webhookPartialRefundPayment.ChargeId
    webhookPartialRefundMinorAmount = $webhookPartialRefundPayment.MinorAmount
    webhookPartialRefundCurrency = $webhookPartialRefundPayment.Currency.ToLowerInvariant()
    webhookDeferredPaymentId = $webhookDeferredPayment.Id.ToString('D')
    webhookDeferredBookingInternalId = $webhookDeferredPayment.BookingId.ToString('D')
    webhookDeferredBookingId = $webhookDeferredPayment.BookingReference.ToString('D')
    webhookDeferredIntentId = $webhookDeferredPayment.IntentId
    webhookDeferredChargeId = "ch_step14_deferred_$([guid]::NewGuid().ToString('N'))"
    webhookDeferredMinorAmount = $webhookDeferredPayment.MinorAmount
    webhookDeferredCurrency = $webhookDeferredPayment.Currency.ToLowerInvariant()
    webhookAnonymousPaymentId = $webhookAnonymousPayment.Id.ToString('D')
    webhookAnonymousBookingInternalId = $webhookAnonymousPayment.BookingId.ToString('D')
    webhookAnonymousBookingId = $webhookAnonymousPayment.BookingReference.ToString('D')
    webhookAnonymousIntentId = $webhookAnonymousPayment.IntentId
    webhookAnonymousMinorAmount = $webhookAnonymousPayment.MinorAmount
    webhookAnonymousCurrency = $webhookAnonymousPayment.Currency.ToLowerInvariant()
    webhookSecret = $webhookSecret
    immutableBookingSnapshotHash = $immutableBookingSnapshotHash
    fixtureCreatedAtUtc = $now.ToString('o')
    baselineCustomerPaymentCount = $baselineCustomerPaymentCount
    baselineIdempotencyCount = $baselineIdempotencyCount
    baselineWebhookCount = $baselineWebhookCount
    expectedBookingFixtureIds = @($bookingFixtures | ForEach-Object {
        $_.Id.ToString('D')
    })
    expectedPaymentFixtures = @($paymentFixtures | ForEach-Object {
        $finalStatus = $_.Status
        $finalProviderStatus = $_.ProviderStatus
        if ($_.Id -in @(
            $webhookPendingPayment.Id,
            $webhookFailurePayment.Id,
            $webhookAnonymousPayment.Id
        )) {
            $finalStatus = 1
            $finalProviderStatus = 'payment_intent.succeeded'
        }
        elseif ($_.Id -eq $webhookCancelPayment.Id) {
            $finalStatus = 2
            $finalProviderStatus = 'payment_intent.canceled'
        }
        elseif ($_.Id -in @(
            $webhookRefundPayment.Id,
            $webhookDeferredPayment.Id
        )) {
            $finalStatus = 3
            $finalProviderStatus = 'charge.refunded'
        }
        [ordered]@{
            id = $_.Id.ToString('D')
            bookingId = $_.BookingId.ToString('D')
            bookingReference = $_.BookingReference.ToString('D')
            status = $_.Status
            providerStatus = $_.ProviderStatus
            isPaid = $_.ExpectedIsPaid
            paymentState = $_.ExpectedPaymentState
            hasIdempotency = $_.HasIdempotency
            idempotencyKey = $_.Key
            requestHash = $_.RequestHash
            paymentIntentId = $_.IntentId
            webhookFinalStatus = $finalStatus
            webhookFinalProviderStatus = $finalProviderStatus
            webhookFinalIsPaid = $finalStatus -eq 1
            webhookFinalPaymentState =
                @('Pending', 'Completed', 'Failed', 'Refunded')[$finalStatus]
        }
    })
}

$resolvedVariablesPath = if ([IO.Path]::IsPathRooted($VariablesPath)) {
    $VariablesPath
}
else { Join-Path $solution $VariablesPath }

$sourceManifestPath = Join-Path $solution `
    'scripts\http-tests\plans\step-14-payment-rebuild.manifest.json'
$runtimeManifestPath = Join-Path $artifacts `
    'step-14-payment-rebuild.runtime.local.json'
foreach ($fixtureName in @(
    'step-14-empty.request.txt',
    'step-14-malformed.request.txt',
    'step-14-null.request.txt'
)) {
    Copy-Item -LiteralPath (Join-Path (Split-Path $sourceManifestPath) $fixtureName) `
        -Destination (Join-Path $artifacts $fixtureName) -Force
}
$runtimeManifest = [IO.File]::ReadAllText($sourceManifestPath)
$runtimeManifest = $runtimeManifest.Replace(
    '"Authorization": "******"',
    '"Authorization": "Bearer {{var:ownerJwt}}"')
$runtimeManifest = $runtimeManifest.Replace(
    '{{env:GHSEELI_HTTP_TEST_OWNER_JWT}}',
    '{{var:ownerJwt}}')
$runtimeManifest = $runtimeManifest.Replace(
    '{{env:GHSEELI_HTTP_TEST_SECOND_JWT}}',
    '{{var:secondJwt}}')
$runtimeManifest = $runtimeManifest.Replace(
    '{{env:GHSEELI_HTTP_TEST_ADMIN_JWT}}',
    '{{var:ownerJwt}}')
$runtimeManifest = $runtimeManifest.Replace(
    '{{env:GHSEELI_HTTP_TEST_OWNER_DEVICE_TOKEN}}',
    '{{var:ownerDeviceToken}}')
$runtimeManifest = $runtimeManifest.Replace(
    '{{env:GHSEELI_HTTP_TEST_OWNER_OTHER_DEVICE_TOKEN}}',
    '{{var:ownerOtherDeviceToken}}')
$runtimeManifest = $runtimeManifest.Replace(
    '{{env:GHSEELI_HTTP_TEST_SECOND_DEVICE_TOKEN}}',
    '{{var:secondDeviceToken}}')
$runtimeManifest = $runtimeManifest.Replace(
    '{{env:GHSEELI_HTTP_TEST_PAYABLE_BOOKING_ID}}',
    '{{var:payableBookingId}}')
$runtimeManifest = $runtimeManifest.Replace(
    '{{env:GHSEELI_HTTP_TEST_SECOND_PAYABLE_BOOKING_ID}}',
    '{{var:secondPayableBookingId}}')
$runtimeManifest = $runtimeManifest.Replace(
    '{{env:GHSEELI_HTTP_TEST_EXISTING_PAYMENT_ID}}',
    '{{var:existingPaymentId}}')
$runtimeManifest = $runtimeManifest.Replace(
    '"runStamp": "{{gen:timestamp:yyyyMMddHHmmssfff}}"',
    '"runStamp": "{{var:runStamp}}"')
$runtimeManifest = $runtimeManifest.Replace(
    '"unknownBookingId": "{{gen:guid}}"',
    '"unknownBookingId": "{{var:unknownBookingId}}"')
$runtimeManifest = $runtimeManifest.Replace(
    '"unknownPaymentId": "{{gen:guid}}"',
    '"unknownPaymentId": "{{var:unknownPaymentId}}"')
$runtimeManifest = $runtimeManifest.Replace(
    '"Authorization": "Bearer {{var:ownerJwt}}", "Content-Type": "application/json", "X-Device-Token": "{{var:secondDeviceToken}}"',
    '"Authorization": "Bearer {{var:secondJwt}}", "Content-Type": "application/json", "X-Device-Token": "{{var:secondDeviceToken}}"')
$runtimeManifest = $runtimeManifest.Replace(
    '"Authorization": "Bearer {{var:ownerJwt}}", "X-Device-Token": "{{var:secondDeviceToken}}"',
    '"Authorization": "Bearer {{var:secondJwt}}", "X-Device-Token": "{{var:secondDeviceToken}}"')
function Resolve-FixtureManifestValue([object]$value) {
    if ($null -eq $value) { return $null }
    if ($value -is [string]) {
        $single = [regex]::Match($value, '^\{\{var:([^{}]+)\}\}$')
        if ($single.Success) {
            $name = $single.Groups[1].Value
            if (-not $variables.Contains($name)) {
                throw "Runtime manifest variable '$name' is not initialized."
            }
            return $variables[$name]
        }
        return [regex]::Replace($value, '\{\{var:([^{}]+)\}\}', {
            param($match)
            $name = $match.Groups[1].Value
            if (-not $variables.Contains($name)) {
                throw "Runtime manifest variable '$name' is not initialized."
            }
            [string]$variables[$name]
        })
    }
    if ($value -is [Collections.IDictionary]) {
        $resolved = [ordered]@{}
        foreach ($key in $value.Keys) {
            $resolved[[string]$key] = Resolve-FixtureManifestValue $value[$key]
        }
        return $resolved
    }
    if ($value -is [Collections.IEnumerable]) {
        return @($value | ForEach-Object { Resolve-FixtureManifestValue $_ })
    }
    if ($value -is [psobject]) {
        $resolved = [ordered]@{}
        foreach ($property in $value.PSObject.Properties) {
            $resolved[$property.Name] = Resolve-FixtureManifestValue $property.Value
        }
        return $resolved
    }
    return $value
}

$runtimeDocument = $runtimeManifest | ConvertFrom-Json
$expectedWebhookReceipts = @()
foreach ($scenario in @($runtimeDocument.scenarios | Where-Object {
    $null -ne $_.stripeSignature -and
    $null -ne $_.jsonBody -and
    [int]$_.expect.status -in @(200, 409)
})) {
    $body = Resolve-FixtureManifestValue $scenario.jsonBody
    $eventId = [string]$body.id
    if (@($expectedWebhookReceipts | Where-Object { $_.eventId -eq $eventId }).Count -gt 0) {
        continue
    }
    $bodyText = $body | ConvertTo-Json -Depth 100 -Compress
    $state = 'Completed'
    $reason = $null
    $matchedPaymentId = $null
    switch -Regex ($eventId) {
        '^evt_unknown_intent_' {
            $state = 'Quarantined'; $reason = 'payment_intent_not_found'; break
        }
        '^evt_meta_missing_' {
            $state = 'Quarantined'; $reason = 'booking_metadata_mismatch'; break
        }
        '^evt_booking_mismatch_' {
            $state = 'Quarantined'; $reason = 'booking_metadata_mismatch'; break
        }
        '^evt_payment_mismatch_' {
            $state = 'Quarantined'; $reason = 'payment_metadata_mismatch'; break
        }
        '^evt_amount_mismatch_' {
            $state = 'Quarantined'; $reason = 'amount_mismatch'; break
        }
        '^evt_currency_mismatch_' {
            $state = 'Quarantined'; $reason = 'currency_mismatch'; break
        }
        '^evt_charge_mismatch_' {
            $state = 'Quarantined'; $reason = 'charge_mismatch'; break
        }
        '^evt_deferred_refund_' {
            $reason = 'deferred_refund_applied'
            $matchedPaymentId = $webhookDeferredPayment.Id.ToString('D')
            break
        }
        '^evt_partial_refund_' {
            $reason = 'partial_refund_ignored'
            $matchedPaymentId = $webhookPartialRefundPayment.Id.ToString('D')
            break
        }
    }
    $expectedWebhookReceipts += [ordered]@{
        eventId = $eventId
        eventType = [string]$body.type
        bodyHash = ConvertTo-Hex (Get-Sha256 ([Text.Encoding]::UTF8.GetBytes($bodyText)))
        state = $state
        dispositionReason = $reason
        customerPaymentId = $matchedPaymentId
    }
}
$variables.expectedWebhookReceipts = $expectedWebhookReceipts

$runtimeTokens = @([regex]::Matches($runtimeManifest, '\{\{var:([^{}]+)\}\}') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique)
$missingTokens = @($runtimeTokens | Where-Object { -not $variables.Contains($_) })
if ($missingTokens.Count -gt 0) {
    throw "Initializer did not produce variables required by the runtime manifest: $($missingTokens -join ', ')."
}
$unsupportedTokens = @([regex]::Matches($runtimeManifest, '\{\{([^{}]+)\}\}') |
    ForEach-Object { $_.Groups[1].Value } |
    Where-Object { $_ -notmatch '^var:' })
if ($unsupportedTokens.Count -gt 0) {
    throw 'Runtime manifest contains unresolved non-variable tokens.'
}
if ($bookingFixtures.Count -ne 23 -or
    $paymentFixtures.Count -ne 11 -or
    $expectedWebhookReceipts.Count -ne 23) {
    throw 'Fixture identity/type contract did not produce the exact expected counts.'
}
$fixturePaymentIdSql = @($paymentFixtures | ForEach-Object {
    "'$($_.Id.ToString('D'))'"
}) -join ','
$fixtureBookingIdSql = @($bookingFixtures | ForEach-Object {
    "'$($_.Id.ToString('D'))'"
}) -join ','
if ([int](Invoke-Scalar $CustomerDatabase `
    "SELECT COUNT(*) FROM CustomerPayments WHERE Id IN ($fixturePaymentIdSql)") -ne 11 -or
    [int](Invoke-Scalar $CustomerDatabase `
    "SELECT COUNT(*) FROM CustomerBookings WHERE Id IN ($fixtureBookingIdSql)") -ne 23 -or
    [int](Invoke-Scalar $CustomerDatabase @"
SELECT COUNT(*) FROM CustomerPayments p
JOIN CustomerBookings b ON b.Id=p.CustomerBookingId
WHERE p.Id IN ($fixturePaymentIdSql)
  AND ((p.Status=0 AND (b.IsPaid<>0 OR b.PaymentState<>'Pending'))
    OR (p.Status=1 AND (b.IsPaid<>1 OR b.PaymentState<>'Completed'))
    OR (p.Status=2 AND (b.IsPaid<>0 OR b.PaymentState<>'Failed'))
    OR (p.Status=3 AND (b.IsPaid<>0 OR b.PaymentState<>'Refunded')))
"@) -ne 0) {
    throw 'Persisted fixture counts or payment/booking state types are invalid.'
}

[IO.File]::WriteAllText(
    $resolvedVariablesPath,
    ($variables | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    $runtimeManifestPath,
    $runtimeManifest,
    [Text.UTF8Encoding]::new($false))

[ordered]@{
    customerDatabase = $CustomerDatabase
    sourceCustomerDatabase = $SourceCustomerDatabase
    payableFixtures = 2
    bookingFixtures = 23
    paymentFixtures = 11
    idempotencyFixtures = 1
    webhookReceiptExpectations = 23
    manifestVariableTokens = $runtimeTokens.Count
    variablesPath = $resolvedVariablesPath
    runtimeManifestPath = $runtimeManifestPath
} | ConvertTo-Json
