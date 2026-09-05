#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceCustomerDatabase,
    [string]$Server = '(localdb)\MSSQLLocalDB',
    [string]$RunId = ([guid]::NewGuid().ToString('N')),
    [int]$Port = 50841,
    [switch]$SkipBuild,
    [switch]$KeepDatabase,
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

if (-not $Execute) {
    throw 'Step 18 live-local execution requires explicit -Execute.'
}
if ($SourceCustomerDatabase -notmatch '^[A-Za-z0-9_]+$') {
    throw 'Unsafe source database name.'
}
if ($Port -lt 1024 -or $Port -gt 65535) {
    throw 'Port must be between 1024 and 65535.'
}
if ([Environment]::GetEnvironmentVariable('ASPNETCORE_ENVIRONMENT') -eq
    'Production') {
    throw 'Step 18 local fixtures cannot run in Production.'
}

$solution = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifacts = Join-Path $PSScriptRoot 'artifacts'
$manifest = Join-Path $PSScriptRoot `
    'plans\step-18-lahza-payment-migration.manifest.json'
$initializer = Join-Path $PSScriptRoot 'Initialize-Step18Fixtures.ps1'
$databaseVerifier = Join-Path $PSScriptRoot `
    'Verify-Step18DatabaseInvariants.ps1'
$harness = Join-Path $PSScriptRoot 'Invoke-HttpTests.ps1'
$validator = Join-Path $PSScriptRoot 'Test-Step18LiveAssets.ps1'
$suffix = ($RunId -replace '[^A-Za-z0-9]', '')
if ($suffix.Length -gt 16) { $suffix = $suffix.Substring(0, 16) }
$customerDatabase = "GhseeliStep18_$suffix"
$variablesPath = Join-Path $artifacts "step-18-$suffix.variables.local.json"
$resultsPath = Join-Path $artifacts "step-18-$suffix.results.local.json"
$baseUrl = "http://127.0.0.1:$Port"
$process = $null

function New-RandomText([int]$ByteCount) {
    $bytes = [byte[]]::new($ByteCount)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) }
    finally { $generator.Dispose() }
    return [Convert]::ToBase64String($bytes).
        TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Quote([string]$Value) {
    return '"' + $Value.Replace('"', '\"') + '"'
}

function Start-CustomerHost([string]$SecretKey) {
    $project = Join-Path $solution 'GhseeliApis\GhseeliApis.csproj'
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'dotnet'
    $start.Arguments =
        "run --project $(Quote $project) -c Release --no-build --no-launch-profile"
    $start.WorkingDirectory = $solution
    $start.UseShellExecute = $false
    $environment = @{
        ASPNETCORE_ENVIRONMENT = 'Development'
        ASPNETCORE_URLS = $baseUrl
        ConnectionStrings__CustomerConnection =
            "Server=$Server;Database=$customerDatabase;Integrated Security=true;TrustServerCertificate=true"
        JwtSettings__SecretKey = $script:jwtSecret
        JwtSettings__Issuer = 'GhseeliApis'
        JwtSettings__Audience = 'GhseeliApis'
        Lahza__BaseUrl = 'https://api.lahza.io'
        Lahza__SecretKey = $SecretKey
        Lahza__CallbackUrl = 'https://localhost.invalid/payment/callback'
        Logging__LogLevel__Default = 'Warning'
        Logging__LogLevel__Microsoft = 'Warning'
        Step17TestFixtures__Enabled = 'true'
        Step17TestFixtures__PaymentGateway = 'DeterministicFake'
        RateLimiting__PaymentAggregatePermitLimit = '10000'
        RateLimiting__PaymentIntentPermitLimit = '10000'
        RateLimiting__DevicePermitLimit = '10000'
        RateLimiting__BearerMutationPermitLimit = '10000'
        RateLimiting__AnonymousPermitLimit = '10000'
        RateLimiting__ValidPaymentWebhookPermitLimit = '10000'
        RateLimiting__InvalidPaymentWebhookPermitLimit = '10000'
    }
    foreach ($entry in $environment.GetEnumerator()) {
        $start.EnvironmentVariables[[string]$entry.Key] = [string]$entry.Value
    }
    $owned = [Diagnostics.Process]::new()
    $owned.StartInfo = $start
    if (-not $owned.Start()) {
        throw 'Could not start the Step 18 customer host.'
    }
    return $owned
}

function Stop-CustomerHost {
    if ($null -eq $script:process) { return }
    $script:process.Refresh()
    if (-not $script:process.HasExited) {
        Stop-Process -Id $script:process.Id
        if (-not $script:process.WaitForExit(15000)) {
            throw "Owned customer process $($script:process.Id) did not stop."
        }
    }
    $script:process.Dispose()
    $script:process = $null
}

function Wait-Ready([Diagnostics.Process]$OwnedProcess) {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(2)
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    try {
        do {
            $OwnedProcess.Refresh()
            if ($OwnedProcess.HasExited) {
                throw 'Step 18 customer host exited before readiness.'
            }
            try {
                $response = $client.GetAsync("$baseUrl/api/Health").
                    GetAwaiter().GetResult()
                try {
                    if ([int]$response.StatusCode -lt 500) { return }
                }
                finally { $response.Dispose() }
            }
            catch { Start-Sleep -Milliseconds 500 }
        } while ([DateTime]::UtcNow -lt $deadline)
    }
    finally {
        $client.Dispose()
        $handler.Dispose()
    }
    throw 'Step 18 customer host did not become ready.'
}

function Invoke-ManifestTags([string[]]$Tags, [string]$Name) {
    $phaseResults = Join-Path $artifacts `
        "step-18-$suffix-$Name.results.local.json"
    $global:LASTEXITCODE = 0
    & $harness `
        -ManifestPath $manifest `
        -BaseUrl $baseUrl `
        -VariablesPath $variablesPath `
        -Tags $Tags `
        -ResultsPath $phaseResults
    if ($LASTEXITCODE -ne 0) {
        throw "Step 18 $Name HTTP scenarios failed."
    }
    return $phaseResults
}

function Drop-CustomerDatabase {
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$Server;Database=master;Integrated Security=true;TrustServerCertificate=true")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = @"
IF DB_ID(N'$customerDatabase') IS NOT NULL
BEGIN
    ALTER DATABASE [$customerDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$customerDatabase];
END;
"@
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$jwtSecret = New-RandomText 48
$webhookSecret = New-RandomText 48
$configuredResults = $null
$unconfiguredResults = $null

try {
    & $validator
    if (-not $?) { throw 'Step 18 asset validation failed.' }

    if (-not $SkipBuild) {
        & dotnet build (Join-Path $solution 'GhseeliApis.sln') `
            --configuration Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    }

    & $initializer `
        -SourceCustomerDatabase $SourceCustomerDatabase `
        -CustomerDatabase $customerDatabase `
        -JwtSecret $jwtSecret `
        -WebhookSecret $webhookSecret `
        -VariablesPath $variablesPath `
        -Server $Server | Out-Null

    $process = Start-CustomerHost $webhookSecret
    Wait-Ready $process
    $configuredResults = Invoke-ManifestTags @('configured') 'configured'
    Stop-CustomerHost

    $process = Start-CustomerHost ''
    Wait-Ready $process
    $unconfiguredResults = Invoke-ManifestTags @('unconfigured') 'unconfigured'
    Stop-CustomerHost
    & $databaseVerifier `
        -CustomerDatabase $customerDatabase `
        -VariablesPath $variablesPath `
        -Server $Server
    if (-not $?) {
        throw 'Step 18 database invariant verification failed.'
    }

    [pscustomobject]@{
        runId = $RunId
        configuredResults = $configuredResults
        unconfiguredResults = $unconfiguredResults
        databaseRemoved = -not $KeepDatabase
        realLahzaCredentialsUsed = $false
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $resultsPath -Encoding UTF8
    Write-Host "Step 18 live-local scenarios passed. Evidence: $resultsPath"
}
finally {
    Stop-CustomerHost
    Remove-Item -LiteralPath $variablesPath -Force -ErrorAction SilentlyContinue
    if (-not $KeepDatabase) {
        Drop-CustomerDatabase
    }
}
