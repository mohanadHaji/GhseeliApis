#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$StatePath,
    [switch]$RequireRoles,
    [switch]$RequireZeroDomainRows,
    [switch]$AllowMissingCustomer,
    [switch]$AllowMissingBusiness
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$state = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
function Test-IsExplicitLocalSqlServer([string]$value) {
    return $value -match '^(?i:\(localdb\)\\[A-Za-z0-9_.-]+)$' -or
        $value -match '^(?i:localhost|127\.0\.0\.1)(?:\\[A-Za-z0-9_.-]+|,[1-9][0-9]{0,4})?$' -or
        $value -match '^\.[\\][A-Za-z0-9_.-]+$'
}
if ($state.fixtureMarker -ne 'STEP16_DISPOSABLE_LOCAL_ONLY' -or
    $state.runId -notmatch '^[a-fA-F0-9]{12,32}$' -or
    $state.server -match '(?i)prod(uction)?' -or
    -not (Test-IsExplicitLocalSqlServer ([string]$state.server))) {
    throw 'State file is not a safe Step 16 disposable-local fixture.'
}
foreach ($database in @($state.customerDatabase,$state.businessDatabase,$state.wrongDatabase)) {
    if ($database -notmatch "^GhseeliStep16_$($state.runId.Substring(0,12))_" -or
        $database -match '(?i)prod(uction)?') {
        throw "Unsafe fixture database '$database'."
    }
}

$customerTables = @(
    'AspNetUsers','AspNetRoles','AspNetUserRoles','AspNetUserClaims',
    'AspNetRoleClaims','AspNetUserLogins','AspNetUserTokens','UserAddresses',
    'Vehicles','CustomerDevices','CustomerConfigurations','CatalogProviders',
    'CatalogBranches','CatalogCategories','CatalogOfferings','CatalogAddonGroups',
    'CatalogAddonChoices','CheckoutDrafts','CheckoutDraftItems',
    'CheckoutDraftSelections','CheckoutDraftPricingSnapshots',
    'CheckoutDraftPricingItemSnapshots','CheckoutDraftPricingSelectionSnapshots',
    'CustomerBookings','CustomerBookingItems','CustomerBookingSelections',
    'BookingConfirmationAttempts','CustomerPayments',
    'CustomerPaymentIdempotencyRecords','StripeWebhookEvents',
    'CustomerInternalServiceNonces','CustomerInternalIdempotencyRecords',
    'ProcessedBookingStatusMessages','__EFMigrationsHistory')
$businessTables = @(
    'AspNetUsers','AspNetRoles','AspNetUserRoles','AspNetUserClaims',
    'AspNetRoleClaims','AspNetUserLogins','AspNetUserTokens','Companies','Branches',
    'ServiceCategories','ServiceOfferings','AddonGroups','AddonChoices',
    'BranchAvailabilitySettings','BranchRecurringSchedules',
    'BranchAvailabilityOverrides','BranchServiceAreas','BusinessUserAssignments',
    'InternalServiceNonces','InternalServiceIdempotencyRecords',
    'AppointmentReservations','WorkOrders','WorkOrderItems','WorkOrderSelections',
    'BookingStatusOutboxMessages','BookingStatusRequeueHistory',
    '__EFMigrationsHistory')

function Query([string]$database,[string]$sql) {
    if ([string]::IsNullOrWhiteSpace($sql)) {
        throw 'Invariant query SQL must not be empty.'
    }
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$($state.server);Database=$database;Integrated Security=true;TrustServerCertificate=true")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $sql
        $reader = $command.ExecuteReader()
        $values = @()
        while ($reader.Read()) { $values += [string]$reader.GetValue(0) }
        return $values
    }
    finally { $connection.Dispose() }
}
function Test-Database([string]$database) {
    $count = @(Query -database 'master' `
        -sql "SELECT COUNT(*) FROM sys.databases WHERE name=N'$database'")
    return [int]$count[0] -eq 1
}
function Assert-Inventory([string]$database,[string[]]$expected,[string]$label) {
    $actual = @(Query $database "SELECT name FROM sys.tables ORDER BY name")
    $missing = @($expected | Where-Object { $_ -notin $actual })
    $extra = @($actual | Where-Object { $_ -notin $expected })
    if ($missing.Count -or $extra.Count) {
        throw "$label inventory mismatch. Missing: $($missing -join ', '); extra: $($extra -join ', ')."
    }
    $cross = @(Query $database @"
SELECT OBJECT_NAME(parent_object_id)
FROM sys.foreign_keys
WHERE referenced_object_id IS NULL OR referenced_object_id=0
"@)
    if ($cross.Count) { throw "$label contains an unresolved/cross-database foreign key." }
}
$hasCustomer = Test-Database $state.customerDatabase
$hasBusiness = Test-Database $state.businessDatabase
if (-not $hasCustomer -and -not $AllowMissingCustomer) {
    throw 'Customer disposable database is missing.'
}
if (-not $hasBusiness -and -not $AllowMissingBusiness) {
    throw 'Business disposable database is missing.'
}
if ($hasCustomer) { Assert-Inventory $state.customerDatabase $customerTables 'Customer' }
if ($hasBusiness) { Assert-Inventory $state.businessDatabase $businessTables 'Business' }
if (@(Query $state.wrongDatabase 'SELECT name FROM sys.tables').Count) {
    throw 'Wrong/foreign fixture database must remain empty.'
}
$customerPrincipals = @(if ($hasCustomer) {
    Query $state.customerDatabase `
        "SELECT name FROM sys.database_principals WHERE name='$($state.customerPrincipal)'"
})
$businessPrincipals = @(if ($hasBusiness) {
    Query $state.businessDatabase `
        "SELECT name FROM sys.database_principals WHERE name='$($state.businessPrincipal)'"
})
$crossPrincipals = @()
if ($hasCustomer) { $crossPrincipals += @(Query $state.customerDatabase `
    "SELECT name FROM sys.database_principals WHERE name='$($state.businessPrincipal)'") }
if ($hasBusiness) { $crossPrincipals += @(Query $state.businessDatabase `
    "SELECT name FROM sys.database_principals WHERE name='$($state.customerPrincipal)'") }
if (($hasCustomer -and $customerPrincipals.Count -ne 1) -or
    ($hasBusiness -and $businessPrincipals.Count -ne 1) -or $crossPrincipals.Count) {
    throw 'Owning SQL principals are missing, duplicated, or visible in the foreign database.'
}

if ($RequireRoles) {
    if (-not $hasCustomer -or -not $hasBusiness) {
        throw 'Exact role verification requires both disposable databases.'
    }
    $customerRoles = @(Query $state.customerDatabase 'SELECT Name FROM AspNetRoles ORDER BY Name')
    $businessRoles = @(Query $state.businessDatabase 'SELECT Name FROM AspNetRoles ORDER BY Name')
    if (($customerRoles -join ',') -ne 'Admin,User') {
        throw "Customer roles are not exactly Admin,User: $($customerRoles -join ',')."
    }
    if (($businessRoles -join ',') -ne 'Admin,Employee,Owner') {
        throw "Business roles are not exactly Admin,Employee,Owner: $($businessRoles -join ',')."
    }
}
if ($RequireZeroDomainRows) {
    foreach ($item in @(
            [pscustomobject]@{ Database=$state.customerDatabase; Tables=$customerTables;
                Exists=$hasCustomer },
            [pscustomobject]@{ Database=$state.businessDatabase; Tables=$businessTables;
                Exists=$hasBusiness })) {
        if (-not $item.Exists) { continue }
        foreach ($table in @($item.Tables | Where-Object {
                    $_ -notin @('AspNetRoles','__EFMigrationsHistory')
                })) {
            $count = @(Query -database $item.Database `
                -sql "SELECT COUNT(*) FROM [$table]")
            if ([int]$count[0] -ne 0) {
                throw "$($item.Database).$table was unexpectedly seeded."
            }
        }
    }
}
Write-Host 'Step 16 exact schema, ownership, principal, and seed invariants passed.'
