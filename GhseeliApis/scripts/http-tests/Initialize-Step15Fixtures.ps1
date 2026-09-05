#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceCustomerDatabase,
    [Parameter(Mandatory = $true)][string]$SourceBusinessDatabase,
    [Parameter(Mandatory = $true)][string]$CustomerDatabase,
    [Parameter(Mandatory = $true)][string]$BusinessDatabase,
    [Parameter(Mandatory = $true)][string]$CustomerJwtSecret,
    [Parameter(Mandatory = $true)][string]$BusinessJwtSecret,
    [string]$CustomerJwtIssuer = 'GhseeliCustomer.Step15.Local',
    [string]$CustomerJwtAudience = 'GhseeliCustomer.Step15.Local',
    [string]$BusinessJwtIssuer = 'GhseeliBusiness.Step15.Local',
    [string]$BusinessJwtAudience = 'GhseeliBusiness.Step15.Local',
    [string]$VariablesPath =
        '.\scripts\http-tests\artifacts\step-15.variables.local.json',
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$server = '(localdb)\MSSQLLocalDB'
$solution = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifacts = Join-Path $PSScriptRoot 'artifacts'

$names = @(
    $SourceCustomerDatabase, $SourceBusinessDatabase,
    $CustomerDatabase, $BusinessDatabase)
foreach ($name in $names) {
    if ($name -notmatch '^[A-Za-z0-9_]+$' -or $name -match '(?i)prod(uction)?') {
        throw "Unsafe local fixture database name '$name'."
    }
}
if ($CustomerDatabase -eq $SourceCustomerDatabase -or
    $BusinessDatabase -eq $SourceBusinessDatabase -or
    $CustomerDatabase -eq $BusinessDatabase) {
    throw 'Step 15 source and isolated target databases must all be distinct.'
}
if ($CustomerJwtSecret.Length -lt 32 -or $BusinessJwtSecret.Length -lt 32) {
    throw 'Local JWT fixture secrets must contain at least 32 characters.'
}
if ($ValidateOnly) {
    Write-Host 'Step 15 initializer safety validation passed.'
    return
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

function Get-TableEvidence([string]$database, [string]$table) {
    if ($table -notmatch '^[A-Za-z0-9_]+$') {
        throw "Unsafe evidence table '$table'."
    }
    $count = [int](Invoke-Scalar $database "SELECT COUNT(*) FROM [$table]")
    $projection = if ($table -eq 'CustomerDevices') {
        'Id,InstallationId,Platform,AppVersion,TokenHash,CreatedAt,ExpiresAt'
    }
    else {
        '*'
    }
    $hash = [string](Invoke-Scalar $database @"
SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256',
    COALESCE((SELECT $projection FROM [$table] ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]')), 2)
"@)
    return [ordered]@{
        table = $table
        count = $count
        sha256 = $hash
    }
}

function Get-JsonRows([string]$database, [string]$sql) {
    $connection = Open-Connection $database
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $sql
        $reader = $command.ExecuteReader()
        $builder = [Text.StringBuilder]::new()
        while ($reader.Read()) {
            if (-not $reader.IsDBNull(0)) {
                [void]$builder.Append($reader.GetString(0))
            }
        }
        $json = $builder.ToString()
    }
    finally { $connection.Dispose() }
    if ([string]::IsNullOrWhiteSpace($json)) { return @() }
    return @($json | ConvertFrom-Json)
}

function Copy-LocalDatabase([string]$source, [string]$target) {
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
                'SELECT physical_name FROM sys.master_files WHERE database_id=1 AND file_id=1'))
        }
        $moves = foreach ($file in $files) {
            $extension = if ($file.Type -eq 'L') { '.ldf' } else { '.mdf' }
            $path = Join-Path $dataRoot "$target$extension"
            ", MOVE N'$($file.LogicalName.Replace("'", "''"))' TO N'$($path.Replace("'", "''"))'"
        }
        Invoke-Sql master `
            "RESTORE DATABASE [$target] FROM DISK=N'$escapedBackup' WITH REPLACE$($moves -join '')"
    }
    finally { $connection.Dispose() }
}

function New-RandomToken {
    $bytes = [byte[]]::new(32)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-HashSql([string]$value) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($value)) }
    finally { $sha.Dispose() }
    return '0x' + (-join $hash.ForEach({ $_.ToString('X2') }))
}

function New-Jwt(
    [guid]$userId, [string]$role, [string]$secret,
    [string]$issuer, [string]$audience, [guid]$companyId
) {
    function Encode([byte[]]$bytes) {
        return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }
    $header = Encode ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $payload = [ordered]@{
        'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier' =
            $userId.ToString('D')
        'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' = $role
        iss = $issuer
        aud = $audience
        nbf = $now - 30
        iat = $now
        exp = $now + 7200
    }
    if ($companyId -ne [guid]::Empty) {
        $payload['company_id'] = $companyId.ToString('D')
    }
    $encodedPayload = Encode ([Text.Encoding]::UTF8.GetBytes(
        ($payload | ConvertTo-Json -Compress)))
    $unsigned = "$header.$encodedPayload"
    $hmac = [Security.Cryptography.HMACSHA256]::new(
        [Text.Encoding]::UTF8.GetBytes($secret))
    try { $signature = Encode ($hmac.ComputeHash([Text.Encoding]::ASCII.GetBytes($unsigned))) }
    finally { $hmac.Dispose() }
    return "$unsigned.$signature"
}

Copy-LocalDatabase $SourceCustomerDatabase $CustomerDatabase
Copy-LocalDatabase $SourceBusinessDatabase $BusinessDatabase

$env:ConnectionStrings__RemoteTest =
    "Server=$server;Database=$CustomerDatabase;Integrated Security=true;TrustServerCertificate=true"
$env:ConnectionStrings__BusinessConnection =
    "Server=$server;Database=$BusinessDatabase;Integrated Security=true;TrustServerCertificate=true"
try {
    & dotnet ef database update `
        --project (Join-Path $solution 'Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj') `
        --startup-project (Join-Path $solution 'Ghseeli.CustomerApi\Ghseeli.CustomerApi.csproj') `
        --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Customer migration failed.' }
    & dotnet ef database update `
        --project (Join-Path $solution 'Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj') `
        --startup-project (Join-Path $solution 'Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj') `
        --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Business migration failed.' }
}
finally {
    Remove-Item Env:\ConnectionStrings__RemoteTest -ErrorAction SilentlyContinue
    Remove-Item Env:\ConnectionStrings__BusinessConnection -ErrorAction SilentlyContinue
}

$customerUserId = [guid](Invoke-Scalar $CustomerDatabase `
    'SELECT TOP (1) Id FROM AspNetUsers ORDER BY Id')
$businessUserId = [guid](Invoke-Scalar $BusinessDatabase @'
SELECT TOP (1) a.UserId
FROM BusinessUserAssignments a
WHERE a.IsActive=1
ORDER BY a.UserId
'@)
$companyId = [guid](Invoke-Scalar $BusinessDatabase `
    "SELECT TOP (1) CompanyId FROM BusinessUserAssignments WHERE UserId='$businessUserId' AND IsActive=1")

$deviceId = [guid]'15000000-0000-4000-8000-000000000001'
$expiredDeviceId = [guid]'15000000-0000-4000-8000-000000000002'
$foreignDeviceId = [guid]'15000000-0000-4000-8000-000000000003'
$deviceToken = New-RandomToken
$expiredToken = New-RandomToken
$foreignToken = New-RandomToken
$now = [DateTimeOffset]::UtcNow
Invoke-Sql $CustomerDatabase @"
DELETE FROM CustomerDevices WHERE Id IN ('$deviceId','$expiredDeviceId','$foreignDeviceId');
INSERT INTO CustomerDevices
 (Id,InstallationId,Platform,AppVersion,TokenHash,CreatedAt,UpdatedAt,ExpiresAt)
VALUES
 ('$deviceId','15000000-0000-4000-8000-000000000011',N'android',N'step15',
  $(Get-HashSql $deviceToken),'$($now.ToString('o'))','$($now.ToString('o'))',
  '$($now.AddDays(2).ToString('o'))'),
 ('$expiredDeviceId','15000000-0000-4000-8000-000000000012',N'ios',N'step15',
  $(Get-HashSql $expiredToken),'$($now.AddDays(-4).ToString('o'))',
  '$($now.AddDays(-4).ToString('o'))','$($now.AddDays(-2).ToString('o'))'),
 ('$foreignDeviceId','15000000-0000-4000-8000-000000000013',N'android',N'step15',
  $(Get-HashSql $foreignToken),'$($now.ToString('o'))','$($now.ToString('o'))',
  '$($now.AddDays(2).ToString('o'))');
DELETE FROM CustomerConfigurations WHERE Id='15000000-0000-4000-8000-000000000020';
UPDATE CustomerConfigurations SET IsActive=0;
INSERT INTO CustomerConfigurations
 (Id,IsActive,SupportEmail,SupportPhone,DisplayNameAr,DisplayNameHe,
  LegalNoticeAr,LegalNoticeHe,PrivacyPolicyUrl,TermsOfServiceUrl,
  IsMaintenanceModeEnabled,MaintenanceMessageAr,MaintenanceMessageHe,CreatedAt,UpdatedAt)
VALUES
 ('15000000-0000-4000-8000-000000000020',1,N'support@step15.example.invalid',
  N'+970000000000',N'غسيلي المحلي',N'ע׳סילי מקומי',N'إشعار محلي آمن',
  N'הודעה מקומית בטוחה',N'https://step15.example.invalid/privacy',
  N'https://step15.example.invalid/terms',0,NULL,NULL,
  '$($now.ToString('o'))','$($now.ToString('o'))');
"@

$customerEvidenceTables = @(
    'CustomerDevices', 'CustomerConfigurations', 'CheckoutDrafts',
    'CustomerBookings', 'CustomerPayments',
    'CustomerPaymentIdempotencyRecords',
    'BookingConfirmationAttempts', 'CustomerInternalServiceNonces',
    'CustomerInternalIdempotencyRecords')
$businessEvidenceTables = @(
    'Companies', 'ServiceCategories', 'ServiceOfferings',
    'BranchAvailabilitySettings', 'BranchRecurringSchedules',
    'BranchAvailabilityOverrides', 'BranchServiceAreas',
    'AppointmentReservations', 'WorkOrders', 'BookingStatusOutboxMessages',
    'BookingStatusRequeueHistory', 'InternalServiceNonces',
    'InternalServiceIdempotencyRecords')
$expectedCustomerEvidence = @($customerEvidenceTables | ForEach-Object {
    Get-TableEvidence $CustomerDatabase $_
})
$expectedBusinessEvidence = @($businessEvidenceTables | ForEach-Object {
    Get-TableEvidence $BusinessDatabase $_
})
$expectedCustomerDrafts = Get-JsonRows $CustomerDatabase @"
SELECT Id AS id,OrderGuid AS orderGuid,OwnerDeviceId AS ownerDeviceId,
       CatalogVersion AS catalogVersion,PublicVersion AS publicVersion,
       CONVERT(varchar(32),RowVersion,2) AS rowVersion
FROM CheckoutDrafts ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES
"@
$expectedCustomerBookings = Get-JsonRows $CustomerDatabase @"
SELECT Id AS id,PublicReference AS publicReference,OrderGuid AS orderGuid,
       UserId AS userId,OwnerDeviceId AS ownerDeviceId,Status AS status,
       PaymentState AS paymentState
FROM CustomerBookings ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES
"@
$expectedCustomerPayments = Get-JsonRows $CustomerDatabase @"
SELECT Id AS id,CustomerBookingId AS customerBookingId,UserId AS userId,
       OwnerDeviceId AS ownerDeviceId,Status AS status,
       IdempotencyKey AS idempotencyKey
FROM CustomerPayments ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES
"@
$expectedBusinessCompanies = Get-JsonRows $BusinessDatabase @"
SELECT Id AS id,CatalogVersion AS catalogVersion,
       CONVERT(varchar(32),RowVersion,2) AS rowVersion
FROM Companies ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES
"@
$expectedBusinessWorkOrders = Get-JsonRows $BusinessDatabase @"
SELECT Id AS id,PublicId AS publicId,AppointmentReservationId AS reservationId,
       Status AS status,CONVERT(varchar(32),RowVersion,2) AS rowVersion
FROM WorkOrders ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES
"@
$expectedBusinessOutbox = Get-JsonRows $BusinessDatabase @"
SELECT Id AS id,AppointmentReservationId AS reservationId,
       WorkOrderPublicId AS workOrderPublicId,Status AS status,
       Sequence AS sequence,DeliveryState AS deliveryState,
       DeliveryGeneration AS deliveryGeneration,
       CONVERT(varchar(32),RowVersion,2) AS rowVersion
FROM BookingStatusOutboxMessages ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES
"@

$variables = [ordered]@{
    customerBaseUrl = 'https://localhost:5011'
    customerHttpBaseUrl = 'http://localhost:5010'
    businessBaseUrl = 'https://localhost:5012'
    businessHttpBaseUrl = 'http://localhost:5013'
    customerProductionBaseUrl = 'https://localhost:5014'
    businessProductionBaseUrl = 'https://localhost:5015'
    oversizedLanguageHeader = ('x' * 8193)
    customerDeviceToken = $deviceToken
    expiredDeviceToken = $expiredToken
    foreignDeviceToken = $foreignToken
    customerJwt = New-Jwt $customerUserId 'User' $CustomerJwtSecret `
        $CustomerJwtIssuer $CustomerJwtAudience ([guid]::Empty)
    businessJwt = New-Jwt $businessUserId 'Owner' $BusinessJwtSecret `
        $BusinessJwtIssuer $BusinessJwtAudience $companyId
    customerUserId = $customerUserId.ToString('D')
    businessUserId = $businessUserId.ToString('D')
    companyId = $companyId.ToString('D')
    customerDeviceId = $deviceId.ToString('D')
    expectedCustomerEvidence = $expectedCustomerEvidence
    expectedBusinessEvidence = $expectedBusinessEvidence
    expectedCustomerDrafts = $expectedCustomerDrafts
    expectedCustomerBookings = $expectedCustomerBookings
    expectedCustomerPayments = $expectedCustomerPayments
    expectedBusinessCompanies = $expectedBusinessCompanies
    expectedBusinessWorkOrders = $expectedBusinessWorkOrders
    expectedBusinessOutbox = $expectedBusinessOutbox
    expectedManifestEntryCount = 90
    customerDatabase = $CustomerDatabase
    businessDatabase = $BusinessDatabase
    fixtureMarker = 'STEP15_LOCAL_ONLY'
}
$resolvedVariablesPath = [IO.Path]::GetFullPath($VariablesPath)
New-Item -ItemType Directory -Path (Split-Path $resolvedVariablesPath) -Force | Out-Null
$variables | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $resolvedVariablesPath -Encoding UTF8
Write-Host 'Created isolated Step 15 Customer and Business fixtures. Runtime values were written to an ignored local JSON file.'
