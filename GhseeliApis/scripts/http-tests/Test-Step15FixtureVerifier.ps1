#requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$server = '(localdb)\MSSQLLocalDB'
$suffix = [guid]::NewGuid().ToString('N')
$customer = "Step15CustomerSelfTest_$suffix"
$business = "Step15BusinessSelfTest_$suffix"
$variablesPath = Join-Path $PSScriptRoot "artifacts\step15-verifier-$suffix.local.json"
$initializer = Join-Path $PSScriptRoot 'Initialize-Step15Fixtures.ps1'
$verifier = Join-Path $PSScriptRoot 'Verify-Step15DatabaseInvariants.ps1'
$shell = (Get-Process -Id $PID).Path

function Sql([string]$database, [string]$text) {
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$server;Database=$database;Integrated Security=true;TrustServerCertificate=true")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $text
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}

function Scalar([string]$database, [string]$text) {
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$server;Database=$database;Integrated Security=true;TrustServerCertificate=true")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $text
        return $command.ExecuteScalar()
    }
    finally { $connection.Dispose() }
}

function Evidence([string]$database, [string[]]$tables) {
    return @($tables | ForEach-Object {
        $projection = if ($_ -eq 'CustomerDevices') {
            'Id,InstallationId,Platform,AppVersion,TokenHash,CreatedAt,ExpiresAt'
        }
        else {
            '*'
        }
        [ordered]@{
            table = $_
            count = [int](Scalar $database "SELECT COUNT(*) FROM [$_]")
            sha256 = [string](Scalar $database @"
SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256',
 COALESCE((SELECT $projection FROM [$_] ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES),N'[]')),2)
"@)
        }
    })
}

function Run-Verifier([bool]$expected, [string]$name) {
    $priorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & $shell -NoProfile -ExecutionPolicy Bypass -File $verifier `
        -CustomerDatabase $customer -BusinessDatabase $business `
        -VariablesPath $variablesPath 2>&1
    $ErrorActionPreference = $priorPreference
    $success = $LASTEXITCODE -eq 0
    if ($success -ne $expected) {
        throw "$name success was '$success', expected '$expected'. Output: $output"
    }
    Write-Host "[PASS] $name"
}

$created = [Collections.Generic.List[string]]::new()
try {
    & $initializer -SourceCustomerDatabase Step15SourceCustomer `
        -SourceBusinessDatabase Step15SourceBusiness -CustomerDatabase Step15TargetCustomer `
        -BusinessDatabase Step15TargetBusiness -CustomerJwtSecret ('c' * 32) `
        -BusinessJwtSecret ('b' * 32) -ValidateOnly | Out-Null
    Write-Host '[PASS] Initializer accepts distinct safe local inputs'

    $rejected = $false
    try {
        & $initializer -SourceCustomerDatabase SameDatabase `
            -SourceBusinessDatabase OtherSource -CustomerDatabase SameDatabase `
            -BusinessDatabase OtherTarget -CustomerJwtSecret ('c' * 32) `
            -BusinessJwtSecret ('b' * 32) -ValidateOnly | Out-Null
    }
    catch { $rejected = $_.Exception.Message -match 'must all be distinct' }
    if (-not $rejected) { throw 'Initializer accepted identical source and target databases.' }
    Write-Host '[PASS] Initializer rejects identical source and target databases'

    Sql master "CREATE DATABASE [$customer];"
    $created.Add($customer)
    Sql master "CREATE DATABASE [$business];"
    $created.Add($business)
    $user = [guid]::NewGuid()
    $businessUser = [guid]::NewGuid()
    $company = [guid]::NewGuid()
    $device = [guid]::NewGuid()
    Sql $customer @"
CREATE TABLE CustomerConfigurations(Id uniqueidentifier PRIMARY KEY,IsActive bit,DisplayNameAr nvarchar(20),DisplayNameHe nvarchar(20));
CREATE TABLE CustomerDevices(
    Id uniqueidentifier PRIMARY KEY,
    InstallationId uniqueidentifier,
    Platform nvarchar(20),
    AppVersion nvarchar(20),
    TokenHash varbinary(32),
    CreatedAt datetimeoffset,
    UpdatedAt datetimeoffset,
    LastSeenAt datetimeoffset NULL,
    ExpiresAt datetimeoffset,
    RowVersion rowversion);
CREATE TABLE AspNetUsers(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE CheckoutDrafts(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE CustomerBookings(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE CustomerPayments(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE CustomerPaymentIdempotencyRecords(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE CustomerInternalServiceNonces(Id uniqueidentifier PRIMARY KEY,Nonce nvarchar(100));
CREATE TABLE CustomerInternalIdempotencyRecords(Id uniqueidentifier PRIMARY KEY,IdempotencyKey nvarchar(100));
CREATE TABLE StripeWebhookEvents(Id uniqueidentifier PRIMARY KEY,EventId nvarchar(100));
CREATE TABLE BookingConfirmationAttempts(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE ProcessedBookingStatusMessages(Id uniqueidentifier PRIMARY KEY);
INSERT CustomerConfigurations VALUES('15000000-0000-4000-8000-000000000020',1,N'ar',N'he');
INSERT CustomerDevices
    (Id,InstallationId,Platform,AppVersion,TokenHash,CreatedAt,UpdatedAt,LastSeenAt,ExpiresAt)
VALUES
    ('$device',NEWID(),N'android',N'self-test',0x01,SYSUTCDATETIME(),
     SYSUTCDATETIME(),NULL,DATEADD(day,1,SYSUTCDATETIME()));
INSERT AspNetUsers VALUES('$user');
"@
    Sql $business @"
CREATE TABLE BusinessUserAssignments(Id uniqueidentifier PRIMARY KEY,UserId uniqueidentifier,CompanyId uniqueidentifier,IsActive bit);
CREATE TABLE Companies(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE ServiceCategories(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE ServiceOfferings(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE BranchAvailabilitySettings(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE BranchRecurringSchedules(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE BranchAvailabilityOverrides(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE BranchServiceAreas(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE AppointmentReservations(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE WorkOrders(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE BookingStatusOutboxMessages(Id uniqueidentifier PRIMARY KEY);
CREATE TABLE InternalServiceNonces(Id uniqueidentifier PRIMARY KEY,Nonce nvarchar(100));
CREATE TABLE InternalServiceIdempotencyRecords(Id uniqueidentifier PRIMARY KEY,IdempotencyKey nvarchar(100));
CREATE TABLE BookingStatusRequeueHistory(Id uniqueidentifier PRIMARY KEY,RequestId nvarchar(100));
INSERT Companies VALUES('$company');
INSERT BusinessUserAssignments VALUES(NEWID(),'$businessUser','$company',1);
"@
    $customerTables = @('CustomerDevices','CustomerConfigurations','CheckoutDrafts',
        'CustomerBookings','CustomerPayments','CustomerPaymentIdempotencyRecords',
        'BookingConfirmationAttempts',
        'CustomerInternalServiceNonces','CustomerInternalIdempotencyRecords')
    $businessTables = @('Companies','ServiceCategories','ServiceOfferings',
        'BranchAvailabilitySettings','BranchRecurringSchedules',
        'BranchAvailabilityOverrides','BranchServiceAreas','AppointmentReservations',
        'WorkOrders','BookingStatusOutboxMessages','BookingStatusRequeueHistory',
        'InternalServiceNonces','InternalServiceIdempotencyRecords')
    $variables = [ordered]@{
        fixtureMarker = 'STEP15_LOCAL_ONLY'
        customerDatabase = $customer
        businessDatabase = $business
        customerDeviceId = $device.ToString('D')
        customerUserId = $user.ToString('D')
        businessUserId = $businessUser.ToString('D')
        companyId = $company.ToString('D')
        expectedCustomerEvidence = Evidence $customer $customerTables
        expectedBusinessEvidence = Evidence $business $businessTables
        expectedCustomerDrafts = @()
        expectedCustomerBookings = @()
        expectedCustomerPayments = @()
        expectedBusinessCompanies = @([ordered]@{ id = $company.ToString('D') })
        expectedBusinessWorkOrders = @()
        expectedBusinessOutbox = @()
        expectedManifestEntryCount = 90
    }
    $variables | ConvertTo-Json -Depth 8 | Set-Content $variablesPath -Encoding UTF8
    Run-Verifier $true 'Verifier accepts exact Customer and Business fixture evidence'
    Sql $customer "UPDATE CustomerDevices SET LastSeenAt=SYSUTCDATETIME();"
    Run-Verifier $true 'Verifier permits only the expected device access timestamp mutation'
    Sql $business "INSERT ServiceOfferings VALUES(NEWID());"
    Run-Verifier $false 'Verifier rejects a changed Business catalog identity snapshot'
    Sql $business "DELETE FROM ServiceOfferings;"
    Run-Verifier $true 'Verifier returns green after evidence restoration'
}
finally {
    Remove-Item $variablesPath -Force -ErrorAction SilentlyContinue
    $drop = $created.ToArray()
    [array]::Reverse($drop)
    foreach ($database in $drop) {
        Sql master "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database];"
    }
}
