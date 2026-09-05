#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceCustomerDatabase,
    [Parameter(Mandatory)]
    [string]$CustomerDatabase,
    [Parameter(Mandatory)]
    [string]$JwtSecret,
    [Parameter(Mandatory)]
    [string]$WebhookSecret,
    [Parameter(Mandatory)]
    [string]$VariablesPath,
    [string]$Server = '(localdb)\MSSQLLocalDB',
    [switch]$SkipDatabaseCopy
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$solution = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifacts = Join-Path $PSScriptRoot 'artifacts'
$resolvedVariables = $ExecutionContext.SessionState.Path.
    GetUnresolvedProviderPathFromPSPath($VariablesPath)
if ((Split-Path -Leaf (Split-Path -Parent $resolvedVariables)) -cne 'artifacts' -or
    (Split-Path -Leaf $resolvedVariables) -notlike '*.local.json') {
    throw 'Step 18 variables must be an ignored artifacts/*.local.json file.'
}
foreach ($name in @($SourceCustomerDatabase, $CustomerDatabase)) {
    if ($name -notmatch '^[A-Za-z0-9_]+$') {
        throw 'Unsafe database name.'
    }
}
if (-not $SkipDatabaseCopy -and [string]::Equals(
        $SourceCustomerDatabase,
        $CustomerDatabase,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source and target customer databases must differ.'
}
if ($JwtSecret.Length -lt 32 -or $WebhookSecret.Length -lt 32) {
    throw 'Step 18 local secrets must contain at least 32 characters.'
}
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

function Open-Connection([string]$Database) {
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$Server;Database=$Database;Integrated Security=true;TrustServerCertificate=true")
    $connection.Open()
    return $connection
}

function Invoke-Sql([string]$Database, [string]$Sql) {
    $connection = Open-Connection $Database
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}

function Invoke-Scalar([string]$Database, [string]$Sql) {
    $connection = Open-Connection $Database
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        return $command.ExecuteScalar()
    }
    finally { $connection.Dispose() }
}

function Copy-Database([string]$Source, [string]$Target) {
    $backup = Join-Path $artifacts "$Target.bak"
    $escapedBackup = $backup.Replace("'", "''")
    try {
        Invoke-Sql master @"
IF DB_ID(N'$Target') IS NOT NULL
BEGIN
    ALTER DATABASE [$Target] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$Target];
END;
BACKUP DATABASE [$Source] TO DISK=N'$escapedBackup' WITH INIT, COPY_ONLY;
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
        }
        finally { $connection.Dispose() }
        $dataRoot = [string](Invoke-Scalar master `
            "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000))")
        if ([string]::IsNullOrWhiteSpace($dataRoot)) {
            $dataRoot = Split-Path -Parent ([string](Invoke-Scalar master `
                "SELECT physical_name FROM sys.master_files WHERE database_id=1 AND file_id=1"))
        }
        $moves = foreach ($file in $files) {
            $extension = if ($file.Type -eq 'L') { '.ldf' } else { '.mdf' }
            $path = Join-Path $dataRoot ($Target + $extension)
            ", MOVE N'$($file.LogicalName.Replace("'", "''"))' TO N'$($path.Replace("'", "''"))'"
        }
        Invoke-Sql master `
            "RESTORE DATABASE [$Target] FROM DISK=N'$escapedBackup' WITH REPLACE$($moves -join '')"
    }
    finally {
        Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    }
}

function Get-CloneSql(
    [string]$Table,
    [string]$Where,
    [hashtable]$Replacements) {
    $connection = Open-Connection $CustomerDatabase
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = @"
SELECT name
FROM sys.columns
WHERE object_id = OBJECT_ID(N'dbo.$Table')
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
        if ($Replacements.ContainsKey($column)) { $Replacements[$column] }
        else { "[$column]" }
    }
    return "INSERT INTO dbo.[$Table] ($($columns.ForEach({ ""[$_]"" }) -join ',')) " +
        "SELECT $($select -join ',') FROM dbo.[$Table] WHERE $Where;"
}

function New-RandomBytes([int]$Count) {
    $bytes = [byte[]]::new($Count)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) }
    finally { $generator.Dispose() }
    return $bytes
}

function ConvertTo-Base64Url([byte[]]$Bytes) {
    return [Convert]::ToBase64String($Bytes).
        TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function ConvertTo-Hex([byte[]]$Bytes) {
    return -join $Bytes.ForEach({ $_.ToString('X2') })
}

function Get-Sha256([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return $sha.ComputeHash($Bytes) }
    finally { $sha.Dispose() }
}

function New-Jwt([guid]$UserId, [string]$Role = 'User') {
    $header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes(
        '{"alg":"HS256","typ":"JWT"}'))
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $payload = [ordered]@{
        'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' =
            $UserId.ToString('D')
        'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' = $Role
        iss = 'GhseeliApis'
        aud = 'GhseeliApis'
        nbf = $now - 30
        iat = $now
        exp = $now + 7200
    }
    $encodedPayload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes(
        ($payload | ConvertTo-Json -Compress)))
    $unsigned = "$header.$encodedPayload"
    $hmac = [Security.Cryptography.HMACSHA256]::new(
        [Text.Encoding]::UTF8.GetBytes($JwtSecret))
    try {
        $signature = ConvertTo-Base64Url ($hmac.ComputeHash(
            [Text.Encoding]::ASCII.GetBytes($unsigned)))
    }
    finally { $hmac.Dispose() }
    return "$unsigned.$signature"
}

if (-not $SkipDatabaseCopy) {
    Copy-Database $SourceCustomerDatabase $CustomerDatabase
    $env:ConnectionStrings__CustomerConnection =
        "Server=$Server;Database=$CustomerDatabase;Integrated Security=true;TrustServerCertificate=true"
    try {
        & dotnet ef database update `
            --project (Join-Path $solution 'Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj') `
            --startup-project (Join-Path $solution 'Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj') `
            --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Step 18 customer migration failed.' }
    }
    finally {
        Remove-Item Env:\ConnectionStrings__CustomerConnection -ErrorAction SilentlyContinue
    }
}

$sourceBookingId = Invoke-Scalar $CustomerDatabase @"
SELECT TOP (1) b.Id
FROM dbo.CustomerBookings b
JOIN dbo.AspNetUsers u ON u.Id=b.UserId
WHERE u.Email IS NOT NULL AND LTRIM(RTRIM(u.Email))<>''
ORDER BY b.CreatedAtUtc;
"@
if ($null -eq $sourceBookingId) {
    throw 'Step 18 fixtures require one existing customer booking with an email-backed user.'
}
$sourceBookingId = [guid]$sourceBookingId
$ownerUserId = [guid](Invoke-Scalar $CustomerDatabase `
    "SELECT UserId FROM dbo.CustomerBookings WHERE Id='$sourceBookingId'")
$ownerDeviceId = [guid]::NewGuid()
$otherDeviceId = [guid]::NewGuid()
$ownerToken = ConvertTo-Base64Url (New-RandomBytes 32)
$otherToken = ConvertTo-Base64Url (New-RandomBytes 32)
$now = [DateTimeOffset]::UtcNow

foreach ($device in @(
    @($ownerDeviceId, $ownerToken),
    @($otherDeviceId, $otherToken)
)) {
    $hash = '0x' + (ConvertTo-Hex (Get-Sha256 (
        [Text.Encoding]::ASCII.GetBytes([string]$device[1]))))
    Invoke-Sql $CustomerDatabase @"
INSERT INTO dbo.CustomerDevices
    (Id,InstallationId,Platform,AppVersion,TokenHash,CreatedAt,UpdatedAt,LastSeenAt,ExpiresAt)
VALUES
    ('$($device[0])','$([guid]::NewGuid())',N'android',N'step18',$hash,
     '$($now.ToString('o'))','$($now.ToString('o'))',NULL,
     '$($now.AddDays(2).ToString('o'))');
"@
}

function Add-BookingFixture {
    $id = [guid]::NewGuid()
    $reference = [guid]::NewGuid()
    $replacements = @{
        Id = "CAST('$id' AS uniqueidentifier)"
        PublicReference = "CAST('$reference' AS uniqueidentifier)"
        OrderGuid = "CAST('$([guid]::NewGuid())' AS uniqueidentifier)"
        UserId = "CAST('$ownerUserId' AS uniqueidentifier)"
        OwnerDeviceId = "CAST('$ownerDeviceId' AS uniqueidentifier)"
        BusinessReservationId = "CAST('$([guid]::NewGuid())' AS uniqueidentifier)"
        BusinessWorkOrderId = "CAST('$([guid]::NewGuid())' AS uniqueidentifier)"
        Status = "N'Pending'"
        BusinessStatusSequence = 'CAST(0 AS bigint)'
        IsPaid = 'CAST(0 AS bit)'
        PaymentState = "N'Unpaid'"
        Currency = "N'ILS'"
        GrandTotal = 'CAST(10.00 AS decimal(18,2))'
        CreatedAtUtc = "CAST('$($now.ToString('o'))' AS datetimeoffset)"
        UpdatedAtUtc = "CAST('$($now.ToString('o'))' AS datetimeoffset)"
    }
    Invoke-Sql $CustomerDatabase (Get-CloneSql 'CustomerBookings' `
        "Id='$sourceBookingId'" $replacements)
    return [pscustomobject]@{ Id = $id; Reference = $reference }
}

$primary = Add-BookingFixture
$secondary = Add-BookingFixture
$pendingVerify = Add-BookingFixture
$partialRefund = Add-BookingFixture
$unconfigured = Add-BookingFixture

$variables = [ordered]@{
    ownerAuthorization = "Bearer $(New-Jwt $ownerUserId)"
    otherUserAuthorization = "Bearer $(New-Jwt ([guid]::NewGuid()))"
    wrongRoleAuthorization = "Bearer $(New-Jwt $ownerUserId 'Admin')"
    ownerDeviceToken = $ownerToken
    otherDeviceToken = $otherToken
    primaryBookingId = $primary.Reference.ToString('D')
    secondaryBookingId = $secondary.Reference.ToString('D')
    pendingVerifyBookingId = $pendingVerify.Reference.ToString('D')
    partialRefundBookingId = $partialRefund.Reference.ToString('D')
    unconfiguredBookingId = $unconfigured.Reference.ToString('D')
    unknownPaymentId = [guid]::NewGuid().ToString('D')
    webhookSecret = $WebhookSecret
    successEventId = "evt_step18_success_$([guid]::NewGuid().ToString('N'))"
    duplicateEventId = "evt_step18_duplicate_$([guid]::NewGuid().ToString('N'))"
    unknownEventId = "evt_step18_unknown_$([guid]::NewGuid().ToString('N'))"
    refundPendingEventId = "evt_step18_refund_pending_$([guid]::NewGuid().ToString('N'))"
    refundProcessingEventId = "evt_step18_refund_processing_$([guid]::NewGuid().ToString('N'))"
    refundProcessedEventId = "evt_step18_refund_processed_$([guid]::NewGuid().ToString('N'))"
    deferredRefundEventId = "evt_step18_deferred_refund_$([guid]::NewGuid().ToString('N'))"
    deferredSuccessEventId = "evt_step18_deferred_success_$([guid]::NewGuid().ToString('N'))"
    partialSuccessEventId = "evt_step18_partial_success_$([guid]::NewGuid().ToString('N'))"
    partialRefundEventId = "evt_step18_partial_refund_$([guid]::NewGuid().ToString('N'))"
    refundFailedEventId = "evt_step18_refund_failed_$([guid]::NewGuid().ToString('N'))"
}
[IO.File]::WriteAllText(
    $resolvedVariables,
    ($variables | ConvertTo-Json -Depth 10),
    [Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    customerDatabase = $CustomerDatabase
    ownerUserId = $ownerUserId
    bookingFixtures = 5
    deviceFixtures = 2
    variablesPath = $resolvedVariables
} | ConvertTo-Json -Compress
