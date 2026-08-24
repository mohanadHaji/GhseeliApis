#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Validate','Provision','CustomerFirst','BusinessFirst',
        'Parallel','Repeat','ResetCustomer','ResetBusiness',
        'RunHealthy','ProductionFailures','OutageRecovery',
        'RestartConcurrent','MissingTables','RunTag','Cleanup','All')]
    [string]$Phase = 'Validate',
    [string]$RunId = ([guid]::NewGuid().ToString('N')),
    [string]$Server = '(localdb)\MSSQLLocalDB',
    [string]$StatePath = (Join-Path $PSScriptRoot 'artifacts\step-16.state.local.json'),
    [string]$Tag,
    [string]$VariablesOverlayPath,
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$solution = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$manifest = Join-Path $PSScriptRoot 'plans\step-16-clean-schema-separation.manifest.json'
$plan = Join-Path $solution 'STEP_16_HTTP_TEST_PLAN.md'
$validator = Join-Path $PSScriptRoot 'Test-Step16LiveAssets.ps1'
$initializer = Join-Path $PSScriptRoot 'Initialize-Step16Fixtures.ps1'
$verifier = Join-Path $PSScriptRoot 'Verify-Step16DatabaseInvariants.ps1'
$cleanup = Join-Path $PSScriptRoot 'Remove-Step16Fixtures.ps1'
$harness = Join-Path $PSScriptRoot 'Invoke-HttpTests.ps1'
$harnessModule = Join-Path $PSScriptRoot 'HttpTestHarness.psm1'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$lifecycleEvidence = [Collections.Generic.List[object]]::new()
$evidencePath = Join-Path $artifacts `
    "step16-lifecycle-$($RunId.Substring(0,12)).local.json"

function Assert-IgnoredArtifactPath([string]$path,[string]$parameterName) {
    if ([string]::IsNullOrWhiteSpace($path)) { return }
    $resolvedPath =
        $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($path)
    $resolvedArtifacts =
        $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($artifacts)
    if (-not [string]::Equals(
            (Split-Path -Parent $resolvedPath),
            $resolvedArtifacts,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$parameterName must be a file directly inside the ignored artifacts directory."
    }
}

Assert-IgnoredArtifactPath $StatePath 'StatePath'
Assert-IgnoredArtifactPath $VariablesOverlayPath 'VariablesOverlayPath'

function Add-LifecycleEvidence(
    [string]$name,[string]$status,[object]$details = $null) {
    $entry = [ordered]@{
            phase = $name
            status = $status
            completedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        }
    if ($null -ne $details) { $entry.details = $details }
    $lifecycleEvidence.Add([pscustomobject]$entry)
    New-Item -ItemType Directory $artifacts -Force | Out-Null
    [ordered]@{
        fixtureMarker = 'STEP16_DISPOSABLE_LOCAL_ONLY'
        runIdSuffix = $RunId.Substring(0,12)
        phases = $lifecycleEvidence.ToArray()
    } | ConvertTo-Json -Depth 6 | Set-Content $evidencePath -Encoding UTF8
}

& $validator -PlanPath $plan -ManifestPath $manifest
& $initializer -RunId $RunId -Server $Server -ValidateOnly
if ($Phase -eq 'Validate') {
    Write-Host 'Step 16 static validation completed; no host or database was changed.'
    return
}
if (-not $Execute) {
    throw 'Live lifecycle phases require -Execute after reviewing the run-scoped local inputs.'
}

function Merge-Variables {
    if ([string]::IsNullOrWhiteSpace($VariablesOverlayPath)) { return }
    $state = Get-Content $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $overlay = Get-Content $VariablesOverlayPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($property in $overlay.psobject.Properties) {
        $state | Add-Member -NotePropertyName $property.Name `
            -NotePropertyValue $property.Value -Force
    }
    $state | ConvertTo-Json -Depth 20 | Set-Content $StatePath -Encoding UTF8
}
function Quote([string]$value) { return '"' + $value.Replace('"','\"') + '"' }
function Start-Host(
    [string]$name,[string]$project,[string]$urls,[hashtable]$environment) {
    New-Item -ItemType Directory $artifacts -Force | Out-Null
    $stdout = Join-Path $artifacts "step16-$name-$($RunId.Substring(0,12)).stdout.local.txt"
    $stderr = Join-Path $artifacts "step16-$name-$($RunId.Substring(0,12)).stderr.local.txt"
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'dotnet'
    $start.WorkingDirectory = $solution
    $start.Arguments = "run --project $(Quote $project) -c Release --no-build --no-launch-profile"
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $false
    $start.RedirectStandardError = $false
    $start.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Development'
    $start.EnvironmentVariables['ASPNETCORE_URLS'] = $urls
    if ($project.EndsWith(
            'Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj',
            [StringComparison]::OrdinalIgnoreCase)) {
        $businessJwtSecret = [Environment]::GetEnvironmentVariable(
            'STEP16_BUSINESS_JWT_SECRET')
        if (-not [string]::IsNullOrWhiteSpace($businessJwtSecret)) {
            $start.EnvironmentVariables['BusinessJwtSettings__SecretKey'] =
                $businessJwtSecret
            $start.EnvironmentVariables['BusinessJwtSettings__Issuer'] =
                'Ghseeli.BusinessApi'
            $start.EnvironmentVariables['BusinessJwtSettings__Audience'] =
                'Ghseeli.BusinessClients'
        }
        $businessCallbackServiceId = [Environment]::GetEnvironmentVariable(
            'STEP16_BUSINESS_TO_CUSTOMER_SERVICE_ID')
        $businessCallbackSecret = [Environment]::GetEnvironmentVariable(
            'STEP16_BUSINESS_TO_CUSTOMER_HMAC_SECRET')
        if (-not [string]::IsNullOrWhiteSpace($businessCallbackServiceId) -and
            -not [string]::IsNullOrWhiteSpace($businessCallbackSecret)) {
            $start.EnvironmentVariables['CustomerBookingStatusClient__BaseUrl'] =
                'https://localhost:54431'
            $start.EnvironmentVariables['CustomerBookingStatusClient__ServiceId'] =
                $businessCallbackServiceId
            $start.EnvironmentVariables['CustomerBookingStatusClient__ActiveSecret'] =
                $businessCallbackSecret
        }
    }
    elseif ($project.EndsWith(
            'GhseeliApis\GhseeliApis.csproj',
            [StringComparison]::OrdinalIgnoreCase)) {
        $customerJwtSecret = [Environment]::GetEnvironmentVariable(
            'STEP16_CUSTOMER_JWT_SECRET')
        if (-not [string]::IsNullOrWhiteSpace($customerJwtSecret)) {
            $start.EnvironmentVariables['JwtSettings__SecretKey'] =
                $customerJwtSecret
            $start.EnvironmentVariables['JwtSettings__Issuer'] = 'GhseeliApis'
            $start.EnvironmentVariables['JwtSettings__Audience'] = 'GhseeliApis'
        }
    }
    foreach ($item in $environment.GetEnumerator()) {
        $start.EnvironmentVariables[[string]$item.Key] = [string]$item.Value
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (-not $process.Start()) { throw "Could not start $name." }
    $processes.Add($process)
    return $process
}
function Stop-OwnedProcess([Diagnostics.Process]$process) {
    if ($null -eq $process) { return }
    $process.Refresh()
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id
        if (-not $process.WaitForExit(15000)) {
            throw "Owned process $($process.Id) did not stop."
        }
    }
    $process.Dispose()
}
function Stop-AllOwnedHosts {
    for ($i = $processes.Count - 1; $i -ge 0; $i--) {
        Stop-OwnedProcess $processes[$i]
    }
    $processes.Clear()
}
function Wait-Ready([string]$url,[Diagnostics.Process]$process) {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.ServerCertificateCustomValidationCallback =
        [Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(2)
    try {
        do {
            $process.Refresh()
            if ($process.HasExited) { throw "Host exited before readiness at $url." }
            try {
                $response = $client.GetAsync($url).GetAwaiter().GetResult()
                try {
                    if ($response.IsSuccessStatusCode) { return }
                }
                finally { $response.Dispose() }
                Start-Sleep -Milliseconds 500
            }
            catch { Start-Sleep -Milliseconds 500 }
        } while ([DateTime]::UtcNow -lt $deadline)
    }
    finally { $client.Dispose() }
    throw "Host did not become ready at $url."
}

function Wait-Responding([string]$url,[Diagnostics.Process]$process) {
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.ServerCertificateCustomValidationCallback =
        [Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(5)
    try {
        do {
            $process.Refresh()
            if ($process.HasExited) { throw "Host exited before responding at $url." }
            try {
                $response = $client.GetAsync($url).GetAwaiter().GetResult()
                $response.Dispose()
                return
            }
            catch { Start-Sleep -Milliseconds 500 }
        } while ([DateTime]::UtcNow -lt $deadline)
    }
    finally { $client.Dispose() }
    throw "Host did not begin responding at $url."
}
function Invoke-Step16Sql([string]$database,[string]$sql) {
    $state = Get-Content $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$($state.server);Database=$database;Integrated Security=true;TrustServerCertificate=true")
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $sql
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}
function Set-Step16DatabaseOnline([string]$database,[bool]$online) {
    [Data.SqlClient.SqlConnection]::ClearAllPools()
    $escaped = $database.Replace(']',']]')
    $sql = if ($online) {
        "ALTER DATABASE [$escaped] SET ONLINE; ALTER DATABASE [$escaped] SET MULTI_USER;"
    } else {
        "ALTER DATABASE [$escaped] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ALTER DATABASE [$escaped] SET OFFLINE;"
    }
    Invoke-Step16Sql 'master' $sql
}
function Assert-FinalCleanup([object]$priorState) {
    if (Test-Path -LiteralPath $StatePath) {
        throw 'Step 16 cleanup state still exists.'
    }
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$($priorState.server);Database=master;Integrated Security=true;TrustServerCertificate=true")
    try {
        $connection.Open()
        foreach ($database in @(
                $priorState.customerDatabase,$priorState.businessDatabase,
                $priorState.wrongDatabase)) {
            $command = $connection.CreateCommand()
            $command.CommandText =
                'SELECT COUNT(*) FROM sys.databases WHERE name=@name'
            [void]$command.Parameters.AddWithValue('@name',$database)
            if ([int]$command.ExecuteScalar() -ne 0) {
                throw "Disposable database '$database' remains after cleanup."
            }
        }
    }
    finally { $connection.Dispose() }
    $listenerCount = 0
    foreach ($port in @(54431,54432,50832,54433,54434,54435,54436)) {
        $listenerCount += @(Get-NetTCPConnection -LocalPort $port -State Listen `
            -ErrorAction SilentlyContinue).Count
    }
    if ($listenerCount -ne 0 -or $processes.Count -ne 0) {
        throw 'Owned process or listener remains after Step 16 cleanup.'
    }
    Add-LifecycleEvidence 'cleanup-isolation' 'passed' ([ordered]@{
            ownedProcessCount = 0
            listenerCount = 0
            runDatabaseCount = 0
            stateFileAbsent = $true
        })
}
function Assert-MissingConnectionFailsClosed(
            [string]$name,[string]$project,[string]$url,[string]$healthUrl,
            [string]$connectionKey) {
            $process = Start-Host $name $project $url @{
                ASPNETCORE_ENVIRONMENT = 'Production'
                $connectionKey = ''
}
            $deadline = [DateTime]::UtcNow.AddSeconds(15)
            $failedClosed = $false
            do {
                $process.Refresh()
                if ($process.HasExited) { $failedClosed = $true; break }
                try {
                    $request = [Net.HttpWebRequest]::Create($healthUrl)
                    $request.Timeout = 1500
                    $request.ServerCertificateValidationCallback = { $true }
                    $response = $request.GetResponse()
                    $status = [int]$response.StatusCode
                    $response.Dispose()
                    if ($status -ge 500) { $failedClosed = $true; break }
                    if ($status -lt 500) {
                        throw "$name became ready without its owned connection."
                    }
                }
                catch [Net.WebException] {
                    if ($null -ne $_.Exception.Response) {
                        $status = [int]$_.Exception.Response.StatusCode
                        $_.Exception.Response.Dispose()
                        if ($status -ge 500) { $failedClosed = $true; break }
                    }
                }
                Start-Sleep -Milliseconds 500
            } while ([DateTime]::UtcNow -lt $deadline)
            Stop-OwnedProcess $process
            [void]$processes.Remove($process)
            if (-not $failedClosed) {
                throw "$name did not fail closed for missing owned connection."
            }
        }
function Invoke-ManifestTag([string]$selectedTag) {
    $results = Join-Path $artifacts `
        "step16-$selectedTag-$($RunId.Substring(0,12)).results.local.json"
    & $harness -ManifestPath $manifest -BaseUrl 'https://localhost:54431' `
        -VariablesPath $StatePath -Tags $selectedTag -ResultsPath $results
    if ($LASTEXITCODE -ne 0) { throw "Manifest tag '$selectedTag' failed." }
}
function Initialize-RuntimeVariables {
    foreach ($required in @('STEP16_CUSTOMER_PASSWORD','STEP16_BUSINESS_PASSWORD')) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($required))) {
            throw "Runtime fixture setup requires environment variable '$required'."
        }
    }
    $results = Join-Path $artifacts `
        "step16-fixture-setup-$($RunId.Substring(0,12)).results.local.json"
    Import-Module $harnessModule -Force
    $result = Invoke-HttpTestHarness -ManifestPath $manifest `
        -BaseUrl 'https://localhost:54431' -VariablesPath $StatePath `
        -Tags 'fixture-setup' -ResultsPath $results
    if (-not $result.Summary.passedAll) {
        throw 'Runtime HTTP fixture setup failed.'
    }
    $state = Get-Content $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($name in @(
            'customerJwt','customerUserId','businessJwt','businessUserId',
            'companyId','initialDeviceToken','customerDeviceToken',
            'rotatedDeviceToken','customerDeviceId')) {
        if (-not $result.RuntimeVariables.ContainsKey($name)) {
            throw "Runtime fixture setup did not produce '$name'."
        }
        $state | Add-Member -NotePropertyName $name `
            -NotePropertyValue $result.RuntimeVariables[$name] -Force
    }
    $state | ConvertTo-Json -Depth 20 | Set-Content $StatePath -Encoding UTF8
}
function Ensure-RuntimeVariables {
    $state = Get-Content $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($name in @('customerJwt','businessJwt','customerDeviceToken')) {
        $property = $state.psobject.Properties[$name]
        if ($null -eq $property -or
            [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            Initialize-RuntimeVariables
            return
        }
    }
}
function Assert-RuntimeCredentialOverlay {
    $state = Get-Content $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($name in @('customerAdminJwt','obsoleteCompanyJwt')) {
        $property = $state.psobject.Properties[$name]
        if ($null -eq $property -or
            [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw "Ignored runtime overlay must provide '$name'."
        }
    }
}
function Assert-HmacFixtureEnvironment {
    foreach ($required in @(
            'STEP16_CUSTOMER_TO_BUSINESS_SERVICE_ID',
            'STEP16_CUSTOMER_TO_BUSINESS_HMAC_SECRET',
            'STEP16_CUSTOMER_TO_BUSINESS_CATALOG_HMAC_SECRET',
            'STEP16_BUSINESS_TO_CUSTOMER_SERVICE_ID',
            'STEP16_BUSINESS_TO_CUSTOMER_HMAC_SECRET',
            'STEP16_BUSINESS_TO_CUSTOMER_RECONCILE_SERVICE_ID',
            'STEP16_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET',
            'STEP16_BUSINESS_JWT_SECRET')) {
        if ([string]::IsNullOrWhiteSpace(
                [Environment]::GetEnvironmentVariable($required))) {
            throw "Runtime HMAC setup requires environment variable '$required'."
        }
    }
    $customerFullSecret = [Environment]::GetEnvironmentVariable(
        'STEP16_CUSTOMER_TO_BUSINESS_HMAC_SECRET')
    $customerCatalogSecret = [Environment]::GetEnvironmentVariable(
        'STEP16_CUSTOMER_TO_BUSINESS_CATALOG_HMAC_SECRET')
    if ($customerFullSecret -ceq $customerCatalogSecret) {
        throw 'Full and catalog-only Customer-to-Business credentials must use distinct secrets.'
    }
    $callbackServiceId = [Environment]::GetEnvironmentVariable(
        'STEP16_BUSINESS_TO_CUSTOMER_SERVICE_ID')
    $callbackSecret = [Environment]::GetEnvironmentVariable(
        'STEP16_BUSINESS_TO_CUSTOMER_HMAC_SECRET')
    $reconcileServiceId = [Environment]::GetEnvironmentVariable(
        'STEP16_BUSINESS_TO_CUSTOMER_RECONCILE_SERVICE_ID')
    $reconcileSecret = [Environment]::GetEnvironmentVariable(
        'STEP16_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET')
    if ($callbackServiceId -ceq $reconcileServiceId) {
        throw 'Callback and reconciliation credentials must use distinct service IDs.'
    }
    if ($callbackSecret -ceq $reconcileSecret) {
        throw 'Callback and reconciliation credentials must use distinct secrets.'
    }
}
function Start-HealthyHosts {
    Assert-HmacFixtureEnvironment
    $state = Get-Content $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    Start-CustomerHost $state | Out-Null
    Start-BusinessHost $state | Out-Null
}
function Start-CustomerHost(
    [object]$state,
    [string]$name = 'customer',
    [string]$url = 'https://localhost:54431',
    [string]$database = $state.customerDatabase,
    [hashtable]$extraEnvironment = @{}) {
    $customerConnection =
        "Server=$($state.server);Database=$database;Integrated Security=true;TrustServerCertificate=true"
    $environment = @{ ConnectionStrings__CustomerConnection = $customerConnection }
    $environment['CustomerInternalServiceAuthentication__AllowInsecureHttpInDevelopment'] =
        'false'
    $customerJwtSecret = [Environment]::GetEnvironmentVariable(
        'STEP16_CUSTOMER_JWT_SECRET')
    if (-not [string]::IsNullOrWhiteSpace($customerJwtSecret)) {
        $environment['JwtSettings__SecretKey'] = $customerJwtSecret
        $environment['JwtSettings__Issuer'] = 'GhseeliApis'
        $environment['JwtSettings__Audience'] = 'GhseeliApis'
    }
    $customerBusinessServiceId = [Environment]::GetEnvironmentVariable(
        'STEP16_CUSTOMER_TO_BUSINESS_SERVICE_ID')
    $customerBusinessSecret = [Environment]::GetEnvironmentVariable(
        'STEP16_CUSTOMER_TO_BUSINESS_HMAC_SECRET')
    if (-not [string]::IsNullOrWhiteSpace($customerBusinessServiceId) -and
        -not [string]::IsNullOrWhiteSpace($customerBusinessSecret)) {
        $environment['BusinessApiClient__BaseUrl'] = 'https://localhost:54432'
        $environment['BusinessApiClient__ServiceId'] = $customerBusinessServiceId
        $environment['BusinessApiClient__ActiveSecret'] = $customerBusinessSecret
        $environment['BusinessApiClient__AllowUntrustedDevelopmentCertificate'] =
            'true'
    }
    $businessServiceId = [Environment]::GetEnvironmentVariable(
        'STEP16_BUSINESS_TO_CUSTOMER_SERVICE_ID')
    $businessSecret = [Environment]::GetEnvironmentVariable(
        'STEP16_BUSINESS_TO_CUSTOMER_HMAC_SECRET')
    if (-not [string]::IsNullOrWhiteSpace($businessServiceId) -and
        -not [string]::IsNullOrWhiteSpace($businessSecret)) {
        $environment['CustomerInternalServiceAuthentication__Services__0__ServiceId'] =
            $businessServiceId
        $environment['CustomerInternalServiceAuthentication__Services__0__ActiveSecret'] =
            $businessSecret
        $environment['CustomerInternalServiceAuthentication__Services__0__AllowedOperations__0'] =
            'booking_status_callback'
    }
    $reconcileServiceId = [Environment]::GetEnvironmentVariable(
        'STEP16_BUSINESS_TO_CUSTOMER_RECONCILE_SERVICE_ID')
    $reconcileSecret = [Environment]::GetEnvironmentVariable(
        'STEP16_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET')
    if (-not [string]::IsNullOrWhiteSpace($reconcileServiceId) -and
        -not [string]::IsNullOrWhiteSpace($reconcileSecret)) {
        $environment['CustomerInternalServiceAuthentication__Services__1__ServiceId'] =
            $reconcileServiceId
        $environment['CustomerInternalServiceAuthentication__Services__1__ActiveSecret'] =
            $reconcileSecret
        $environment['CustomerInternalServiceAuthentication__Services__1__AllowedOperations__0'] =
            'booking_status_reconcile'
        $environment['CustomerInternalServiceAuthentication__Services__1__AllowedOperations__1'] =
            'booking_status_read'
    }
    foreach ($item in $extraEnvironment.GetEnumerator()) {
        $environment[$item.Key] = $item.Value
    }
    $customer = Start-Host $name `
        (Join-Path $solution 'GhseeliApis\GhseeliApis.csproj') `
        $url $environment
    Wait-Ready "$url/api/Health" $customer
    return $customer
}
function Start-BusinessHost(
    [object]$state,
    [string]$name = 'business',
    [string]$url = 'https://localhost:54432;http://localhost:50832',
    [string]$readinessUrl = 'https://localhost:54432',
    [string]$database = $state.businessDatabase,
    [hashtable]$extraEnvironment = @{}) {
    $businessConnection =
        "Server=$($state.server);Database=$database;Integrated Security=true;TrustServerCertificate=true"
    $environment = @{ ConnectionStrings__BusinessConnection = $businessConnection }
    $environment['InternalServiceAuthentication__AllowInsecureHttpInDevelopment'] =
        'false'
    $businessJwtSecret = [Environment]::GetEnvironmentVariable(
        'STEP16_BUSINESS_JWT_SECRET')
    if (-not [string]::IsNullOrWhiteSpace($businessJwtSecret)) {
        $environment['BusinessJwtSettings__SecretKey'] = $businessJwtSecret
        $environment['BusinessJwtSettings__Issuer'] = 'Ghseeli.BusinessApi'
        $environment['BusinessJwtSettings__Audience'] = 'Ghseeli.BusinessClients'
    }
    $customerServiceId = [Environment]::GetEnvironmentVariable(
        'STEP16_CUSTOMER_TO_BUSINESS_SERVICE_ID')
    $customerSecret = [Environment]::GetEnvironmentVariable(
        'STEP16_CUSTOMER_TO_BUSINESS_HMAC_SECRET')
    $catalogSecret = [Environment]::GetEnvironmentVariable(
        'STEP16_CUSTOMER_TO_BUSINESS_CATALOG_HMAC_SECRET')
    if (-not [string]::IsNullOrWhiteSpace($customerServiceId) -and
        -not [string]::IsNullOrWhiteSpace($customerSecret) -and
        -not [string]::IsNullOrWhiteSpace($catalogSecret)) {
        $environment['InternalServiceAuthentication__Services__0__ServiceId'] =
            $customerServiceId
        $environment['InternalServiceAuthentication__Services__0__ActiveSecret'] =
            $customerSecret
        $environment['InternalServiceAuthentication__Services__0__AllowedOperations__0'] =
            'catalog_snapshot'
        $environment['InternalServiceAuthentication__Services__0__AllowedOperations__1'] =
            'appointment_validate'
        $environment['InternalServiceAuthentication__Services__0__AllowedOperations__2'] =
            'reservation_create'
        $environment['InternalServiceAuthentication__Services__0__AllowedOperations__3'] =
            'reservation_status_read'
        $environment['InternalServiceAuthentication__Services__1__ServiceId'] =
            "$customerServiceId-catalog"
        $environment['InternalServiceAuthentication__Services__1__ActiveSecret'] =
            $catalogSecret
        $environment['InternalServiceAuthentication__Services__1__AllowedOperations__0'] =
            'catalog_snapshot'
    }
    foreach ($item in $extraEnvironment.GetEnumerator()) {
        $environment[$item.Key] = $item.Value
    }
    $business = Start-Host $name `
        (Join-Path $solution 'Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj') `
        $url $environment
    Wait-Ready "$readinessUrl/api/health" $business
    return $business
}
function Invoke-ProductionAndFailurePhases {
    $state = Get-Content $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    Start-CustomerHost $state 'customer-production' 'https://localhost:54433' `
        $state.customerDatabase @{ ASPNETCORE_ENVIRONMENT='Production' } | Out-Null
    Start-BusinessHost $state 'business-production' 'https://localhost:54434' `
        'https://localhost:54434' $state.businessDatabase `
        @{ ASPNETCORE_ENVIRONMENT='Production' } | Out-Null
    Invoke-ManifestTag 'production'
    Stop-AllOwnedHosts
    Add-LifecycleEvidence 'production-swagger-and-root-disabled' 'passed'

    Assert-MissingConnectionFailsClosed 'customer-missing-connection' `
        (Join-Path $solution 'GhseeliApis\GhseeliApis.csproj') `
        'https://localhost:54433' 'https://localhost:54433/api/Health' `
        'ConnectionStrings__CustomerConnection'
    Add-LifecycleEvidence 'customer-missing-connection-failed-closed' 'passed'
    Assert-MissingConnectionFailsClosed 'business-missing-connection' `
        (Join-Path $solution 'Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj') `
        'https://localhost:54434' 'https://localhost:54434/api/health' `
        'ConnectionStrings__BusinessConnection'
    Add-LifecycleEvidence 'business-missing-connection-failed-closed' 'passed'

    $wrongConnection =
        "Server=$($state.server);Database=$($state.wrongDatabase);Integrated Security=true;TrustServerCertificate=true"
    $customerWrong = Start-Host 'customer-wrong-schema' `
        (Join-Path $solution 'GhseeliApis\GhseeliApis.csproj') `
        'https://localhost:54433' @{
            ASPNETCORE_ENVIRONMENT='Production'
            ConnectionStrings__CustomerConnection=$wrongConnection
        }
    $businessWrong = Start-Host 'business-wrong-schema' `
        (Join-Path $solution 'Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj') `
        'https://localhost:54434' @{
            ASPNETCORE_ENVIRONMENT='Production'
            ConnectionStrings__BusinessConnection=$wrongConnection
        }
    Wait-Responding 'https://localhost:54433/api/Health' $customerWrong
    Wait-Responding 'https://localhost:54434/api/health' $businessWrong
    Invoke-ManifestTag 'unmigrated'
    Stop-AllOwnedHosts
    Add-LifecycleEvidence 'empty-schema-failed-closed' 'passed'

    $customerCrossWired = Start-CustomerHost $state 'customer-wrong-schema' `
        'https://localhost:54433' $state.businessDatabase `
        @{ ASPNETCORE_ENVIRONMENT='Production' }
    $businessCrossWired = Start-Host 'business-wrong-schema' `
        (Join-Path $solution 'Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj') `
        'https://localhost:54434' @{
            ASPNETCORE_ENVIRONMENT='Production'
            ConnectionStrings__BusinessConnection=(
                "Server=$($state.server);Database=$($state.customerDatabase);Integrated Security=true;TrustServerCertificate=true")
        }
    Wait-Responding 'https://localhost:54433/api/Health/db' $customerCrossWired
    Wait-Responding 'https://localhost:54434/api/health' $businessCrossWired
    Invoke-ManifestTag 'wrong-schema'
    Stop-AllOwnedHosts
    & $verifier -StatePath $StatePath
    Add-LifecycleEvidence 'cross-wired-owned-schemas-failed-closed' 'passed' `
        ([ordered]@{
            wrongDatabaseTableCount = 0
            migrationHistoryAbsent = $true
            startupOrDbHealthFailedClosed = $true
        })

    $deniedPassword = [Environment]::GetEnvironmentVariable(
        'STEP16_DENIED_SQL_PASSWORD')
    if ([string]::IsNullOrWhiteSpace($deniedPassword)) {
        throw 'Login-denial phase requires STEP16_DENIED_SQL_PASSWORD.'
    }
    $customerDenied =
        "Server=$($state.server);Database=$($state.customerDatabase);User ID=Step16Denied;Password=$deniedPassword;TrustServerCertificate=true"
    $businessDenied =
        "Server=$($state.server);Database=$($state.businessDatabase);User ID=Step16Denied;Password=$deniedPassword;TrustServerCertificate=true"
    $customerDeniedBuilder = [Data.SqlClient.SqlConnectionStringBuilder]::new()
    $customerDeniedBuilder['Data Source'] = $state.server
    $customerDeniedBuilder['Initial Catalog'] = $state.customerDatabase
    $customerDeniedBuilder['User ID'] = 'Step16Denied'
    $customerDeniedBuilder['Password'] = $deniedPassword
    $customerDeniedBuilder['TrustServerCertificate'] = $true
    $customerDeniedBuilder['Connect Timeout'] = 2
    $customerDeniedBuilder['ConnectRetryCount'] = 0
    $businessDeniedBuilder = [Data.SqlClient.SqlConnectionStringBuilder]::new()
    $businessDeniedBuilder['Data Source'] = $state.server
    $businessDeniedBuilder['Initial Catalog'] = $state.businessDatabase
    $businessDeniedBuilder['User ID'] = 'Step16Denied'
    $businessDeniedBuilder['Password'] = $deniedPassword
    $businessDeniedBuilder['TrustServerCertificate'] = $true
    $businessDeniedBuilder['Connect Timeout'] = 2
    $businessDeniedBuilder['ConnectRetryCount'] = 0
    $customerLoginDenied = Start-Host 'customer-login-denied' `
        (Join-Path $solution 'GhseeliApis\GhseeliApis.csproj') `
        'https://localhost:54433' @{
            ASPNETCORE_ENVIRONMENT='Production'
            ConnectionStrings__CustomerConnection=$customerDeniedBuilder.ConnectionString
        }
    $businessLoginDenied = Start-Host 'business-login-denied' `
        (Join-Path $solution 'Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj') `
        'https://localhost:54434' @{
            ASPNETCORE_ENVIRONMENT='Production'
            ConnectionStrings__BusinessConnection=$businessDeniedBuilder.ConnectionString
            BookingStatusOutbox__FailureBackoffMilliseconds='60000'
        }
    Wait-Responding 'https://localhost:54433/api/Health' $customerLoginDenied
    Wait-Responding 'https://localhost:54434/api/health' $businessLoginDenied
    Invoke-ManifestTag 'login-denied-customer'
    Invoke-ManifestTag 'login-denied-business'
    Stop-AllOwnedHosts
    Add-LifecycleEvidence 'both-sql-principals-login-denied' 'passed'
}

function Invoke-OutageRecoveryAndDependencyPhases {
    $state = Get-Content $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $customer = Start-CustomerHost $state
    $business = Start-BusinessHost $state
    Ensure-RuntimeVariables
    try {
        Set-Step16DatabaseOnline $state.customerDatabase $false
        Invoke-ManifestTag 'customer-outage'
    }
    finally { Set-Step16DatabaseOnline $state.customerDatabase $true }
    Wait-Ready 'https://localhost:54431/api/Health/db' $customer
    Invoke-ManifestTag 'customer-recovered'
    Add-LifecycleEvidence 'customer-database-outage-recovery' 'passed'
    try {
        Set-Step16DatabaseOnline $state.businessDatabase $false
        Invoke-ManifestTag 'business-outage'
    }
    finally { Set-Step16DatabaseOnline $state.businessDatabase $true }
    Wait-Ready 'https://localhost:54432/api/health' $business
    Invoke-ManifestTag 'business-recovered'
    Add-LifecycleEvidence 'business-database-outage-recovery' 'passed'

    Stop-OwnedProcess $business
    [void]$processes.Remove($business)
    Invoke-ManifestTag 'business-down'
    $business = Start-BusinessHost $state
    Stop-OwnedProcess $customer
    [void]$processes.Remove($customer)
    Invoke-ManifestTag 'customer-down'
    $customer = Start-CustomerHost $state
    Add-LifecycleEvidence 'opposite-host-outage-isolation' 'passed'
    Stop-AllOwnedHosts
}

function Invoke-RestartAndConcurrentPhases {
    $state = Get-Content $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $customer = Start-CustomerHost $state
    $business = Start-BusinessHost $state
    Ensure-RuntimeVariables
    Stop-OwnedProcess $customer
    [void]$processes.Remove($customer)
    Invoke-ManifestTag 'customer-restarting'
    $customer = Start-CustomerHost $state
    Invoke-ManifestTag 'customer-restarted'
    Stop-OwnedProcess $business
    [void]$processes.Remove($business)
    Invoke-ManifestTag 'business-restarting'
    $business = Start-BusinessHost $state
    Invoke-ManifestTag 'business-restarted'
    Invoke-ManifestTag 'after-restart'
    Stop-AllOwnedHosts

    $customer = Start-CustomerHost $state
    $business = Start-BusinessHost $state
    Stop-AllOwnedHosts
    $customer = Start-CustomerHost $state
    $business = Start-BusinessHost $state
    Invoke-ManifestTag 'after-second-restart'
    $customerSecond = Start-CustomerHost $state 'customer-second' `
        'https://localhost:54435'
    $businessSecond = Start-BusinessHost $state 'business-second' `
        'https://localhost:54436' 'https://localhost:54436'
    Invoke-ManifestTag 'concurrent-hosts'
    & $verifier -StatePath $StatePath -RequireRoles
    Add-LifecycleEvidence 'restart-and-concurrent-instances' 'passed'
    Stop-AllOwnedHosts
}

function Invoke-MissingTablePhases {
    $state = Get-Content $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    Start-HealthyHosts
    Ensure-RuntimeVariables
    try {
        Invoke-Step16Sql $state.customerDatabase `
            'EXEC sp_rename N''AspNetUsers'', N''Step16Missing_AspNetUsers'';'
        Invoke-ManifestTag 'customer-table-missing'
    }
    finally {
        Invoke-Step16Sql $state.customerDatabase `
            'IF OBJECT_ID(N''Step16Missing_AspNetUsers'') IS NOT NULL EXEC sp_rename N''Step16Missing_AspNetUsers'', N''AspNetUsers'';'
    }
    try {
        Invoke-Step16Sql $state.businessDatabase `
            'EXEC sp_rename N''Companies'', N''Step16Missing_Companies'';'
        Invoke-ManifestTag 'business-table-missing'
    }
    finally {
        Invoke-Step16Sql $state.businessDatabase `
            'IF OBJECT_ID(N''Step16Missing_Companies'') IS NOT NULL EXEC sp_rename N''Step16Missing_Companies'', N''Companies'';'
    }
    Add-LifecycleEvidence 'owned-table-missing-and-restored' 'passed'
    Stop-AllOwnedHosts
}

try {
    switch ($Phase) {
        'Provision' {
            & $initializer -Operation Parallel -RunId $RunId -Server $Server `
                -StatePath $StatePath
            & $verifier -StatePath $StatePath -RequireZeroDomainRows
            Merge-Variables
            Start-HealthyHosts
            Invoke-ManifestTag 'after-parallel'
            Stop-AllOwnedHosts
            Add-LifecycleEvidence 'parallel-migration' 'passed'
        }
        'CustomerFirst' {
            & $initializer -Operation CustomerFirst -RunId $RunId -Server $Server `
                -StatePath $StatePath
            & $verifier -StatePath $StatePath -RequireZeroDomainRows `
                -AllowMissingBusiness
            Merge-Variables
            $state = Get-Content $StatePath -Raw | ConvertFrom-Json
            Start-CustomerHost $state | Out-Null
            Invoke-ManifestTag 'customer-first'
            Invoke-ManifestTag 'customer-only'
            Stop-AllOwnedHosts
            Add-LifecycleEvidence 'customer-first-order' 'passed'
        }
        'BusinessFirst' {
            & $initializer -Operation BusinessFirst -RunId $RunId -Server $Server `
                -StatePath $StatePath
            & $verifier -StatePath $StatePath -RequireZeroDomainRows `
                -AllowMissingCustomer
            Merge-Variables
            $state = Get-Content $StatePath -Raw | ConvertFrom-Json
            Start-BusinessHost $state | Out-Null
            Invoke-ManifestTag 'business-first'
            Invoke-ManifestTag 'business-only'
            Stop-AllOwnedHosts
            Add-LifecycleEvidence 'business-first-order' 'passed'
        }
        'Parallel' {
            & $initializer -Operation Parallel -RunId $RunId -Server $Server `
                -StatePath $StatePath
            & $verifier -StatePath $StatePath -RequireZeroDomainRows
            Merge-Variables
            Start-HealthyHosts
            Invoke-ManifestTag 'after-parallel'
            Stop-AllOwnedHosts
            Add-LifecycleEvidence 'parallel-migration' 'passed'
        }
        'Repeat' {
            & $initializer -Operation Repeat -RunId $RunId -Server $Server `
                -StatePath $StatePath
            & $verifier -StatePath $StatePath
            Merge-Variables
            Start-HealthyHosts
            Invoke-ManifestTag 'after-repeat'
            Stop-AllOwnedHosts
            Add-LifecycleEvidence 'repeat-migration-twice' 'passed'
        }
        'ResetCustomer' {
            & $initializer -Operation ResetCustomer -RunId $RunId -Server $Server `
                -StatePath $StatePath
            & $verifier -StatePath $StatePath
            Merge-Variables
            Start-HealthyHosts
            Invoke-ManifestTag 'after-customer-reset'
            Stop-AllOwnedHosts
            Add-LifecycleEvidence 'customer-reset-recreate' 'passed'
        }
        'ResetBusiness' {
            & $initializer -Operation ResetBusiness -RunId $RunId -Server $Server `
                -StatePath $StatePath
            & $verifier -StatePath $StatePath
            Merge-Variables
            Start-HealthyHosts
            Invoke-ManifestTag 'after-business-reset'
            Stop-AllOwnedHosts
            Add-LifecycleEvidence 'business-reset-recreate' 'passed'
        }
        'RunHealthy' {
            Merge-Variables
            Start-HealthyHosts
            Initialize-RuntimeVariables
            Assert-RuntimeCredentialOverlay
            Invoke-ManifestTag 'healthy'
            & $verifier -StatePath $StatePath -RequireRoles
        }
        'ProductionFailures' { Invoke-ProductionAndFailurePhases }
        'OutageRecovery' { Invoke-OutageRecoveryAndDependencyPhases }
        'RestartConcurrent' { Invoke-RestartAndConcurrentPhases }
        'MissingTables' { Invoke-MissingTablePhases }
        'RunTag' {
            if ([string]::IsNullOrWhiteSpace($Tag)) { throw 'RunTag requires -Tag.' }
            Merge-Variables
            Start-HealthyHosts
            Invoke-ManifestTag $Tag
        }
        'Cleanup' {
            $cleanupState = Get-Content $StatePath -Raw -Encoding UTF8 |
                ConvertFrom-Json
            & $cleanup -StatePath $StatePath -Confirm:$false
            Assert-FinalCleanup $cleanupState
        }
        'All' {
            & $initializer -Operation CustomerFirst -RunId $RunId -Server $Server `
                -StatePath $StatePath
            & $verifier -StatePath $StatePath -RequireZeroDomainRows `
                -AllowMissingBusiness
            Merge-Variables
            $state = Get-Content $StatePath -Raw | ConvertFrom-Json
            Start-CustomerHost $state | Out-Null
            Invoke-ManifestTag 'customer-first'
            Invoke-ManifestTag 'customer-only'
            Stop-AllOwnedHosts
            Add-LifecycleEvidence 'customer-first-order' 'passed'
            & $cleanup -StatePath $StatePath -Confirm:$false
            Add-LifecycleEvidence 'customer-first-cleanup' 'passed'

            & $initializer -Operation BusinessFirst -RunId $RunId -Server $Server `
                -StatePath $StatePath
            & $verifier -StatePath $StatePath -RequireZeroDomainRows `
                -AllowMissingCustomer
            Merge-Variables
            $state = Get-Content $StatePath -Raw | ConvertFrom-Json
            Start-BusinessHost $state | Out-Null
            Invoke-ManifestTag 'business-first'
            Invoke-ManifestTag 'business-only'
            Stop-AllOwnedHosts
            Add-LifecycleEvidence 'business-first-order' 'passed'
            & $cleanup -StatePath $StatePath -Confirm:$false
            Add-LifecycleEvidence 'business-first-cleanup' 'passed'

            & $initializer -Operation Parallel -RunId $RunId -Server $Server `
                -StatePath $StatePath
            & $verifier -StatePath $StatePath -RequireZeroDomainRows
            Merge-Variables
            Start-HealthyHosts
            Invoke-ManifestTag 'after-parallel'
            Stop-AllOwnedHosts
            Add-LifecycleEvidence 'parallel-migration' 'passed'
            & $initializer -Operation Repeat -RunId $RunId -Server $Server `
                -StatePath $StatePath
            & $verifier -StatePath $StatePath
            Merge-Variables
            Start-HealthyHosts
            Invoke-ManifestTag 'after-repeat'
            Stop-AllOwnedHosts
            Add-LifecycleEvidence 'repeat-migration-twice' 'passed'
            & $initializer -Operation ResetCustomer -RunId $RunId -Server $Server `
                -StatePath $StatePath
            & $verifier -StatePath $StatePath
            Merge-Variables
            Start-HealthyHosts
            Invoke-ManifestTag 'after-customer-reset'
            Stop-AllOwnedHosts
            Add-LifecycleEvidence 'customer-reset-recreate' 'passed'
            & $initializer -Operation ResetBusiness -RunId $RunId -Server $Server `
                -StatePath $StatePath
            & $verifier -StatePath $StatePath
            Merge-Variables
            Start-HealthyHosts
            Invoke-ManifestTag 'after-business-reset'
            Stop-AllOwnedHosts
            Add-LifecycleEvidence 'business-reset-recreate' 'passed'
            Add-LifecycleEvidence 'combined-customer-business-reset-sequence' 'passed'
            Merge-Variables
            Start-HealthyHosts
            Initialize-RuntimeVariables
            Assert-RuntimeCredentialOverlay
            Invoke-ManifestTag 'after-reset-sequence'
            Invoke-ManifestTag 'healthy'
            & $verifier -StatePath $StatePath -RequireRoles
            Add-LifecycleEvidence 'steady-http-and-invariants' 'passed'
            Stop-AllOwnedHosts
            Invoke-ProductionAndFailurePhases
            Invoke-OutageRecoveryAndDependencyPhases
            Invoke-RestartAndConcurrentPhases
            Invoke-MissingTablePhases
        }
    }
}
finally {
    Stop-AllOwnedHosts
    if ($Phase -eq 'All' -and (Test-Path $StatePath)) {
        $cleanupState = Get-Content $StatePath -Raw -Encoding UTF8 |
            ConvertFrom-Json
        & $cleanup -StatePath $StatePath -Confirm:$false
        Assert-FinalCleanup $cleanupState
    }
}

if ($Phase -notin @('Validate','Cleanup')) {
    foreach ($port in @(54431,54432,50832,54433,54434,54435,54436)) {
        $listeners = @(Get-NetTCPConnection -LocalPort $port -State Listen `
            -ErrorAction SilentlyContinue)
        if ($listeners.Count) { throw "Listener leak remains on local port $port." }
    }
}
Write-Host "Step 16 live-local phase '$Phase' completed without an owned process leak."
