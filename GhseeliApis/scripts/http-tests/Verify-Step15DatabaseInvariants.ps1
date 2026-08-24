#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CustomerDatabase,
    [Parameter(Mandatory = $true)][string]$BusinessDatabase,
    [Parameter(Mandatory = $true)][string]$VariablesPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$server = '(localdb)\MSSQLLocalDB'
foreach ($name in @($CustomerDatabase, $BusinessDatabase)) {
    if ($name -notmatch '^[A-Za-z0-9_]+$' -or $name -match '(?i)prod(uction)?') {
        throw "Unsafe local fixture database name '$name'."
    }
}
$variables = Get-Content -LiteralPath $VariablesPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ($variables.fixtureMarker -ne 'STEP15_LOCAL_ONLY' -or
    $variables.customerDatabase -ne $CustomerDatabase -or
    $variables.businessDatabase -ne $BusinessDatabase) {
    throw 'Variables do not belong to these isolated Step 15 databases.'
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

function Get-TableEvidence([string]$database, [string]$table) {
    if ($table -notmatch '^[A-Za-z0-9_]+$') {
        throw "Unsafe evidence table '$table'."
    }
    $projection = if ($table -eq 'CustomerDevices') {
        'Id,InstallationId,Platform,AppVersion,TokenHash,CreatedAt,ExpiresAt'
    }
    else {
        '*'
    }
    return [ordered]@{
        table = $table
        count = [int](Scalar $database "SELECT COUNT(*) FROM [$table]")
        sha256 = [string](Scalar $database @"
SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256',
    COALESCE((SELECT $projection FROM [$table] ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES), N'[]')), 2)
"@)
    }
}

$failures = [Collections.Generic.List[string]]::new()
function Expect-One([string]$name, [string]$database, [string]$sql) {
    $count = [int](Scalar $database $sql)
    if ($count -ne 1) { $failures.Add("$name expected exactly one row; found $count.") }
}
Expect-One 'Active localized configuration' $CustomerDatabase `
    "SELECT COUNT(*) FROM CustomerConfigurations WHERE Id='15000000-0000-4000-8000-000000000020' AND IsActive=1 AND LEN(DisplayNameAr)>0 AND LEN(DisplayNameHe)>0"
Expect-One 'Current device fixture' $CustomerDatabase `
    "SELECT COUNT(*) FROM CustomerDevices WHERE Id='$($variables.customerDeviceId)' AND ExpiresAt>SYSUTCDATETIME()"
Expect-One 'Customer JWT subject' $CustomerDatabase `
    "SELECT COUNT(*) FROM AspNetUsers WHERE Id='$($variables.customerUserId)'"
Expect-One 'Business JWT subject assignment' $BusinessDatabase `
    "SELECT COUNT(*) FROM BusinessUserAssignments WHERE UserId='$($variables.businessUserId)' AND CompanyId='$($variables.companyId)' AND IsActive=1"

foreach ($property in @(
        'expectedCustomerEvidence', 'expectedBusinessEvidence',
        'expectedCustomerDrafts', 'expectedCustomerBookings',
        'expectedCustomerPayments', 'expectedBusinessCompanies',
        'expectedBusinessWorkOrders', 'expectedBusinessOutbox',
        'expectedManifestEntryCount')) {
    if (-not ($variables.psobject.Properties.Name -contains $property)) {
        $failures.Add("Variables are missing required fixture evidence '$property'.")
    }
}
if ($variables.expectedManifestEntryCount -ne 90) {
    $failures.Add('Variables were not initialized for the 90-entry Step 15 manifest.')
}
foreach ($databaseEvidence in @(
        [pscustomobject]@{
            database = $CustomerDatabase
            evidence = @($variables.expectedCustomerEvidence)
            label = 'Customer'
        },
        [pscustomobject]@{
            database = $BusinessDatabase
            evidence = @($variables.expectedBusinessEvidence)
            label = 'Business'
        })) {
    foreach ($expected in $databaseEvidence.evidence) {
        $actual = Get-TableEvidence $databaseEvidence.database $expected.table
        if ($actual.count -ne [int]$expected.count -or
            $actual.sha256 -cne [string]$expected.sha256) {
            $failures.Add(
                "$($databaseEvidence.label) $($expected.table) identity/version snapshot changed.")
        }
    }
}

$mutationChecks = [ordered]@{
    CustomerInternalServiceNonces =
        "SELECT COUNT(*) FROM CustomerInternalServiceNonces WHERE Nonce LIKE 'step15-%'"
    CustomerInternalIdempotencyRecords =
        "SELECT COUNT(*) FROM CustomerInternalIdempotencyRecords WHERE IdempotencyKey LIKE 'step15-%'"
    StripeWebhookEvents =
        "SELECT COUNT(*) FROM StripeWebhookEvents WHERE EventId LIKE 'step15-%'"
}
foreach ($check in $mutationChecks.GetEnumerator()) {
    $table = $check.Key
    $unsafe = [int](Scalar $CustomerDatabase `
        $check.Value)
    if ($unsafe -ne 0) {
        $failures.Add("$table contains unexpected Step 15 mutation evidence.")
    }
    $businessMutationChecks = [ordered]@{
        InternalServiceNonces =
            "SELECT COUNT(*) FROM InternalServiceNonces WHERE Nonce LIKE 'step15-%'"
        InternalServiceIdempotencyRecords =
            "SELECT COUNT(*) FROM InternalServiceIdempotencyRecords WHERE IdempotencyKey LIKE 'step15-%'"
        BookingStatusRequeueHistory =
            "SELECT COUNT(*) FROM BookingStatusRequeueHistory WHERE RequestId LIKE 'step15-%'"
    }
    foreach ($check in $businessMutationChecks.GetEnumerator()) {
        $unsafe = [int](Scalar $BusinessDatabase $check.Value)
        if ($unsafe -ne 0) {
            $failures.Add("$($check.Key) contains unexpected Step 15 mutation evidence.")
        }
    }
}
if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}
Write-Host 'Step 15 isolated fixture and no-unexpected-mutation invariants passed.'
