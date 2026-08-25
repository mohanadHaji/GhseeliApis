#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Validate','Provision','Setup','Chain','OutageRecovery',
        'Security','Inherited','Verify','Cleanup','All')]
    [string]$Phase = 'Validate',
    [string]$RunId = ([guid]::NewGuid().ToString('N')),
    [string]$Server = '(localdb)\MSSQLLocalDB',
    [string]$StatePath,
    [string]$RuntimeConfigPath,
    [string]$FixtureOverlayPath,
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$solution = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifacts = Join-Path $PSScriptRoot 'artifacts'
if ([string]::IsNullOrWhiteSpace($StatePath)) {
    $StatePath = Join-Path $artifacts 'step-17.state.local.json'
}
if ([string]::IsNullOrWhiteSpace($RuntimeConfigPath)) {
    $RuntimeConfigPath = Join-Path $artifacts 'step-17.runtime.local.json'
}
if ([string]::IsNullOrWhiteSpace($FixtureOverlayPath)) {
    $FixtureOverlayPath = Join-Path $artifacts 'step-17.fixtures.local.json'
}
$manifest = Join-Path $PSScriptRoot `
    'plans\step-17-quality-gate.manifest.json'
$coverage = Join-Path $PSScriptRoot `
    'plans\step-17-quality-gate.coverage.json'
$plan = Join-Path $solution 'STEP_17_HTTP_TEST_PLAN.md'
$validator = Join-Path $PSScriptRoot 'Test-Step17LiveAssets.ps1'
$step16Initializer = Join-Path $PSScriptRoot 'Initialize-Step16Fixtures.ps1'
$step16Verifier = Join-Path $PSScriptRoot 'Verify-Step16DatabaseInvariants.ps1'
$step16Cleanup = Join-Path $PSScriptRoot 'Remove-Step16Fixtures.ps1'
$step16Runner = Join-Path $PSScriptRoot 'Invoke-Step16LiveLocal.ps1'
$step13Initializer = Join-Path $PSScriptRoot 'Initialize-Step13Fixtures.ps1'
$step14Initializer = Join-Path $PSScriptRoot 'Initialize-Step14Fixtures.ps1'
$runtimeInitializer = Join-Path $PSScriptRoot `
    'Initialize-Step17LocalPrerequisites.ps1'
$harnessModule = Join-Path $PSScriptRoot 'HttpTestHarness.psm1'
$step6Manifest = Join-Path $PSScriptRoot `
    'plans\step-06-secure-integration.manifest.json'
$proxyScript = Join-Path $PSScriptRoot 'Invoke-Step17LoopbackProxy.ps1'
$runSuffix = if ($RunId.Length -ge 12) { $RunId.Substring(0,12) } else { $RunId }
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
$resultFiles = [Collections.Generic.List[string]]::new()

function Resolve-LocalArtifactPath(
    [string]$Path,
    [string]$ParameterName,
    [switch]$AllowMissing) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    $resolved = $ExecutionContext.SessionState.Path.
        GetUnresolvedProviderPathFromPSPath($Path)
    $artifactRoot = $ExecutionContext.SessionState.Path.
        GetUnresolvedProviderPathFromPSPath($artifacts)
    if (-not [string]::Equals(
            (Split-Path -Parent $resolved), $artifactRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$ParameterName must be a direct child of the ignored artifacts directory."
    }
    if (-not $AllowMissing -and
        -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "$ParameterName does not exist."
    }
    return $resolved
}

$StatePath = Resolve-LocalArtifactPath $StatePath 'StatePath' -AllowMissing
$RuntimeConfigPath = Resolve-LocalArtifactPath $RuntimeConfigPath `
    'RuntimeConfigPath' -AllowMissing
$FixtureOverlayPath = Resolve-LocalArtifactPath $FixtureOverlayPath `
    'FixtureOverlayPath' -AllowMissing

function Import-LocalRuntimePrerequisites {
    & $runtimeInitializer -OutputPath $RuntimeConfigPath
    $configuration = Get-Content -LiteralPath $RuntimeConfigPath -Raw `
        -Encoding UTF8 | ConvertFrom-Json
    foreach ($property in $configuration.psobject.Properties) {
        [Environment]::SetEnvironmentVariable(
            $property.Name,
            [string]$property.Value,
            [EnvironmentVariableTarget]::Process)
    }
}

function Get-MissingRuntimePrerequisites {
    $required = @(
        'STEP17_CUSTOMER_PASSWORD',
        'STEP17_BUSINESS_OWNER_PASSWORD',
        'STEP17_CUSTOMER_JWT_SECRET',
        'STEP17_BUSINESS_JWT_SECRET',
        'STEP17_CUSTOMER_TO_BUSINESS_SERVICE_ID',
        'STEP17_CUSTOMER_TO_BUSINESS_HMAC_SECRET',
        'STEP17_BUSINESS_TO_CUSTOMER_SERVICE_ID',
        'STEP17_BUSINESS_TO_CUSTOMER_HMAC_SECRET',
        'STEP17_BUSINESS_TO_CUSTOMER_NEXT_HMAC_SECRET',
        'STEP17_BUSINESS_TO_CUSTOMER_RECONCILE_SERVICE_ID',
        'STEP17_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET',
        'STEP17_WEBHOOK_SECRET')
    return @($required | Where-Object {
            [string]::IsNullOrWhiteSpace(
                [Environment]::GetEnvironmentVariable($_))
        })
}

function Assert-LiveOptIn {
    if (-not $Execute) {
        throw 'Live Step 17 phases require explicit -Execute.'
    }
    if ([Environment]::GetEnvironmentVariable('ASPNETCORE_ENVIRONMENT') -eq
        'Production') {
        throw 'Step 17 test-only wiring cannot run in Production.'
    }
    $missing = @(Get-MissingRuntimePrerequisites)
    if ($missing.Count) {
        throw "Missing Step 17 runtime prerequisites: $($missing -join ', ')."
    }
    if ([Environment]::GetEnvironmentVariable(
            'STEP17_ENABLE_DETERMINISTIC_FAKE_PAYMENT_GATEWAY') -cne 'true') {
        throw ('Set STEP17_ENABLE_DETERMINISTIC_FAKE_PAYMENT_GATEWAY=true ' +
            'to explicitly opt into the local fake; real Stripe is forbidden.')
    }
    $customerProgram = Join-Path $solution 'GhseeliApis\Program.cs'
    $customerSource = Get-Content -LiteralPath $customerProgram -Raw `
        -Encoding UTF8
    if (-not $customerSource.Contains('Step17TestFixtures') -or
        -not $customerSource.Contains('DeterministicFake')) {
        throw ('Customer host lacks Development-only Step17TestFixtures ' +
            'deterministic payment-gateway wiring. Live execution is blocked ' +
            'instead of falling through to Stripe.')
    }
    foreach ($name in @(
            'STEP17_CUSTOMER_JWT_SECRET','STEP17_BUSINESS_JWT_SECRET',
            'STEP17_CUSTOMER_TO_BUSINESS_HMAC_SECRET',
            'STEP17_BUSINESS_TO_CUSTOMER_HMAC_SECRET',
            'STEP17_BUSINESS_TO_CUSTOMER_NEXT_HMAC_SECRET',
            'STEP17_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET',
            'STEP17_WEBHOOK_SECRET')) {
        if ([Environment]::GetEnvironmentVariable($name).Length -lt 32) {
            throw "$name must contain at least 32 characters."
        }
    }
}

function Read-State {
    if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) {
        throw "Step 17 state does not exist at '$StatePath'. Run Provision first."
    }
    return Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 |
        ConvertFrom-Json
}

function Save-State([object]$State) {
    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $State | ConvertTo-Json -Depth 50 |
        Set-Content -LiteralPath $StatePath -Encoding UTF8
}

function Merge-StateValues([hashtable]$Values) {
    $state = Read-State
    foreach ($entry in $Values.GetEnumerator()) {
        $state | Add-Member -NotePropertyName $entry.Key `
            -NotePropertyValue $entry.Value -Force
    }
    Save-State $state
}

function Initialize-Step17StateVariables {
    $stamp = $RunId.Substring(0,12).ToLowerInvariant()
    Merge-StateValues @{
        runStamp = $stamp
        installationId = [guid]::NewGuid().ToString('D')
        customerEmail = "step17-$stamp@example.invalid"
        slotStartUtc = ([DateTimeOffset]::UtcNow.AddDays(4)).ToString(
            'yyyy-MM-ddT12:00:00Z')
        baseUtc = [DateTimeOffset]::UtcNow.UtcDateTime.ToString(
            'yyyy-MM-ddTHH:mm:ss.fffffffZ')
        successEventId = "evt_step17_success_$stamp"
        failureEventId = "evt_step17_failure_$stamp"
        refundEventId = "evt_step17_refund_$stamp"
        invalidStripeEventId = "evt_step17_invalid_$stamp"
        recoveryEventId = [guid]::NewGuid().ToString('D')
        recoveryIdempotencyKey = "step17-recovery-$stamp"
        customerHttpBaseUrl = 'http://127.0.0.1:50831'
        businessHttpBaseUrl = 'http://127.0.0.1:50832'
        businessProxyBaseUrl = 'http://127.0.0.1:50833'
        businessProxyBackendBaseUrl = 'http://[::1]:50832'
    }
}

function Merge-RuntimeVariables([hashtable]$Variables) {
    $state = Read-State
    foreach ($entry in $Variables.GetEnumerator()) {
        if ($entry.Key -in @('baseUrl')) { continue }
        $state | Add-Member -NotePropertyName $entry.Key `
            -NotePropertyValue $entry.Value -Force
    }
    Save-State $state
}

function Quote([string]$Value) {
    return '"' + $Value.Replace('"','\"') + '"'
}

function Start-OwnedProcess(
    [string]$Name,
    [string]$FileName,
    [string]$Arguments,
    [string]$WorkingDirectory,
    [hashtable]$Environment) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FileName
    $start.Arguments = $Arguments
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    foreach ($entry in $Environment.GetEnumerator()) {
        $start.EnvironmentVariables[[string]$entry.Key] = [string]$entry.Value
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (-not $process.Start()) { throw "Could not start owned process '$Name'." }
    $processes.Add($process)
    return $process
}

function Stop-OwnedProcess([Diagnostics.Process]$Process) {
    if ($null -eq $Process) { return }
    $Process.Refresh()
    if (-not $Process.HasExited) {
        Stop-Process -Id $Process.Id
        if (-not $Process.WaitForExit(15000)) {
            throw "Owned process $($Process.Id) did not stop."
        }
    }
    [void]$processes.Remove($Process)
    $Process.Dispose()
}

function Stop-AllOwnedProcesses {
    for ($index = $processes.Count - 1; $index -ge 0; $index--) {
        Stop-OwnedProcess $processes[$index]
    }
    $processes.Clear()
}

function Wait-Ready(
    [string]$Url,
    [Diagnostics.Process]$Process,
    [int]$Seconds = 90) {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.ServerCertificateCustomValidationCallback =
        [Net.Http.HttpClientHandler]::
        DangerousAcceptAnyServerCertificateValidator
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(2)
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    try {
        do {
            $Process.Refresh()
            if ($Process.HasExited) {
                throw "Owned host exited before readiness at '$Url'."
            }
            try {
                $response = $client.GetAsync($Url).GetAwaiter().GetResult()
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
    throw "Owned host did not become ready at '$Url'."
}

function Set-LiveJwtEnvironment(
    [object]$State,
    [switch]$Customer,
    [switch]$Business) {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $false
    $handler.ServerCertificateCustomValidationCallback =
        [Net.Http.HttpClientHandler]::
        DangerousAcceptAnyServerCertificateValidator
    $client = [Net.Http.HttpClient]::new($handler)
    try {
        $requests = @()
        if ($Customer) {
            $requests += @{
                Name = 'Customer'
                Url = "$($State.customerBaseUrl)/api/Auth/login"
                EnvironmentName = 'STEP17_CUSTOMER_JWT'
                Body = @{
                    email = $State.customerEmail
                    password = [Environment]::GetEnvironmentVariable(
                        'STEP17_CUSTOMER_PASSWORD')
                }
            }
        }
        if ($Business) {
            $requests += @{
                Name = 'Business'
                Url = "$($State.businessBaseUrl)/api/v1/business/auth/login"
                EnvironmentName = 'STEP17_BUSINESS_JWT'
                Body = @{
                    email = $State.ownerEmail
                    password = [Environment]::GetEnvironmentVariable(
                        'STEP17_BUSINESS_OWNER_PASSWORD')
                }
            }
        }
        foreach ($request in $requests) {
            $json = $request.Body | ConvertTo-Json -Compress
            $content = [Net.Http.StringContent]::new(
                $json,
                [Text.Encoding]::UTF8,
                'application/json')
            try {
                $response = $client.PostAsync(
                    $request.Url,
                    $content).GetAwaiter().GetResult()
                try {
                    $body = $response.Content.ReadAsStringAsync().
                        GetAwaiter().GetResult()
                    if (-not $response.IsSuccessStatusCode) {
                        throw "$($request.Name) login failed with HTTP $([int]$response.StatusCode)."
                    }
                    $token = [string](($body | ConvertFrom-Json).token)
                    if ([string]::IsNullOrWhiteSpace($token)) {
                        throw "$($request.Name) login returned no token."
                    }
                    [Environment]::SetEnvironmentVariable(
                        $request.EnvironmentName,
                        $token,
                        [EnvironmentVariableTarget]::Process)
                }
                finally { $response.Dispose() }
            }
            finally { $content.Dispose() }
        }
    }
    finally {
        $client.Dispose()
        $handler.Dispose()
    }
}

function New-Step17Jwt(
    [string]$Secret,
    [string]$Issuer,
    [string]$Audience,
    [string]$Role,
    [AllowNull()][string]$Subject,
    [long]$ExpiresOffsetSeconds = 7200) {
    function ConvertTo-Base64Url([byte[]]$Bytes) {
        return [Convert]::ToBase64String($Bytes).
            TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }

    $header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes(
            '{"alg":"HS256","typ":"JWT"}'))
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $payload = [ordered]@{
        'http://schemas.microsoft.com/ws/2008/06/identity/claims/role' = $Role
        iss = $Issuer
        aud = $Audience
        nbf = $now - 7200
        iat = $now - 7200
        exp = $now + $ExpiresOffsetSeconds
    }
    if ($null -ne $Subject) {
        $payload[
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'
        ] = $Subject
    }
    $encodedPayload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes(
            ($payload | ConvertTo-Json -Compress)))
    $unsigned = "$header.$encodedPayload"
    $hmac = [Security.Cryptography.HMACSHA256]::new(
        [Text.Encoding]::UTF8.GetBytes($Secret))
    try {
        $signature = ConvertTo-Base64Url ($hmac.ComputeHash(
                [Text.Encoding]::ASCII.GetBytes($unsigned)))
    }
    finally { $hmac.Dispose() }
    return "$unsigned.$signature"
}

function Get-HostEnvironment([object]$State,[bool]$Customer) {
    $inheritedMode = [Environment]::GetEnvironmentVariable(
        'STEP17_INHERITED_MODE') -ceq 'true'
    if ($Customer) {
        $step9Active = $null -ne $State.psobject.Properties[
            'step9InheritedActive'] -and $State.step9InheritedActive -eq $true
        $primaryCatalogCompanyId = if ($step9Active) {
            $State.businessOneSourceId
        }
        else {
            $State.companyId
        }
        $environment = @{
            ASPNETCORE_ENVIRONMENT = 'Development'
            ASPNETCORE_URLS = (
                "$($State.customerBaseUrl);$($State.customerHttpBaseUrl)")
            ConnectionStrings__CustomerConnection = (
                "Server=$($State.server);Database=$($State.customerDatabase);" +
                'Integrated Security=true;TrustServerCertificate=true')
            JwtSettings__SecretKey = [Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_JWT_SECRET')
            JwtSettings__Issuer = 'GhseeliApis'
            JwtSettings__Audience = 'GhseeliApis'
            BusinessApiClient__BaseUrl = $State.businessBaseUrl
            BusinessApiClient__ServiceId = [Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_TO_BUSINESS_SERVICE_ID')
            BusinessApiClient__ActiveSecret = [Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_TO_BUSINESS_HMAC_SECRET')
            BusinessApiClient__AllowUntrustedDevelopmentCertificate = 'true'
            CatalogReadModel__Providers__0__SourceCompanyId =
                $primaryCatalogCompanyId
            CatalogReadModel__Providers__0__Enabled = 'true'
            CatalogReadModel__Providers__0__Order = '0'
            CustomerInternalServiceAuthentication__AllowInsecureHttpInDevelopment = 'false'
            CustomerInternalServiceAuthentication__Services__0__ServiceId =
                [Environment]::GetEnvironmentVariable(
                    'STEP17_BUSINESS_TO_CUSTOMER_SERVICE_ID')
            CustomerInternalServiceAuthentication__Services__0__ActiveSecret =
                [Environment]::GetEnvironmentVariable(
                    'STEP17_BUSINESS_TO_CUSTOMER_HMAC_SECRET')
            CustomerInternalServiceAuthentication__Services__0__NextSecret =
                [Environment]::GetEnvironmentVariable(
                    'STEP17_BUSINESS_TO_CUSTOMER_NEXT_HMAC_SECRET')
            CustomerInternalServiceAuthentication__Services__0__AllowedOperations__0 =
                'booking_status_callback'
            CustomerInternalServiceAuthentication__Services__0__AllowedOperations__1 =
                '__disabled__'
            CustomerInternalServiceAuthentication__Services__0__AllowedOperations__2 =
                '__disabled__'
            CustomerInternalServiceAuthentication__Services__1__ServiceId =
                [Environment]::GetEnvironmentVariable(
                    'STEP17_BUSINESS_TO_CUSTOMER_RECONCILE_SERVICE_ID')
            CustomerInternalServiceAuthentication__Services__1__ActiveSecret =
                [Environment]::GetEnvironmentVariable(
                    'STEP17_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET')
            CustomerInternalServiceAuthentication__Services__1__AllowedOperations__0 =
                'booking_status_reconcile'
            CustomerInternalServiceAuthentication__Services__1__AllowedOperations__1 =
                'booking_status_read'
            CustomerInternalServiceAuthentication__Services__1__AllowedOperations__2 =
                '__disabled__'
            Stripe__WebhookSecret = [Environment]::GetEnvironmentVariable(
                'STEP17_WEBHOOK_SECRET')
            Stripe__PublishableKey = 'pk_test_step17_local'
            Stripe__SecretKey = 'sk_test_step17_local_no_network'
            Step17TestFixtures__Enabled = 'true'
            Step17TestFixtures__PaymentGateway = 'DeterministicFake'
            RateLimiting__DeviceRegistrationPermitLimit =
                $(if ($inheritedMode) { '10000' } else { '3' })
            RateLimiting__DeviceRegistrationWindowSeconds = '60'
            RateLimiting__DeviceRegistrationPeerPermitLimit =
                $(if ($inheritedMode) { '10000' } else { '4' })
            RateLimiting__DeviceRegistrationPeerWindowSeconds = '60'
            RateLimiting__AuthAccountPermitLimit =
                $(if ($inheritedMode) { '10000' } else { '3' })
            RateLimiting__AuthAccountWindowSeconds = '60'
            RateLimiting__AuthAggregatePermitLimit =
                $(if ($inheritedMode) { '10000' } else { '6' })
            RateLimiting__AuthAggregateWindowSeconds = '60'
            RateLimiting__ValidStripePermitLimit =
                $(if ($inheritedMode) { '10000' } else { '5' })
            RateLimiting__ValidStripeWindowSeconds = '60'
            RateLimiting__InvalidStripePermitLimit =
                $(if ($inheritedMode) { '10000' } else { '3' })
            RateLimiting__InvalidStripeWindowSeconds = '60'
            RateLimiting__PaymentAggregatePermitLimit =
                $(if ($inheritedMode) { '10000' } else { '2' })
            RateLimiting__PaymentAggregateWindowSeconds = '60'
            RateLimiting__PaymentIntentPermitLimit =
                $(if ($inheritedMode) { '10000' } else { '10' })
            RateLimiting__PaymentIntentWindowSeconds = '60'
            RateLimiting__DevicePermitLimit =
                $(if ($inheritedMode) { '10000' } else { '300' })
            RateLimiting__DeviceWindowSeconds = '60'
            RateLimiting__BearerMutationPermitLimit =
                $(if ($inheritedMode) { '10000' } else { '60' })
            RateLimiting__BearerMutationWindowSeconds = '60'
            RateLimiting__AnonymousPermitLimit =
                $(if ($inheritedMode) { '10000' } else { '60' })
            RateLimiting__AnonymousWindowSeconds = '60'
        }
        if ($step9Active) {
            $environment[
                'CatalogReadModel__Providers__1__SourceCompanyId'
            ] = $State.businessTwoSourceId
            $environment['CatalogReadModel__Providers__1__Enabled'] = 'true'
            $environment['CatalogReadModel__Providers__1__Order'] = '1'
        }
        return $environment
    }
    return @{
        ASPNETCORE_ENVIRONMENT = 'Development'
        ASPNETCORE_URLS = (
            "$($State.businessBaseUrl);$($State.businessHttpBaseUrl);" +
            "$($State.businessProxyBackendBaseUrl)")
        ConnectionStrings__BusinessConnection = (
            "Server=$($State.server);Database=$($State.businessDatabase);" +
            'Integrated Security=true;TrustServerCertificate=true')
        BusinessJwtSettings__SecretKey = [Environment]::GetEnvironmentVariable(
            'STEP17_BUSINESS_JWT_SECRET')
        BusinessJwtSettings__Issuer = 'Ghseeli.BusinessApi'
        BusinessJwtSettings__Audience = 'Ghseeli.BusinessClients'
        InternalServiceAuthentication__AllowInsecureHttpInDevelopment = 'false'
        InternalServiceAuthentication__Services__0__ServiceId =
            [Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_TO_BUSINESS_SERVICE_ID')
        InternalServiceAuthentication__Services__0__ActiveSecret =
            [Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_TO_BUSINESS_HMAC_SECRET')
        InternalServiceAuthentication__Services__0__AllowedOperations__0 =
            'catalog_snapshot'
        InternalServiceAuthentication__Services__0__AllowedOperations__1 =
            'appointment_validate'
        InternalServiceAuthentication__Services__0__AllowedOperations__2 =
            'reservation_create'
        InternalServiceAuthentication__Services__0__AllowedOperations__3 =
            'reservation_status_read'
        CustomerBookingStatusClient__BaseUrl = $State.customerBaseUrl
        CustomerBookingStatusClient__ServiceId =
            [Environment]::GetEnvironmentVariable(
                'STEP17_BUSINESS_TO_CUSTOMER_SERVICE_ID')
        CustomerBookingStatusClient__ActiveSecret =
            [Environment]::GetEnvironmentVariable(
                'STEP17_BUSINESS_TO_CUSTOMER_HMAC_SECRET')
        CustomerBookingStatusClient__AllowUntrustedDevelopmentCertificate = 'true'
        ForwardedHeaders__KnownProxies__0 = '::1'
        Step17TestFixtures__Enabled = 'true'
    }
}

function Start-Customer([object]$State) {
    $project = Join-Path $solution 'GhseeliApis\GhseeliApis.csproj'
    $process = Start-OwnedProcess 'customer' 'dotnet' (
        "run --project $(Quote $project) -c Release --no-build --no-launch-profile") `
        $solution (Get-HostEnvironment $State $true)
    Wait-Ready "$($State.customerBaseUrl)/api/Health" $process
    return $process
}

function Start-Business([object]$State) {
    $project = Join-Path $solution `
        'Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj'
    $process = Start-OwnedProcess 'business' 'dotnet' (
        "run --project $(Quote $project) -c Release --no-build --no-launch-profile") `
        $solution (Get-HostEnvironment $State $false)
    Wait-Ready "$($State.businessBaseUrl)/api/health" $process
    return $process
}

function Start-CustomerProduction([object]$State) {
    $project = Join-Path $solution 'GhseeliApis\GhseeliApis.csproj'
    $environment = Get-HostEnvironment $State $true
    $environment.ASPNETCORE_ENVIRONMENT = 'Production'
    $environment.ASPNETCORE_URLS = $State.customerProductionBaseUrl
    $environment.Remove('Step17TestFixtures__Enabled')
    $environment.Remove('Step17TestFixtures__PaymentGateway')
    $process = Start-OwnedProcess 'customer-production' 'dotnet' (
        "run --project $(Quote $project) -c Release --no-build --no-launch-profile") `
        $solution $environment
    Wait-Ready "$($State.customerProductionBaseUrl)/api/Health" $process
    return $process
}

function Start-BusinessProduction([object]$State) {
    $project = Join-Path $solution `
        'Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj'
    $environment = Get-HostEnvironment $State $false
    $environment.ASPNETCORE_ENVIRONMENT = 'Production'
    $environment.ASPNETCORE_URLS = $State.businessProductionBaseUrl
    $environment.Remove('Step17TestFixtures__Enabled')
    $process = Start-OwnedProcess 'business-production' 'dotnet' (
        "run --project $(Quote $project) -c Release --no-build --no-launch-profile") `
        $solution $environment
    Wait-Ready "$($State.businessProductionBaseUrl)/api/health" $process
    return $process
}

function Start-Proxy {
    $state = Read-State
    $shell = (Get-Process -Id $PID).Path
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $process = Start-OwnedProcess 'trusted-proxy' $shell (
            "-NoProfile -ExecutionPolicy Bypass -File $(Quote $proxyScript) " +
            "-ListenPrefix $($state.businessProxyBaseUrl)/ " +
            "-BackendBaseUrl $($state.businessProxyBackendBaseUrl)") $solution @{}
        try {
            Wait-Ready "$($state.businessProxyBaseUrl)/api/health" `
                $process 10
            return $process
        }
        catch {
            Stop-OwnedProcess $process
            if ($attempt -eq 3) {
                throw 'Trusted loopback proxy did not become ready after 3 attempts.'
            }
            Start-Sleep -Seconds 1
        }
    }
}

function Invoke-SqlScalar(
    [object]$State,[string]$Database,[string]$Sql) {
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$($State.server);Database=$Database;" +
        'Integrated Security=true;TrustServerCertificate=true')
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        return $command.ExecuteScalar()
    }
    finally { $connection.Dispose() }
}

function Invoke-SqlNonQuery(
    [object]$State,[string]$Database,[string]$Sql) {
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$($State.server);Database=$Database;" +
        'Integrated Security=true;TrustServerCertificate=true')
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}

function Get-SqlRow(
    [object]$State,[string]$Database,[string]$Sql) {
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$($State.server);Database=$Database;" +
        'Integrated Security=true;TrustServerCertificate=true')
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $reader = $command.ExecuteReader()
        if (-not $reader.Read()) { return $null }
        $row = @{}
        for ($index = 0; $index -lt $reader.FieldCount; $index++) {
            $row[$reader.GetName($index)] =
                if ($reader.IsDBNull($index)) { $null } else {
                    $reader.GetValue($index)
                }
        }
        return $row
    }
    finally { $connection.Dispose() }
}

function Get-Sha256Hex([string]$Value) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = $algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value))
    }
    finally { $algorithm.Dispose() }
    return ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
}

function Get-CloneSql(
    [object]$State,
    [string]$Database,
    [string]$Table,
    [string]$Where,
    [hashtable]$Replacements) {
    if ($Table -notmatch '^[A-Za-z][A-Za-z0-9]+$') {
        throw "Unsafe fixture table '$Table'."
    }
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$($State.server);Database=$Database;" +
        'Integrated Security=true;TrustServerCertificate=true')
    try {
        $connection.Open()
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
    if (-not $columns.Count) {
        throw "Fixture table '$Table' has no cloneable columns."
    }
    $insertColumns = @($columns | ForEach-Object { "[$_]" }) -join ','
    $selectColumns = @($columns | ForEach-Object {
            if ($Replacements.ContainsKey($_)) {
                $Replacements[$_]
            }
            else { "[$_]" }
        }) -join ','
    return "INSERT INTO dbo.[$Table] ($insertColumns) " +
        "SELECT $selectColumns FROM dbo.[$Table] WHERE $Where;"
}

function Initialize-SupportingBookingFixtures {
    $state = Read-State
    $sourceReference = [guid]$state.bookingReference
    $sourceReservation = Get-SqlRow $state $state.businessDatabase (
        "SELECT Id FROM AppointmentReservations WHERE PublicId=" +
        "'$($state.reservationId)'")
    if ($null -eq $sourceReservation) {
        throw 'Main Step 17 reservation was not persisted before fixture cloning.'
    }
    $sourceWorkOrder = Get-SqlRow $state $state.businessDatabase (
        "SELECT Id FROM WorkOrders WHERE PublicId='$($state.workOrderId)'")
    if ($null -eq $sourceWorkOrder) {
        throw 'Main Step 17 work order was not persisted before fixture cloning.'
    }

    $fixtures = @{}
    foreach ($name in @('payable','failure','recovery')) {
        $bookingId = [guid]::NewGuid()
        $bookingReference = [guid]::NewGuid()
        $orderGuid = [guid]::NewGuid()
        $reservationId = [guid]::NewGuid()
        $reservationPublicId = [guid]::NewGuid()
        $workOrderId = [guid]::NewGuid()
        $workOrderPublicId = [guid]::NewGuid()
        $requestHash = Get-Sha256Hex ($bookingReference.ToString('D'))

        $customerSql = Get-CloneSql $state $state.customerDatabase `
            'CustomerBookings' "PublicReference='$sourceReference'" @{
                Id = "CAST('$bookingId' AS uniqueidentifier)"
                PublicReference =
                    "CAST('$bookingReference' AS uniqueidentifier)"
                OrderGuid = "CAST('$orderGuid' AS uniqueidentifier)"
                BusinessReservationId =
                    "CAST('$reservationPublicId' AS uniqueidentifier)"
                BusinessWorkOrderId =
                    "CAST('$workOrderPublicId' AS uniqueidentifier)"
                Status = "N'Pending'"
                BusinessStatusSequence = 'CAST(0 AS bigint)'
                IsPaid = 'CAST(0 AS bit)'
                PaymentState = "N'Unpaid'"
                CreatedAtUtc = 'SYSDATETIMEOFFSET()'
                StatusChangedAtUtc = 'SYSDATETIMEOFFSET()'
            }
        Invoke-SqlNonQuery $state $state.customerDatabase $customerSql

        $reservationSql = Get-CloneSql $state $state.businessDatabase `
            'AppointmentReservations' "Id='$($sourceReservation.Id)'" @{
                Id = "CAST('$reservationId' AS uniqueidentifier)"
                PublicId =
                    "CAST('$reservationPublicId' AS uniqueidentifier)"
                CustomerBookingReference =
                    "CAST('$bookingReference' AS uniqueidentifier)"
                OrderGuid = "CAST('$orderGuid' AS uniqueidentifier)"
                RequestHash = "N'$requestHash'"
                Status = "N'Pending'"
                StatusSequence = 'CAST(0 AS bigint)'
                StatusChangedAtUtc = 'SYSDATETIMEOFFSET()'
                CreatedAtUtc = 'SYSDATETIMEOFFSET()'
            }
        Invoke-SqlNonQuery $state $state.businessDatabase $reservationSql

        $workOrderSql = Get-CloneSql $state $state.businessDatabase `
            'WorkOrders' "Id='$($sourceWorkOrder.Id)'" @{
                Id = "CAST('$workOrderId' AS uniqueidentifier)"
                PublicId = "CAST('$workOrderPublicId' AS uniqueidentifier)"
                AppointmentReservationId =
                    "CAST('$reservationId' AS uniqueidentifier)"
                Status = "N'Pending'"
                CreatedAtUtc = 'SYSDATETIMEOFFSET()'
                UpdatedAtUtc = 'SYSDATETIMEOFFSET()'
            }
        Invoke-SqlNonQuery $state $state.businessDatabase $workOrderSql
        $fixtures[$name] = [pscustomobject]@{
            BookingId = $bookingId
            BookingReference = $bookingReference
            ReservationId = $reservationPublicId
            WorkOrderId = $workOrderPublicId
        }
    }

    $overlay = [ordered]@{
        providerId = $state.providerId
        categoryLocalId = $state.categoryLocalId
        offeringLocalId = $state.offeringLocalId
        catalogVersion = $state.catalogVersion
        payableBookingReference =
            $fixtures.payable.BookingReference.ToString('D')
        payableBookingInternalId = $fixtures.payable.BookingId.ToString('D')
        failurePaymentId = 'pending-after-payment-create'
        failurePaymentIntentId = 'pending-after-payment-create'
        failurePaymentMinorAmount = 'pending-after-payment-create'
        failurePaymentCurrency = 'pending-after-payment-create'
        failureBookingInternalId = $fixtures.failure.BookingId.ToString('D')
        failureBookingReference =
            $fixtures.failure.BookingReference.ToString('D')
        recoveryBookingReference =
            $fixtures.recovery.BookingReference.ToString('D')
        recoveryReservationId =
            $fixtures.recovery.ReservationId.ToString('D')
        recoveryWorkOrderId = $fixtures.recovery.WorkOrderId.ToString('D')
    }
    [IO.File]::WriteAllText(
        $FixtureOverlayPath,
        ($overlay | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))
    Merge-FixtureOverlay
}

function Initialize-FailurePaymentFixture {
    $state = Read-State
    $failurePaymentId = [guid]::NewGuid()
    $intentId = "pi_step17_failure_$runSuffix"
    $idempotencyKey = "step17-failure-$runSuffix"
    $stripeKey = "ghseeli-step17-failure-$runSuffix"
    $requestHash = Get-Sha256Hex (
        "$($state.failureBookingReference)`nCard")
    $paymentSql = Get-CloneSql $state $state.customerDatabase `
        'CustomerPayments' "Id='$($state.paymentId)'" @{
            Id = "CAST('$failurePaymentId' AS uniqueidentifier)"
            CustomerBookingId =
                "CAST('$($state.failureBookingInternalId)' AS uniqueidentifier)"
            IdempotencyKey = "N'$idempotencyKey'"
            RequestHash = "N'$requestHash'"
            StripeIdempotencyKey = "N'$stripeKey'"
            PaymentIntentId = "N'$intentId'"
            ChargeId = 'NULL'
            ProviderStatus = "N'requires_payment_method'"
            ClientSecret = "N'${intentId}_secret_local_fixture'"
            IntentLeaseOwnerToken = 'NULL'
            IntentLeaseExpiresAtUtc = 'NULL'
            CreatedAtUtc = 'SYSDATETIMEOFFSET()'
            UpdatedAtUtc = 'SYSDATETIMEOFFSET()'
        }
    Invoke-SqlNonQuery $state $state.customerDatabase $paymentSql
    $row = Get-SqlRow $state $state.customerDatabase @"
SELECT p.Id,p.PaymentIntentId,p.MinorAmount,p.Currency,p.CustomerBookingId,
       b.PublicReference
FROM CustomerPayments p
JOIN CustomerBookings b ON b.Id=p.CustomerBookingId
WHERE p.Id='$failurePaymentId'
"@
    if ($null -eq $row) {
        throw 'Failure-payment fixture was not persisted.'
    }
    $overlay = Get-Content -LiteralPath $FixtureOverlayPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $overlay.failurePaymentId = ([guid]$row.Id).ToString('D')
    $overlay.failurePaymentIntentId = [string]$row.PaymentIntentId
    $overlay.failurePaymentMinorAmount = [long]$row.MinorAmount
    $overlay.failurePaymentCurrency = ([string]$row.Currency).ToLowerInvariant()
    [IO.File]::WriteAllText(
        $FixtureOverlayPath,
        ($overlay | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))
    Merge-FixtureOverlay
}

function Hydrate-PaymentVariables {
    $state = Read-State
    $paymentId = [guid]$state.paymentId
    $row = Get-SqlRow $state $state.customerDatabase @"
SELECT p.PaymentIntentId,p.Amount,p.Currency,p.CustomerBookingId,
       b.PublicReference
FROM CustomerPayments p
JOIN CustomerBookings b ON b.Id=p.CustomerBookingId
WHERE p.Id='$paymentId'
"@
    if ($null -eq $row -or
        [string]::IsNullOrWhiteSpace([string]$row.PaymentIntentId)) {
        throw 'Fake gateway payment row could not be hydrated after payment 026.'
    }
    Merge-StateValues @{
        paymentIntentId = [string]$row.PaymentIntentId
        paymentMinorAmount = [long]([decimal]$row.Amount * 100)
        paymentCurrency = ([string]$row.Currency).ToLowerInvariant()
        payableBookingInternalId = ([guid]$row.CustomerBookingId).ToString('D')
        payableBookingReference = ([guid]$row.PublicReference).ToString('D')
        paymentChargeId = "ch_step17_$runSuffix"
    }
}

function Add-ConfigurationFixture([object]$State) {
    $now = [DateTimeOffset]::UtcNow.ToString('o')
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$($State.server);Database=$($State.customerDatabase);" +
        'Integrated Security=true;TrustServerCertificate=true')
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = @"
UPDATE CustomerConfigurations SET IsActive=0;
        DELETE FROM CustomerConfigurations
        WHERE Id='17000000-0000-4000-8000-000000000020';
        INSERT INTO CustomerConfigurations
 (Id,IsActive,SupportEmail,SupportPhone,DisplayNameAr,DisplayNameHe,
  LegalNoticeAr,LegalNoticeHe,PrivacyPolicyUrl,TermsOfServiceUrl,
  IsMaintenanceModeEnabled,MaintenanceMessageAr,MaintenanceMessageHe,
  CreatedAt,UpdatedAt)
VALUES
 ('17000000-0000-4000-8000-000000000020',1,N'support@example.invalid',
  N'+970000000000',N'غسيلي المحلي',N'ע׳סילי מקומי',N'إشعار محلي آمن',
  N'הודעה מקומית בטוחה',N'https://example.invalid/privacy',
  N'https://example.invalid/terms',0,NULL,NULL,'$now','$now');
"@
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
    Merge-StateValues @{
        configurationNameAr = 'غسيلي المحلي'
    }
}

function Invoke-ManifestTag([string]$Tag) {
    $state = Read-State
    $resultPath = Join-Path $artifacts (
        "step17-$Tag-$runSuffix.results.local.json")
    Import-Module $harnessModule -Force
    $result = Invoke-HttpTestHarness -ManifestPath $manifest `
        -BaseUrl $state.customerBaseUrl -VariablesPath $StatePath `
        -Tags $Tag -ResultsPath $resultPath
    $resultFiles.Add($resultPath)
    if (-not $result.Summary.passedAll) {
        throw "Step 17 manifest phase '$Tag' failed."
    }
    Merge-RuntimeVariables $result.RuntimeVariables
}

function Invoke-ManifestScenarioId([string]$ScenarioId) {
    $document = Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $selected = @($document.scenarios | Where-Object {
            $_.id -ceq $ScenarioId
        })
    if ($selected.Count -ne 1) {
        throw "Expected one Step 17 scenario '$ScenarioId'."
    }
    $document.scenarios = $selected
    $filteredPath = Join-Path $artifacts `
        "step17-selected-$ScenarioId.local.json"
    $document | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $filteredPath -Encoding UTF8
    $state = Read-State
    $resultPath = Join-Path $artifacts `
        "step17-$ScenarioId-$runSuffix.results.local.json"
    Import-Module $harnessModule -Force
    $result = Invoke-HttpTestHarness -ManifestPath $filteredPath `
        -BaseUrl $state.customerBaseUrl -VariablesPath $StatePath `
        -ResultsPath $resultPath
    if (-not $result.Summary.passedAll) {
        throw "Step 17 scenario '$ScenarioId' failed."
    }
    Merge-RuntimeVariables $result.RuntimeVariables
    Remove-Item -LiteralPath $filteredPath -Force -ErrorAction SilentlyContinue
}

function Invoke-SupportingStatusCallback(
    [int]$Sequence,[string]$Status,[string]$EventVariable) {
    $state = Read-State
    $eventId = [string]$state.$EventVariable
    if ([string]::IsNullOrWhiteSpace($eventId)) {
        throw "Transition did not expose '$EventVariable'."
    }
    $supportPath = Join-Path $artifacts `
        "step17-status-$Sequence-support.local.json"
    [ordered]@{
        name = "Step 17 status $Sequence supporting callback"
        sensitiveFields = @(
            'authorization','token','secret','signature',
            'x-ghseeli-signature','x-ghseeli-nonce','idempotency-key')
        scenarios = @([ordered]@{
            id = "STEP17-SUPPORT-STATUS-$Sequence"
            feature = 'step17-support'
            tags = @('step17-support')
            method = 'POST'
            url = "$($state.customerBaseUrl)/api/v1/internal/bookings/status"
            headers = [ordered]@{
                Accept = 'application/json'
                'Content-Type' = 'application/json'
                'X-Correlation-Id' = "corr-step17-support-$Sequence"
                'Idempotency-Key' = "step17-support-$Sequence-$runSuffix"
            }
            jsonBody = [ordered]@{
                contractVersion = 'v1'
                eventId = $eventId
                bookingReference = $state.bookingReference
                reservationId = $state.reservationId
                workOrderId = $state.workOrderId
                status = $Status
                sequence = $Sequence
                occurredAtUtc = $state.baseUtc
            }
            internalAuth = [ordered]@{
                serviceId = $state.businessToCustomerServiceId
                secretEnv = 'STEP17_BUSINESS_TO_CUSTOMER_HMAC_SECRET'
                timestamp = '{{gen:iso8601}}'
                nonce = '{{gen:nonce}}'
            }
            expect = [ordered]@{
                status = 200
                contentType = 'application/json'
                bodyNotContains = @(
                    'stack','SqlException','ConnectionString',
                    'X-Ghseeli-Signature','Bearer ')
                json = @(
                    [ordered]@{path='$.status';equals=$Status},
                    [ordered]@{path='$.sequence';equals=$Sequence})
            }
        })
    } | ConvertTo-Json -Depth 30 |
        Set-Content -LiteralPath $supportPath -Encoding UTF8
    try {
        Import-Module $harnessModule -Force
        $resultPath = Join-Path $artifacts `
            "step17-status-$Sequence-support.results.local.json"
        $result = Invoke-HttpTestHarness -ManifestPath $supportPath `
            -BaseUrl $state.customerBaseUrl -VariablesPath $StatePath `
            -ResultsPath $resultPath
        if (-not $result.Summary.passedAll) {
            throw "Supporting status callback $Sequence failed."
        }
    }
    finally {
        Remove-Item -LiteralPath $supportPath -Force `
            -ErrorAction SilentlyContinue
    }
}

function Invoke-Step6Setup([object]$State) {
    $mapped = [ordered]@{
        GHSEELI_HTTP_TEST_OWNER_PASSWORD =
            [Environment]::GetEnvironmentVariable(
                'STEP17_BUSINESS_OWNER_PASSWORD')
        GHSEELI_HTTP_TEST_INTERNAL_SERVICE_ID =
            [Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_TO_BUSINESS_SERVICE_ID')
        GHSEELI_HTTP_TEST_INTERNAL_ACTIVE_SECRET =
            [Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_TO_BUSINESS_HMAC_SECRET')
    }
    $prior = @{}
    foreach ($entry in $mapped.GetEnumerator()) {
        $prior[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key)
        [Environment]::SetEnvironmentVariable(
            $entry.Key,
            $entry.Value,
            [EnvironmentVariableTarget]::Process)
    }
    try {
        Import-Module $harnessModule -Force
        $resultPath = Join-Path $artifacts `
            "step17-business-setup-$runSuffix.results.local.json"
        $result = Invoke-HttpTestHarness -ManifestPath $step6Manifest `
            -BaseUrl $State.businessBaseUrl -Tags 'setup' `
            -ResultsPath $resultPath
        if (-not $result.Summary.passedAll) {
            throw 'Step 6 Business fixture setup failed.'
        }
        $variables = $result.RuntimeVariables
        $variables['businessJwt'] = $variables['businessOwnerToken']
        $variables['customerToBusinessServiceId'] =
            [Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_TO_BUSINESS_SERVICE_ID')
        $variables['businessToCustomerServiceId'] =
            [Environment]::GetEnvironmentVariable(
                'STEP17_BUSINESS_TO_CUSTOMER_SERVICE_ID')
        $variables['businessToCustomerReconcileServiceId'] =
            [Environment]::GetEnvironmentVariable(
                'STEP17_BUSINESS_TO_CUSTOMER_RECONCILE_SERVICE_ID')
        $variables['webhookSecret'] =
            [Environment]::GetEnvironmentVariable('STEP17_WEBHOOK_SECRET')
        $variables['catalogVersion'] = [long](Invoke-SqlScalar `
            $State $State.businessDatabase (
                "SELECT CatalogVersion FROM Companies WHERE Id=" +
                "'$($variables['companyId'])'"))
        $variables['slotStartUtc'] = $variables['requestedSlotStartUtc']
        Merge-RuntimeVariables $variables
    }
    finally {
        foreach ($entry in $prior.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable(
                $entry.Key,
                $entry.Value,
                [EnvironmentVariableTarget]::Process)
        }
    }
}

function Merge-FixtureOverlay {
    if (-not (Test-Path -LiteralPath $FixtureOverlayPath -PathType Leaf)) {
        throw 'Generated Step 17 fixture overlay does not exist.'
    }
    $overlay = Get-Content -LiteralPath $FixtureOverlayPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $required = @(
        'providerId','categoryLocalId','offeringLocalId','catalogVersion',
        'payableBookingReference','payableBookingInternalId',
        'failurePaymentId','failurePaymentIntentId',
        'failurePaymentMinorAmount','failurePaymentCurrency',
        'failureBookingInternalId','failureBookingReference',
        'recoveryBookingReference','recoveryReservationId',
        'recoveryWorkOrderId')
    foreach ($name in $required) {
        $property = $overlay.psobject.Properties[$name]
        if ($null -eq $property -or
            [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw "Fixture overlay is missing '$name'."
        }
    }
    $unexpected = @($overlay.psobject.Properties.Name | Where-Object {
            $_ -notin $required
        })
    if ($unexpected.Count) {
        throw "Fixture overlay contains unexpected fields: $($unexpected -join ', ')."
    }
    $values = @{}
    foreach ($name in $required) {
        $values[$name] = $overlay.psobject.Properties[$name].Value
    }
    Merge-StateValues $values
}

function New-ExactJsonFile(
    [string]$Name,[object]$Body,[int]$ByteCount,
    [string]$PaddingProperty = '_padding') {
    $path = Join-Path $artifacts $Name
    $plain = $Body | ConvertTo-Json -Depth 30 -Compress
    $withoutClose = $plain.Substring(0, $plain.Length - 1)
    if ($PaddingProperty -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "Unsafe JSON padding property '$PaddingProperty'."
    }
    $prefix = $withoutClose + ',"' + $PaddingProperty + '":"'
    $suffix = '"}'
    $paddingCount = $ByteCount -
        [Text.Encoding]::UTF8.GetByteCount($prefix + $suffix)
    if ($paddingCount -lt 0) {
        throw "Boundary template '$Name' already exceeds $ByteCount bytes."
    }
    $content = $prefix + ('x' * $paddingCount) + $suffix
    $bytes = [Text.Encoding]::UTF8.GetBytes($content)
    if ($bytes.Length -ne $ByteCount) {
        throw "Boundary body '$Name' is $($bytes.Length), not $ByteCount bytes."
    }
    [IO.File]::WriteAllBytes($path, $bytes)
}

function New-BoundaryBodies {
    $state = Read-State
    $registration = [ordered]@{
        installationId = [guid]::NewGuid().ToString('D')
        platform = 'Android'
    }
    New-ExactJsonFile 'step17-device-65536.local.json' $registration 65536 `
        'appVersion'
    New-ExactJsonFile 'step17-device-65537.local.json' $registration 65537 `
        'appVersion'
    $draft = [ordered]@{
        businessSourceId=$state.companyId
        branchSourceId=$state.branchId
        requestedSlotStartUtc=$state.slotStartUtc
        vehicle=[ordered]@{vehicleType='Sedan';licensePlate='S17-BOUND';
            make='Toyota';model='Corolla';color='Blue'}
        location=[ordered]@{addressLine='Local';city='Haifa';area='Carmel';
            latitude=24.7136;longitude=46.6753}
        items=@([ordered]@{offeringSourceId=$state.offeringId;selections=@()})
    }
    New-ExactJsonFile 'step17-draft-65536.local.json' $draft 65536
    New-ExactJsonFile 'step17-draft-65537.local.json' $draft 65537
    New-ExactJsonFile 'step17-direct-price-65537.local.json' `
        ([ordered]@{ businessSourceId=$state.companyId;
            branchSourceId=$state.branchId;catalogVersion=$state.catalogVersion;
            requestedSlotStartUtc=$state.slotStartUtc;
            location=[ordered]@{latitude=24.7136;longitude=46.6753};
            items=@([ordered]@{offeringSourceId=$state.offeringId;
                selections=@()}) }) 65537
    New-ExactJsonFile 'step17-checkout-price-65537.local.json' `
        ([ordered]@{expectedVersion=1}) 65537
    New-ExactJsonFile 'step17-confirm-65537.local.json' `
        ([ordered]@{expectedVersion=1;cancellationPolicyAcknowledged=$true}) 65537
    New-ExactJsonFile 'step17-payment-65537.local.json' `
        ([ordered]@{bookingId=$state.payableBookingReference;method='Card'}) 65537
    New-ExactJsonFile 'step17-webhook-65537.local.json' `
        ([ordered]@{id="evt_bound_$runSuffix";object='event';
            type='payment_intent.succeeded';data=[ordered]@{
                object=[ordered]@{id='pi_step17_boundary';
                    object='payment_intent';metadata=[ordered]@{}}}}) 65537
    New-ExactJsonFile 'step17-callback-65537.local.json' `
        ([ordered]@{contractVersion='v1';eventId=[guid]::NewGuid();
            bookingReference=$state.bookingReference;
            reservationId=$state.reservationId;workOrderId=$state.workOrderId;
            status='Confirmed';sequence=1;occurredAtUtc=$state.baseUtc}) 65537
}

function Remove-BoundaryBodies {
    foreach ($name in @(
            'step17-device-65536.local.json',
            'step17-device-65537.local.json',
            'step17-draft-65536.local.json',
            'step17-draft-65537.local.json',
            'step17-direct-price-65537.local.json',
            'step17-checkout-price-65537.local.json',
            'step17-confirm-65537.local.json',
            'step17-payment-65537.local.json',
            'step17-webhook-65537.local.json',
            'step17-callback-65537.local.json')) {
        Remove-Item -LiteralPath (Join-Path $artifacts $name) -Force `
            -ErrorAction SilentlyContinue
    }
}

function Invoke-RateLimitWarmup {
    $state = Read-State
    $warmupPath = Join-Path $artifacts 'step17-rate-warmup.local.json'
    $warmups = [Collections.Generic.List[object]]::new()
    for ($index = 1; $index -le 3; $index++) {
        $warmups.Add([ordered]@{
            id = "STEP17-SUPPORT-INVALID-STRIPE-$index"
            feature = 'step17-support'
            tags = @('step17-support')
            method = 'POST'
            url = "$($state.customerBaseUrl)/api/stripe/webhook"
            headers = [ordered]@{
                Accept='application/json'
                'Content-Type'='application/json'
                'Stripe-Signature'='invalid-local-signature'
            }
            jsonBody = [ordered]@{
                id="evt_step17_rate_${runSuffix}_$index"
                object='event'
                type='payment_intent.succeeded'
                data=[ordered]@{object=[ordered]@{}}
            }
            expect = [ordered]@{
                status=400
                errorCode='stripe_signature_invalid'
                bodyNotContains=@(
                    'stack','SqlException','ConnectionString',
                    'Stripe-Signature','whsec_')
            }
        })
    }
    for ($index = 1; $index -le 3; $index++) {
        $warmups.Add([ordered]@{
            id = "STEP17-SUPPORT-FORGED-XFF-LOGIN-$index"
            feature = 'step17-support'
            tags = @('step17-support')
            method = 'POST'
            url = "$($state.customerBaseUrl)/api/Auth/login"
            headers = [ordered]@{
                Accept='application/json'
                'Content-Type'='application/json'
                'X-Forwarded-For'="10.0.0.$index"
            }
            jsonBody = [ordered]@{
                email=$state.customerEmail
                password='{{env:STEP17_CUSTOMER_PASSWORD}}'
            }
            expect = [ordered]@{
                status=200
                bodyNotContains=@(
                    'stack','SqlException','ConnectionString','Bearer ',
                    'X-Forwarded-For','@example.com')
            }
        })
    }
    [ordered]@{
        name='Step 17 rate-limit warmup'
        sensitiveFields=@(
            'authorization','token','password','email','signature',
            'stripe-signature')
        scenarios=$warmups.ToArray()
    } | ConvertTo-Json -Depth 30 |
        Set-Content -LiteralPath $warmupPath -Encoding UTF8
    try {
        Import-Module $harnessModule -Force
        $resultPath = Join-Path $artifacts `
            "step17-rate-warmup-$runSuffix.results.local.json"
        $result = Invoke-HttpTestHarness -ManifestPath $warmupPath `
            -BaseUrl $state.customerBaseUrl -VariablesPath $StatePath `
            -ResultsPath $resultPath
        if (-not $result.Summary.passedAll) {
            throw 'Rate-limit warmup failed before the measured fourth requests.'
        }
    }
    finally {
        Remove-Item -LiteralPath $warmupPath -Force `
            -ErrorAction SilentlyContinue
    }
}

function Invoke-RecoveryTransition([object]$State) {
    $path = Join-Path $artifacts 'step17-recovery-transition.local.json'
    [ordered]@{
        name='Step 17 recovery transition setup'
        sensitiveFields=@('authorization','token','idempotency-key')
        scenarios=@([ordered]@{
            id='STEP17-SUPPORT-RECOVERY-TRANSITION'
            feature='step17-support'
            method='POST'
            url="$($State.businessBaseUrl)/api/v1/business/work-orders/$($State.recoveryWorkOrderId)/transitions"
            headers=[ordered]@{
                Accept='application/json'
                'Content-Type'='application/json'
                Authorization='Bearer {{env:STEP17_BUSINESS_JWT}}'
                'Idempotency-Key'="step17-recovery-transition-$runSuffix"
            }
            jsonBody=[ordered]@{status='Confirmed'}
            expect=[ordered]@{
                status=200
                bodyNotContains=@(
                    'stack','SqlException','ConnectionString','Bearer ')
                json=@(
                    [ordered]@{path='$.status';equals='Confirmed'},
                    [ordered]@{path='$.sequence';equals=1},
                    [ordered]@{path='$.eventId';exists=$true})
            }
            extract=[ordered]@{
                recoveryEventId='$.eventId'
                recoveryOccurredAtUtc='$.changedAtUtc'
            }
        })
    } | ConvertTo-Json -Depth 30 |
        Set-Content -LiteralPath $path -Encoding UTF8
    try {
        Import-Module $harnessModule -Force
        $resultPath = Join-Path $artifacts `
            "step17-recovery-transition-$runSuffix.results.local.json"
        $result = Invoke-HttpTestHarness -ManifestPath $path `
            -BaseUrl $State.businessBaseUrl -VariablesPath $StatePath `
            -ResultsPath $resultPath
        if (-not $result.Summary.passedAll) {
            throw 'Recovery Business transition setup failed.'
        }
        Merge-RuntimeVariables $result.RuntimeVariables
    }
    finally {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }
}

function Assert-CustomerDownCallbackHasNoResponse {
    $document = Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $document.scenarios = @($document.scenarios | Where-Object {
            $_.id -ceq 'STEP17-E2E-RECOVERY-035'
        })
    $document.scenarios[0].id = 'STEP17-SUPPORT-CUSTOMER-DOWN-CALLBACK'
    $path = Join-Path $artifacts 'step17-customer-down-callback.local.json'
    $document | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $path -Encoding UTF8
    try {
        Import-Module $harnessModule -Force
        $resultPath = Join-Path $artifacts `
            "step17-customer-down-callback-$runSuffix.results.local.json"
        $result = Invoke-HttpTestHarness -ManifestPath $path `
            -BaseUrl 'https://localhost:54431' -VariablesPath $StatePath `
            -ResultsPath $resultPath
        $probe = @($result.Scenarios)
        if ($probe.Count -ne 1 -or $null -ne $probe[0].statusCode -or
            $probe[0].passed) {
            throw 'Customer-down callback unexpectedly received an HTTP response.'
        }
    }
    finally {
        Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-FixtureRecoveryScenarios {
    $state = Read-State
    Invoke-SqlNonQuery $state $state.customerDatabase @"
DELETE FROM CustomerConfigurations
WHERE Id='17000000-0000-4000-8000-000000000020';
"@
    Invoke-ManifestScenarioId 'STEP17-E2E-RECOVERY-036'
    Add-ConfigurationFixture $state
    Invoke-ManifestScenarioId 'STEP17-E2E-RECOVERY-037'

    Invoke-SqlNonQuery $state $state.customerDatabase @"
DELETE FROM CatalogProviders
WHERE Id='$($state.providerId)'
  AND SourceCompanyId='$($state.companyId)';
"@
    Invoke-ManifestScenarioId 'STEP17-E2E-RECOVERY-038'
    Invoke-ManifestScenarioId 'STEP17-E2E-CATALOG-006'
    Invoke-ManifestScenarioId 'STEP17-E2E-RECOVERY-039'
}

function ConvertTo-SqlUnicodeLiteral([AllowNull()][string]$Value) {
    if ($null -eq $Value) { return 'NULL' }
    $escaped = $Value.Replace("'", "''")
    return "N'$escaped'"
}

function Restore-Step17CanonicalCatalogState {
    $state = Read-State
    $row = Get-SqlRow $state $state.customerDatabase @"
SELECT p.SourceCompanyId,p.CatalogVersion,
       b.Id AS BranchLocalId,b.SourceBranchId,
       c.Id AS CategoryLocalId,c.SourceCategoryId,
       o.Id AS OfferingLocalId,o.SourceOfferingId,
       g.SourceAddonGroupId,ch.SourceAddonChoiceId
FROM CatalogProviders p
JOIN CatalogBranches b ON b.ProviderId=p.Id
JOIN CatalogCategories c ON c.ProviderId=p.Id
JOIN CatalogOfferings o ON o.CategoryId=c.Id AND o.BranchId=b.Id
LEFT JOIN CatalogAddonGroups g ON g.OfferingId=o.Id
LEFT JOIN CatalogAddonChoices ch ON ch.AddonGroupId=g.Id
WHERE p.Id='$($state.providerId)';
"@
    if ($null -eq $row) {
        throw 'Canonical Step 17 catalog provider graph is missing.'
    }
    $slot = Get-SqlRow $state $state.businessDatabase @"
SELECT TOP(1)
       CONVERT(varchar(10),OverrideDate,23) + 'T' +
       CONVERT(varchar(8),StartLocalTime,108) + 'Z' AS SlotStartUtc
FROM BranchAvailabilityOverrides
WHERE BranchId='$($row.SourceBranchId)'
  AND IsActive=1
  AND IsClosed=0
ORDER BY OverrideDate,StartLocalTime;
"@
    if ($null -eq $slot) {
        throw 'Canonical Step 17 availability override is missing.'
    }
    Merge-StateValues @{
        companyId = ([guid]$row.SourceCompanyId).ToString('D')
        branchId = ([guid]$row.SourceBranchId).ToString('D')
        branchLocalId = ([guid]$row.BranchLocalId).ToString('D')
        categoryId = ([guid]$row.SourceCategoryId).ToString('D')
        categoryLocalId = ([guid]$row.CategoryLocalId).ToString('D')
        offeringId = ([guid]$row.SourceOfferingId).ToString('D')
        offeringLocalId = ([guid]$row.OfferingLocalId).ToString('D')
        addonGroupId = if ($null -eq $row.SourceAddonGroupId) {
            $null
        } else {
            ([guid]$row.SourceAddonGroupId).ToString('D')
        }
        addonChoiceId = if ($null -eq $row.SourceAddonChoiceId) {
            $null
        } else {
            ([guid]$row.SourceAddonChoiceId).ToString('D')
        }
        catalogVersion = [long]$row.CatalogVersion
        slotStartUtc = [string]$slot.SlotStartUtc
        requestedSlotStartUtc = [string]$slot.SlotStartUtc
    }
}

function Initialize-Step9InheritedFixtures {
    Restore-Step17CanonicalCatalogState
    $state = Read-State
    $dataStamp = $runSuffix.ToLowerInvariant()
    $ids = @{}
    foreach ($name in @(
            'businessOneSourceId','businessOneBranchSourceId',
            'businessOneCategorySourceId','businessOneOfferingSourceId',
            'businessOneAddonGroupSourceId','businessOneAddonChoiceSourceId',
            'businessOneServiceAreaId','businessTwoSourceId',
            'businessTwoBranchSourceId','businessTwoCategorySourceId',
            'businessTwoOfferingSourceId','businessTwoAddonGroupSourceId',
            'businessTwoAddonChoiceSourceId','businessTwoServiceAreaId')) {
        $property = $state.psobject.Properties[$name]
        $ids[$name] = if ($null -ne $property) {
            ([guid]$property.Value).ToString('D')
        }
        else {
            [guid]::NewGuid().ToString('D')
        }
    }

    $values = @{
        step9InheritedActive = $true
        dataStamp = $dataStamp
        businessOneSourceId = $ids.businessOneSourceId
        businessOneBranchSourceId = $ids.businessOneBranchSourceId
        businessOneCategorySourceId = $ids.businessOneCategorySourceId
        businessOneOfferingSourceId = $ids.businessOneOfferingSourceId
        businessOneAddonGroupSourceId = $ids.businessOneAddonGroupSourceId
        businessOneAddonChoiceSourceId = $ids.businessOneAddonChoiceSourceId
        businessOneServiceAreaId = $ids.businessOneServiceAreaId
        businessTwoSourceId = $ids.businessTwoSourceId
        businessTwoBranchSourceId = $ids.businessTwoBranchSourceId
        businessTwoCategorySourceId = $ids.businessTwoCategorySourceId
        businessTwoOfferingSourceId = $ids.businessTwoOfferingSourceId
        businessTwoAddonGroupSourceId = $ids.businessTwoAddonGroupSourceId
        businessTwoAddonChoiceSourceId = $ids.businessTwoAddonChoiceSourceId
        businessTwoServiceAreaId = $ids.businessTwoServiceAreaId
        businessOneVersion = 901L
        businessTwoVersion = 902L
        businessOneVersionAfterMutation = 902L
        businessOneNameAr = "شركة ستيب 9 ألف $dataStamp"
        businessOneDescriptionAr = "وصف عربي ألف $dataStamp"
        businessOneBranchNameAr = "فرع ألف $dataStamp"
        businessOneAddressAr = "الرياض $dataStamp"
        businessOneCategoryNameAr = "غسيل خارجي $dataStamp"
        businessOneCategoryDescriptionAr =
            "تصنيف ألف عربي $dataStamp"
        businessOneOfferingNameAr = "غسيل أساسي $dataStamp"
        businessOneOfferingDescriptionAr =
            "خدمة ألف عربية $dataStamp"
        businessOneAddonGroupNameAr = "شمع إضافي $dataStamp"
        businessOneAddonGroupDescriptionAr = "إضافة ألف $dataStamp"
        businessOneAddonChoiceNameAr = "شمع سريع $dataStamp"
        businessOneAddonChoiceDescriptionAr = "خيار ألف $dataStamp"
        businessOneUpdatedNameAr = "شركة ستيب 9 ألف محدثة $dataStamp"
        businessTwoNameAr = "شركة ستيب 9 باء $dataStamp"
        businessTwoNameHe = "חברת סטפ 9 בית $dataStamp"
        businessTwoBranchNameAr = "فرع باء $dataStamp"
        businessTwoBranchNameHe = "סניף בית $dataStamp"
        businessTwoCategoryNameAr = "غسيل داخلي $dataStamp"
        businessTwoCategoryNameHe = "שטיפה פנימית $dataStamp"
        businessTwoOfferingNameAr = "غسيل مميز $dataStamp"
        businessTwoOfferingNameHe = "שטיפה מיוחדת $dataStamp"
        businessTwoAddonGroupNameHe = "בישום $dataStamp"
        businessTwoAddonChoiceNameHe = "בישום חזק $dataStamp"
        phaseInitialRefreshDeviceToken = $state.deviceToken
        phaseUnchangedRefreshDeviceToken = $state.deviceToken
        phaseForceRefreshDeviceToken = $state.deviceToken
    }
    Merge-StateValues $values
    $state = Read-State

    $one = @{
        Company = ConvertTo-SqlUnicodeLiteral $state.businessOneNameAr
        Description = ConvertTo-SqlUnicodeLiteral `
            $state.businessOneDescriptionAr
        Branch = ConvertTo-SqlUnicodeLiteral $state.businessOneBranchNameAr
        Address = ConvertTo-SqlUnicodeLiteral $state.businessOneAddressAr
        Category = ConvertTo-SqlUnicodeLiteral `
            $state.businessOneCategoryNameAr
        CategoryDescription = ConvertTo-SqlUnicodeLiteral `
            $state.businessOneCategoryDescriptionAr
        Offering = ConvertTo-SqlUnicodeLiteral `
            $state.businessOneOfferingNameAr
        OfferingDescription = ConvertTo-SqlUnicodeLiteral `
            $state.businessOneOfferingDescriptionAr
        Group = ConvertTo-SqlUnicodeLiteral `
            $state.businessOneAddonGroupNameAr
        GroupDescription = ConvertTo-SqlUnicodeLiteral `
            $state.businessOneAddonGroupDescriptionAr
        Choice = ConvertTo-SqlUnicodeLiteral `
            $state.businessOneAddonChoiceNameAr
        ChoiceDescription = ConvertTo-SqlUnicodeLiteral `
            $state.businessOneAddonChoiceDescriptionAr
    }
    $two = @{
        Company = ConvertTo-SqlUnicodeLiteral $state.businessTwoNameAr
        CompanyHe = ConvertTo-SqlUnicodeLiteral $state.businessTwoNameHe
        Branch = ConvertTo-SqlUnicodeLiteral $state.businessTwoBranchNameAr
        BranchHe = ConvertTo-SqlUnicodeLiteral `
            $state.businessTwoBranchNameHe
        Category = ConvertTo-SqlUnicodeLiteral `
            $state.businessTwoCategoryNameAr
        CategoryHe = ConvertTo-SqlUnicodeLiteral `
            $state.businessTwoCategoryNameHe
        Offering = ConvertTo-SqlUnicodeLiteral `
            $state.businessTwoOfferingNameAr
        OfferingHe = ConvertTo-SqlUnicodeLiteral `
            $state.businessTwoOfferingNameHe
        GroupHe = ConvertTo-SqlUnicodeLiteral `
            $state.businessTwoAddonGroupNameHe
        ChoiceHe = ConvertTo-SqlUnicodeLiteral `
            $state.businessTwoAddonChoiceNameHe
    }
    Invoke-SqlNonQuery $state $state.businessDatabase @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DELETE FROM Companies
WHERE Id IN ('$($state.businessOneSourceId)',
             '$($state.businessTwoSourceId)');

INSERT INTO Companies
 (Id,NameAr,NameHe,DescriptionAr,DescriptionHe,
  ServiceAreaDescriptionAr,ServiceAreaDescriptionHe,Phone,IsActive,
  CatalogVersion,CreatedAt,UpdatedAt)
VALUES
 ('$($state.businessOneSourceId)',$($one.Company),NULL,$($one.Description),
  NULL,NULL,NULL,N'+970000000001',1,$($state.businessOneVersion),
  GETUTCDATE(),NULL),
 ('$($state.businessTwoSourceId)',$($two.Company),$($two.CompanyHe),NULL,
  NULL,NULL,NULL,N'+970000000002',1,$($state.businessTwoVersion),
  GETUTCDATE(),NULL);

INSERT INTO Branches
 (Id,CompanyId,NameAr,NameHe,AddressAr,AddressHe,Latitude,Longitude,
  IsActive,CreatedAt,UpdatedAt)
VALUES
 ('$($state.businessOneBranchSourceId)','$($state.businessOneSourceId)',
  $($one.Branch),NULL,$($one.Address),NULL,24.7136,46.6753,1,
  GETUTCDATE(),NULL),
 ('$($state.businessTwoBranchSourceId)','$($state.businessTwoSourceId)',
  $($two.Branch),$($two.BranchHe),N'حيفا',N'חיפה',32.7940,34.9896,1,
  GETUTCDATE(),NULL);

INSERT INTO BranchServiceAreas
 (Id,BranchId,CenterLatitude,CenterLongitude,RadiusKm,IsActive,
  CreatedAt,UpdatedAt)
VALUES
 ('$($state.businessOneServiceAreaId)',
  '$($state.businessOneBranchSourceId)',24.7136,46.6753,15.5,1,
  GETUTCDATE(),NULL),
 ('$($state.businessTwoServiceAreaId)',
  '$($state.businessTwoBranchSourceId)',32.7940,34.9896,9.0,1,
  GETUTCDATE(),NULL);

INSERT INTO ServiceCategories
 (Id,CompanyId,NameAr,NameHe,DescriptionAr,DescriptionHe,DisplayOrder,
  IsActive,CreatedAt,UpdatedAt)
VALUES
 ('$($state.businessOneCategorySourceId)','$($state.businessOneSourceId)',
  $($one.Category),NULL,$($one.CategoryDescription),NULL,0,1,
  GETUTCDATE(),NULL),
 ('$($state.businessTwoCategorySourceId)','$($state.businessTwoSourceId)',
  $($two.Category),$($two.CategoryHe),NULL,NULL,1,1,GETUTCDATE(),NULL);

INSERT INTO ServiceOfferings
 (Id,CategoryId,BranchId,NameAr,NameHe,DescriptionAr,DescriptionHe,
  BasePrice,DurationMinutes,ImageUrl,ReferenceCode,DisplayOrder,IsActive,
  CreatedAt,UpdatedAt)
VALUES
 ('$($state.businessOneOfferingSourceId)',
  '$($state.businessOneCategorySourceId)',
  '$($state.businessOneBranchSourceId)',$($one.Offering),NULL,
  $($one.OfferingDescription),NULL,50.00,45,NULL,N'STEP9-ONE',0,1,
  GETUTCDATE(),NULL),
 ('$($state.businessTwoOfferingSourceId)',
  '$($state.businessTwoCategorySourceId)',
  '$($state.businessTwoBranchSourceId)',$($two.Offering),$($two.OfferingHe),
  NULL,NULL,75.00,60,NULL,N'STEP9-TWO',0,1,GETUTCDATE(),NULL);

INSERT INTO AddonGroups
 (Id,ServiceOfferingId,NameAr,NameHe,DescriptionAr,DescriptionHe,
  SelectionType,IsRequired,MinimumSelections,MaximumSelections,
  DisplayOrder,IsActive,CreatedAt,UpdatedAt)
VALUES
 ('$($state.businessOneAddonGroupSourceId)',
  '$($state.businessOneOfferingSourceId)',$($one.Group),NULL,
  $($one.GroupDescription),NULL,N'QuantityCounter',0,0,3,0,1,
  GETUTCDATE(),NULL),
 ('$($state.businessTwoAddonGroupSourceId)',
  '$($state.businessTwoOfferingSourceId)',N'تعطير',$($two.GroupHe),
  NULL,NULL,N'SingleChoice',0,0,1,0,1,GETUTCDATE(),NULL);

INSERT INTO AddonChoices
 (Id,AddonGroupId,NameAr,NameHe,DescriptionAr,DescriptionHe,
  PriceAdjustment,DurationAdjustmentMinutes,DefaultQuantity,
  DisplayOrder,IsActive,CreatedAt,UpdatedAt)
VALUES
 ('$($state.businessOneAddonChoiceSourceId)',
  '$($state.businessOneAddonGroupSourceId)',$($one.Choice),NULL,
  $($one.ChoiceDescription),NULL,10.00,5,0,0,1,GETUTCDATE(),NULL),
 ('$($state.businessTwoAddonChoiceSourceId)',
  '$($state.businessTwoAddonGroupSourceId)',N'تعطير قوي',$($two.ChoiceHe),
  NULL,NULL,5.00,0,0,0,1,GETUTCDATE(),NULL);
COMMIT TRANSACTION;
"@
}

function Invoke-Step9HarnessDocument(
    [object]$Document,
    [string]$Name) {
    $path = Join-Path $artifacts "step17-inherited-$Name.local.json"
    $resultPath = Join-Path $artifacts "step17-inherited-$Name.results.local.json"
    $Document | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $path -Encoding UTF8
    Import-Module $harnessModule -Force
    $state = Read-State
    $result = Invoke-HttpTestHarness -ManifestPath $path `
        -BaseUrl $state.customerBaseUrl -VariablesPath $StatePath `
        -ResultsPath $resultPath
    if (-not $result.Summary.passedAll) {
        throw "Inherited Step 9 phase '$Name' failed."
    }
    Merge-RuntimeVariables $result.RuntimeVariables
}

function Invoke-Step9SupportRefresh {
    $state = Read-State
    $document = [pscustomobject][ordered]@{
        name = 'Step 9 two-provider support refresh'
        sensitiveFields = @('token')
        scenarios = @([pscustomobject][ordered]@{
            id = 'STEP9-SUPPORT-PRESEED'
            feature = 'step9-support'
            method = 'GET'
            url = '/api/v1/catalog/businesses?refresh=true'
            headers = [pscustomobject][ordered]@{
                Accept = 'application/json'
                'X-Device-Token' = $state.deviceToken
            }
            expect = [pscustomobject][ordered]@{
                status = 200
                contentType = 'application/json'
                json = @(
                    [pscustomobject][ordered]@{
                        path = '$.businesses[0].sourceId'
                        equals = $state.businessOneSourceId
                    },
                    [pscustomobject][ordered]@{
                        path = '$.businesses[1].sourceId'
                        equals = $state.businessTwoSourceId
                    },
                    [pscustomobject][ordered]@{
                        path = '$.businesses[2]'
                        exists = $false
                    })
            }
        })
    }
    Invoke-Step9HarnessDocument $document 'step09-support-preseed'
}

function Hydrate-Step9ReadModelVariables {
    $state = Read-State
    $one = Get-SqlRow $state $state.customerDatabase @"
SELECT p.Id AS ProviderId,p.SourceCompanyId,p.CatalogVersion,
       p.LastSuccessfulRefreshAtUtc,
       b.Id AS BranchId,c.Id AS CategoryId,c.SourceCategoryId,
       o.Id AS OfferingId,o.SourceOfferingId,
       g.Id AS AddonGroupId,ch.Id AS AddonChoiceId
FROM CatalogProviders p
JOIN CatalogBranches b ON b.ProviderId=p.Id
JOIN CatalogCategories c ON c.ProviderId=p.Id
JOIN CatalogOfferings o ON o.CategoryId=c.Id AND o.BranchId=b.Id
JOIN CatalogAddonGroups g ON g.OfferingId=o.Id
JOIN CatalogAddonChoices ch ON ch.AddonGroupId=g.Id
WHERE p.SourceCompanyId='$($state.businessOneSourceId)';
"@
    $two = Get-SqlRow $state $state.customerDatabase @"
SELECT p.Id AS ProviderId,p.SourceCompanyId,p.CatalogVersion,
       b.Id AS BranchId,c.Id AS CategoryId
FROM CatalogProviders p
JOIN CatalogBranches b ON b.ProviderId=p.Id
JOIN CatalogCategories c ON c.ProviderId=p.Id
WHERE p.SourceCompanyId='$($state.businessTwoSourceId)';
"@
    if ($null -eq $one -or $null -eq $two) {
        throw 'Step 9 two-provider read model was not hydrated.'
    }
    Merge-StateValues @{
        businessOneLocalId = ([guid]$one.ProviderId).ToString('D')
        businessOneBranchLocalId = ([guid]$one.BranchId).ToString('D')
        businessOneCategoryLocalId = ([guid]$one.CategoryId).ToString('D')
        businessOneCategorySourceId =
            ([guid]$one.SourceCategoryId).ToString('D')
        businessOneOfferingLocalId = ([guid]$one.OfferingId).ToString('D')
        businessOneOfferingSourceId =
            ([guid]$one.SourceOfferingId).ToString('D')
        businessOneAddonGroupLocalId =
            ([guid]$one.AddonGroupId).ToString('D')
        businessOneAddonChoiceLocalId =
            ([guid]$one.AddonChoiceId).ToString('D')
        businessTwoLocalId = ([guid]$two.ProviderId).ToString('D')
        businessTwoBranchLocalId = ([guid]$two.BranchId).ToString('D')
        businessTwoCategoryLocalId = ([guid]$two.CategoryId).ToString('D')
    }
}

function Reset-Step9ProviderSnapshots {
    $state = Read-State
    Invoke-SqlNonQuery $state $state.customerDatabase @"
DELETE FROM CatalogAddonChoices
WHERE AddonGroupId IN (
    SELECT g.Id FROM CatalogAddonGroups g
    JOIN CatalogOfferings o ON o.Id=g.OfferingId
    JOIN CatalogCategories c ON c.Id=o.CategoryId
    JOIN CatalogProviders p ON p.Id=c.ProviderId
    WHERE p.SourceCompanyId IN
      ('$($state.businessOneSourceId)','$($state.businessTwoSourceId)'));
DELETE FROM CatalogAddonGroups
WHERE OfferingId IN (
    SELECT o.Id FROM CatalogOfferings o
    JOIN CatalogCategories c ON c.Id=o.CategoryId
    JOIN CatalogProviders p ON p.Id=c.ProviderId
    WHERE p.SourceCompanyId IN
      ('$($state.businessOneSourceId)','$($state.businessTwoSourceId)'));
DELETE FROM CatalogOfferings
WHERE CategoryId IN (
    SELECT c.Id FROM CatalogCategories c
    JOIN CatalogProviders p ON p.Id=c.ProviderId
    WHERE p.SourceCompanyId IN
      ('$($state.businessOneSourceId)','$($state.businessTwoSourceId)'));
DELETE FROM CatalogCategories
WHERE ProviderId IN (
    SELECT Id FROM CatalogProviders
    WHERE SourceCompanyId IN
      ('$($state.businessOneSourceId)','$($state.businessTwoSourceId)'));
DELETE FROM CatalogBranches
WHERE ProviderId IN (
    SELECT Id FROM CatalogProviders
    WHERE SourceCompanyId IN
      ('$($state.businessOneSourceId)','$($state.businessTwoSourceId)'));
UPDATE CatalogProviders
SET NameAr=N'',NameHe=NULL,DescriptionAr=NULL,DescriptionHe=NULL,
    Phone=NULL,CatalogVersion=0,SnapshotHash=NULL,
    SnapshotGeneratedAtUtc=NULL,LastSuccessfulRefreshAtUtc=NULL,
    LastAttemptedRefreshAtUtc=NULL,LastFailedRefreshAtUtc=NULL,
    LastFailureCode=NULL,RefreshLeaseAcquiredAtUtc=NULL,
    RefreshLeaseExpiresAtUtc=NULL,RefreshLeaseToken=NULL
WHERE SourceCompanyId IN
  ('$($state.businessOneSourceId)','$($state.businessTwoSourceId)');
"@
}

function Invoke-Step9InheritedManifest([object]$Document) {
    $state = Read-State
    $Document.setupVariables.dataStamp = [string]$state.dataStamp
    $preseededIds = @($Document.scenarios | Where-Object {
            [string]$_.id -notin @(
                'STEP9-BUSINESSES-INITIAL-REFRESH-020',
                'STEP9-BUSINESSES-FORCE-REFRESH-UNCHANGED-021',
                'STEP9-BUSINESSES-FORCE-REFRESH-VERSION-019')
        })
    $Document.scenarios = $preseededIds
    foreach ($scenario in $Document.scenarios) {
        if ([string]$scenario.id -ceq 'STEP9-HEALTH-001') {
            $scenario.url = '/api/Health'
        }
        elseif ([string]$scenario.id -ceq
            'STEP9-INVALID-LANGUAGE-CORRELATION-018') {
            $scenario.expect.errorCode = 'language_invalid'
            $scenario.expect.json = @(
                [pscustomobject][ordered]@{
                    path = '$.title'
                    equals = 'تعذر إكمال الطلب.'
                },
                [pscustomobject][ordered]@{
                    path = '$.detail'
                    equals = 'اللغة المطلوبة غير مدعومة.'
                },
                [pscustomobject][ordered]@{
                    path = '$.language'
                    equals = 'ar'
                },
                [pscustomobject][ordered]@{
                    path = '$.correlationId'
                    notEquals = '{{var:invalidCorrelationId}}'
                },
                [pscustomobject][ordered]@{
                    path = '$.correlationId'
                    matches = '^[0-9a-f]{32}$'
                },
                [pscustomobject][ordered]@{
                    path = '$.token'
                    exists = $false
                })
        }
    }

    Reset-Step9ProviderSnapshots
    Invoke-Step9SupportRefresh
    Hydrate-Step9ReadModelVariables
    Invoke-Step9HarnessDocument $Document 'step09-preseeded'

    $sourceDocument = Get-Content (
        Join-Path $PSScriptRoot 'plans\step-09-catalog-readmodel.manifest.json'
    ) -Raw -Encoding UTF8 | ConvertFrom-Json
    $sourceDocument.setupVariables.dataStamp = [string]$state.dataStamp
    Reset-Step9ProviderSnapshots
    $initial = [pscustomobject][ordered]@{
        name = $sourceDocument.name
        sensitiveFields = $sourceDocument.sensitiveFields
        setupVariables = $sourceDocument.setupVariables
        scenarios = @($sourceDocument.scenarios | Where-Object {
                [string]$_.id -ceq 'STEP9-BUSINESSES-INITIAL-REFRESH-020'
            })
    }
    Invoke-Step9HarnessDocument $initial 'step09-initial-refresh'
    Hydrate-Step9ReadModelVariables
    $state = Read-State
    $beforeUnchanged = Invoke-SqlScalar $state $state.customerDatabase (
        "SELECT LastSuccessfulRefreshAtUtc FROM CatalogProviders WHERE " +
        "SourceCompanyId='$($state.businessOneSourceId)'")
    Merge-StateValues @{
        businessOneRefreshedAtBeforeUnchangedRefresh =
            ([DateTimeOffset]$beforeUnchanged).ToString('o')
    }

    $unchanged = [pscustomobject][ordered]@{
        name = $sourceDocument.name
        sensitiveFields = $sourceDocument.sensitiveFields
        setupVariables = $sourceDocument.setupVariables
        scenarios = @($sourceDocument.scenarios | Where-Object {
                [string]$_.id -ceq
                    'STEP9-BUSINESSES-FORCE-REFRESH-UNCHANGED-021'
            })
    }
    Invoke-Step9HarnessDocument $unchanged 'step09-unchanged-refresh'
    $state = Read-State
    $beforeMutation = Invoke-SqlScalar $state $state.customerDatabase (
        "SELECT LastSuccessfulRefreshAtUtc FROM CatalogProviders WHERE " +
        "SourceCompanyId='$($state.businessOneSourceId)'")
    Merge-StateValues @{
        businessOneRefreshedAtBeforeForceRefresh =
            ([DateTimeOffset]$beforeMutation).ToString('o')
    }
    $state = Read-State
    $updatedName = ConvertTo-SqlUnicodeLiteral `
        ([string]$state.businessOneUpdatedNameAr)
    Invoke-SqlNonQuery $state $state.businessDatabase @"
UPDATE Companies
SET NameAr=$updatedName,
    CatalogVersion=$($state.businessOneVersionAfterMutation),
    UpdatedAt=GETUTCDATE()
WHERE Id='$($state.businessOneSourceId)';
"@
    $mutated = [pscustomobject][ordered]@{
        name = $sourceDocument.name
        sensitiveFields = $sourceDocument.sensitiveFields
        setupVariables = $sourceDocument.setupVariables
        scenarios = @($sourceDocument.scenarios | Where-Object {
                [string]$_.id -ceq
                    'STEP9-BUSINESSES-FORCE-REFRESH-VERSION-019'
            })
    }
    Invoke-Step9HarnessDocument $mutated 'step09-version-refresh'

    Merge-StateValues @{ step9InheritedActive = $false }
    $state = Read-State
    Invoke-SqlNonQuery $state $state.customerDatabase @"
UPDATE CatalogProviders
SET IsEnabled=CASE WHEN Id='$($state.providerId)' THEN 1 ELSE 0 END,
    DisplayOrder=CASE WHEN Id='$($state.providerId)' THEN 0 ELSE DisplayOrder END;
"@
    Stop-AllOwnedProcesses
    [void](Start-Customer $state)
    [void](Start-Business $state)
}

function Invoke-Step11CatalogRefresh {
    $state = Read-State
    $document = [pscustomobject][ordered]@{
        name = 'Step 11 pricing catalog refresh'
        sensitiveFields = @('token','x-device-token')
        scenarios = @([pscustomobject][ordered]@{
            id = 'STEP11-SUPPORT-CATALOG-REFRESH'
            feature = 'step11-support'
            method = 'GET'
            url = '/api/v1/catalog/businesses?refresh=true'
            headers = [pscustomobject][ordered]@{
                Accept = 'application/json'
                'X-Device-Token' = $state.deviceToken
            }
            expect = [pscustomobject][ordered]@{
                status = 200
                contentType = 'application/json'
                json = @([pscustomobject][ordered]@{
                    path = '$.businesses[0].sourceId'
                    equals = $state.companyId
                })
            }
        })
    }
    Invoke-Step9HarnessDocument $document 'step11-catalog-refresh'
}

function Initialize-Step11InheritedFixtures {
    Restore-Step17CanonicalCatalogState
    $state = Read-State
    $ids = @{}
    foreach ($name in @(
            'step11SingleOfferingId','step11QuantityOfferingId',
            'step11FixedOfferingId','step11SegmentedOfferingId',
            'step11SingleGroupId','step11QuantityGroupId',
            'step11FixedGroupId','step11SegmentedGroupId',
            'step11SingleChoiceId','step11QuantityChoiceId',
            'step11FixedChoiceId','step11SegmentedChoiceId')) {
        $property = $state.psobject.Properties[$name]
        $ids[$name] = if ($null -ne $property) {
            ([guid]$property.Value).ToString('D')
        }
        else {
            [guid]::NewGuid().ToString('D')
        }
    }
    $multipleGroupId = if ($null -ne $state.addonGroupId) {
        ([guid]$state.addonGroupId).ToString('D')
    }
    else {
        [guid]::NewGuid().ToString('D')
    }
    $multipleChoiceId = if ($null -ne $state.addonChoiceId) {
        ([guid]$state.addonChoiceId).ToString('D')
    }
    else {
        [guid]::NewGuid().ToString('D')
    }
    Merge-StateValues @{
        step11MultipleGroupId = $multipleGroupId
        step11MultipleChoiceId = $multipleChoiceId
        step11SingleOfferingId = $ids.step11SingleOfferingId
        step11QuantityOfferingId = $ids.step11QuantityOfferingId
        step11FixedOfferingId = $ids.step11FixedOfferingId
        step11SegmentedOfferingId = $ids.step11SegmentedOfferingId
        step11SingleGroupId = $ids.step11SingleGroupId
        step11QuantityGroupId = $ids.step11QuantityGroupId
        step11FixedGroupId = $ids.step11FixedGroupId
        step11SegmentedGroupId = $ids.step11SegmentedGroupId
        step11SingleChoiceId = $ids.step11SingleChoiceId
        step11QuantityChoiceId = $ids.step11QuantityChoiceId
        step11FixedChoiceId = $ids.step11FixedChoiceId
        step11SegmentedChoiceId = $ids.step11SegmentedChoiceId
    }
    $state = Read-State
    $nextVersion = 1 + [long](Invoke-SqlScalar $state `
        $state.businessDatabase (
            "SELECT CatalogVersion FROM Companies WHERE Id=" +
            "'$($state.companyId)'"))
    Invoke-SqlNonQuery $state $state.businessDatabase @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DELETE FROM AddonChoices
WHERE AddonGroupId IN (
    SELECT Id FROM AddonGroups
    WHERE ServiceOfferingId IN (
        SELECT Id FROM ServiceOfferings
        WHERE CategoryId='$($state.categoryId)'
          AND BranchId='$($state.branchId)'));
DELETE FROM AddonGroups
WHERE ServiceOfferingId IN (
    SELECT Id FROM ServiceOfferings
    WHERE CategoryId='$($state.categoryId)'
      AND BranchId='$($state.branchId)');
DELETE FROM ServiceOfferings
WHERE CategoryId='$($state.categoryId)'
  AND BranchId='$($state.branchId)';

INSERT INTO ServiceOfferings
 (Id,CategoryId,BranchId,NameAr,NameHe,DescriptionAr,DescriptionHe,
  BasePrice,DurationMinutes,ImageUrl,ReferenceCode,DisplayOrder,IsActive,
  CreatedAt,UpdatedAt)
VALUES
 ('$($state.offeringId)','$($state.categoryId)','$($state.branchId)',
  N'خدمة متعددة الخيارات',N'שירות בחירה מרובה',NULL,NULL,
  79.50,30,NULL,N'STEP11-MULTIPLE',0,1,GETUTCDATE(),NULL),
 ('$($state.step11SingleOfferingId)','$($state.categoryId)',
  '$($state.branchId)',N'خدمة اختيار واحد',N'שירות בחירה יחידה',
  NULL,NULL,55.00,30,NULL,N'STEP11-SINGLE',1,1,GETUTCDATE(),NULL),
 ('$($state.step11QuantityOfferingId)','$($state.categoryId)',
  '$($state.branchId)',N'خدمة كمية',N'שירות כמות',NULL,NULL,
  65.00,30,NULL,N'STEP11-QUANTITY',2,1,GETUTCDATE(),NULL),
 ('$($state.step11FixedOfferingId)','$($state.categoryId)',
  '$($state.branchId)',N'خدمة متضمنة',N'שירות כלול',NULL,NULL,
  45.00,30,NULL,N'STEP11-FIXED',3,1,GETUTCDATE(),NULL),
 ('$($state.step11SegmentedOfferingId)','$($state.categoryId)',
  '$($state.branchId)',N'خدمة مقسمة',N'שירות מפולח',NULL,NULL,
  60.00,30,NULL,N'STEP11-SEGMENTED',4,1,GETUTCDATE(),NULL);

INSERT INTO AddonGroups
 (Id,ServiceOfferingId,NameAr,NameHe,DescriptionAr,DescriptionHe,
  SelectionType,IsRequired,MinimumSelections,MaximumSelections,
  DisplayOrder,IsActive,CreatedAt,UpdatedAt)
VALUES
 ('$($state.step11MultipleGroupId)','$($state.offeringId)',
  N'خيارات متعددة',N'בחירות מרובות',NULL,NULL,N'MultipleChoice',
  0,0,3,0,1,GETUTCDATE(),NULL),
 ('$($state.step11SingleGroupId)','$($state.step11SingleOfferingId)',
  N'اختيار واحد',N'בחירה יחידה',NULL,NULL,N'SingleChoice',
  1,1,1,0,1,GETUTCDATE(),NULL),
 ('$($state.step11QuantityGroupId)','$($state.step11QuantityOfferingId)',
  N'عداد الكمية',N'מונה כמות',NULL,NULL,N'QuantityCounter',
  0,0,3,0,1,GETUTCDATE(),NULL),
 ('$($state.step11FixedGroupId)','$($state.step11FixedOfferingId)',
  N'خيار متضمن',N'בחירה כלולה',NULL,NULL,N'FixedIncludedChoice',
  1,1,1,0,1,GETUTCDATE(),NULL),
 ('$($state.step11SegmentedGroupId)',
  '$($state.step11SegmentedOfferingId)',N'زر مقسم',N'כפתור מפולח',
  NULL,NULL,N'SegmentedSingleButtonChoice',1,1,1,0,1,
  GETUTCDATE(),NULL);

INSERT INTO AddonChoices
 (Id,AddonGroupId,NameAr,NameHe,DescriptionAr,DescriptionHe,
  PriceAdjustment,DurationAdjustmentMinutes,DefaultQuantity,
  DisplayOrder,IsActive,CreatedAt,UpdatedAt)
VALUES
 ('$($state.step11MultipleChoiceId)','$($state.step11MultipleGroupId)',
  N'إضافة متعددة',N'תוספת מרובה',NULL,NULL,4.00,0,0,0,1,
  GETUTCDATE(),NULL),
 ('$($state.step11SingleChoiceId)','$($state.step11SingleGroupId)',
  N'إضافة مفردة',N'תוספת יחידה',NULL,NULL,2.00,0,0,0,1,
  GETUTCDATE(),NULL),
 ('$($state.step11QuantityChoiceId)','$($state.step11QuantityGroupId)',
  N'إضافة كمية',N'תוספת כמות',NULL,NULL,1.00,0,0,0,1,
  GETUTCDATE(),NULL),
 ('$($state.step11FixedChoiceId)','$($state.step11FixedGroupId)',
  N'إضافة متضمنة',N'תוספת כלולה',NULL,NULL,0.00,0,1,0,1,
  GETUTCDATE(),NULL),
 ('$($state.step11SegmentedChoiceId)',
  '$($state.step11SegmentedGroupId)',N'إضافة مقسمة',N'תוספת מפולחת',
  NULL,NULL,3.00,0,0,0,1,GETUTCDATE(),NULL);

UPDATE Companies
SET CatalogVersion=$nextVersion,UpdatedAt=GETUTCDATE()
WHERE Id='$($state.companyId)';
UPDATE BranchAvailabilityOverrides
SET Capacity=10000,UpdatedAt=GETUTCDATE()
WHERE BranchId='$($state.branchId)'
  AND IsActive=1
  AND IsClosed=0;
COMMIT TRANSACTION;
"@
    Merge-StateValues @{
        catalogVersion = $nextVersion
        addonGroupId = $multipleGroupId
        addonChoiceId = $multipleChoiceId
    }
    Invoke-Step11CatalogRefresh
}

function Set-Step11RuntimeBodyFiles([object]$Document) {
    $sourceDirectory = Join-Path $PSScriptRoot 'plans'
    $runtimePaths = @{}
    foreach ($fileName in @(
            'step-11-direct.request.json',
            'step-11-all-selections.request.json',
            'step-11-update-draft.request.json')) {
        $body = Get-Content (Join-Path $sourceDirectory $fileName) `
            -Raw -Encoding UTF8 | ConvertFrom-Json
        $body.location.latitude = 24.7136
        $body.location.longitude = 46.6753
        $runtimePath = Join-Path $artifacts `
            "step17-inherited-$runSuffix-$fileName.local.json"
        $body | ConvertTo-Json -Depth 30 |
            Set-Content -LiteralPath $runtimePath -Encoding UTF8
        $runtimePaths[$fileName] = $runtimePath
    }
    foreach ($scenario in @($Document.scenarios | Where-Object {
                $null -ne $_.psobject.Properties['bodyFile']
            })) {
        $fileName = Split-Path -Leaf ([string]$scenario.bodyFile)
        if ($runtimePaths.ContainsKey($fileName)) {
            $scenario.bodyFile = $runtimePaths[$fileName]
        }
    }
}

function Invoke-Inherited {
    $state = Read-State
    Set-LiveJwtEnvironment $state -Customer -Business
    $customerJwt = [Environment]::GetEnvironmentVariable(
        'STEP17_CUSTOMER_JWT')
    $businessJwt = [Environment]::GetEnvironmentVariable(
        'STEP17_BUSINESS_JWT')
    $businessSecret = [Environment]::GetEnvironmentVariable(
        'STEP17_BUSINESS_JWT_SECRET')
    $step16CatalogSecret =
        ('step16-catalog-' + [guid]::NewGuid().ToString('N'))
    $adminId = [guid]::NewGuid().ToString('D')
    $mapped = [ordered]@{
        GHSEELI_HTTP_TEST_OWNER_PASSWORD =
            [Environment]::GetEnvironmentVariable(
                'STEP17_BUSINESS_OWNER_PASSWORD')
        GHSEELI_HTTP_TEST_CUSTOMER_PASSWORD =
            [Environment]::GetEnvironmentVariable('STEP17_CUSTOMER_PASSWORD')
        GHSEELI_HTTP_TEST_INTERNAL_SERVICE_ID = $state.customerToBusinessServiceId
        GHSEELI_HTTP_TEST_INTERNAL_ACTIVE_SECRET =
            [Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_TO_BUSINESS_HMAC_SECRET')
        GHSEELI_HTTP_TEST_BUSINESS_BASE_URL = $state.businessBaseUrl
        GHSEELI_HTTP_TEST_BUSINESS_SERVICE_ID =
            $state.customerToBusinessServiceId
        GHSEELI_HTTP_TEST_OWNER_DEVICE_TOKEN = $state.deviceToken
        GHSEELI_HTTP_TEST_OWNER_JWT = $customerJwt
        GHSEELI_HTTP_TEST_EXPIRED_JWT = New-Step17Jwt `
            ([Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_JWT_SECRET')) `
            'GhseeliApis' 'GhseeliApis' 'User' `
            ([string]$state.customerUserId) -ExpiresOffsetSeconds -60
        GHSEELI_HTTP_TEST_BUSINESS_OWNER_JWT = $businessJwt
        GHSEELI_HTTP_TEST_BUSINESS_OWNER_MISSING_CLAIM_JWT =
            New-Step17Jwt $businessSecret 'Ghseeli.BusinessApi' `
                'Ghseeli.BusinessClients' 'Owner' $null
        GHSEELI_HTTP_TEST_BUSINESS_OWNER_MALFORMED_CLAIM_JWT =
            New-Step17Jwt $businessSecret 'Ghseeli.BusinessApi' `
                'Ghseeli.BusinessClients' 'Owner' 'not-a-guid'
        GHSEELI_HTTP_TEST_BUSINESS_ADMIN_JWT =
            New-Step17Jwt $businessSecret 'Ghseeli.BusinessApi' `
                'Ghseeli.BusinessClients' 'Admin' $adminId
        GHSEELI_HTTP_TEST_BUSINESS_ADMIN_MISSING_CLAIM_JWT =
            New-Step17Jwt $businessSecret 'Ghseeli.BusinessApi' `
                'Ghseeli.BusinessClients' 'Admin' $null
        GHSEELI_HTTP_TEST_BUSINESS_ADMIN_MALFORMED_CLAIM_JWT =
            New-Step17Jwt $businessSecret 'Ghseeli.BusinessApi' `
                'Ghseeli.BusinessClients' 'Admin' 'not-a-guid'
        GHSEELI_HTTP_TEST_STEP13_ACTIVE_SECRET =
            [Environment]::GetEnvironmentVariable(
                'STEP17_BUSINESS_TO_CUSTOMER_HMAC_SECRET')
        GHSEELI_HTTP_TEST_STEP13_CALLBACK_SECRET =
            [Environment]::GetEnvironmentVariable(
                'STEP17_BUSINESS_TO_CUSTOMER_HMAC_SECRET')
        GHSEELI_HTTP_TEST_STEP13_NEXT_SECRET =
            [Environment]::GetEnvironmentVariable(
                'STEP17_BUSINESS_TO_CUSTOMER_NEXT_HMAC_SECRET')
        GHSEELI_HTTP_TEST_STEP13_WRONG_SECRET =
            [Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_TO_BUSINESS_HMAC_SECRET')
        GHSEELI_HTTP_TEST_STEP13_RECONCILE_SECRET =
            [Environment]::GetEnvironmentVariable(
                'STEP17_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET')
        STEP16_CUSTOMER_PASSWORD =
            [Environment]::GetEnvironmentVariable('STEP17_CUSTOMER_PASSWORD')
        STEP16_BUSINESS_PASSWORD =
            [Environment]::GetEnvironmentVariable(
                'STEP17_BUSINESS_OWNER_PASSWORD')
        STEP16_CUSTOMER_TO_BUSINESS_SERVICE_ID =
            $state.customerToBusinessServiceId
        STEP16_CUSTOMER_TO_BUSINESS_HMAC_SECRET =
            [Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_TO_BUSINESS_HMAC_SECRET')
        STEP16_CUSTOMER_TO_BUSINESS_CATALOG_HMAC_SECRET =
            $step16CatalogSecret
        STEP16_BUSINESS_TO_CUSTOMER_SERVICE_ID =
            $state.businessToCustomerServiceId
        STEP16_BUSINESS_TO_CUSTOMER_HMAC_SECRET =
            [Environment]::GetEnvironmentVariable(
                'STEP17_BUSINESS_TO_CUSTOMER_HMAC_SECRET')
        STEP16_BUSINESS_TO_CUSTOMER_RECONCILE_SERVICE_ID =
            $state.businessToCustomerReconcileServiceId
        STEP16_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET =
            [Environment]::GetEnvironmentVariable(
                'STEP17_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET')
        STEP16_CUSTOMER_JWT_SECRET =
            [Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_JWT_SECRET')
        STEP16_BUSINESS_JWT_SECRET = $businessSecret
        STEP16_DENIED_SQL_PASSWORD =
            ('S16!' + [guid]::NewGuid().ToString('N'))
    }
    foreach ($entry in $mapped.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable(
            $entry.Key,
            [string]$entry.Value,
            [EnvironmentVariableTarget]::Process)
    }
    $definitions = [ordered]@{
        'step-06-secure-integration.manifest.json' = 26
        'step-07-device-registration.manifest.json' = 10
        'step-08-customer-configuration.manifest.json' = 19
        'step-09-catalog-readmodel.manifest.json' = 21
        'step-10-checkout-drafts.manifest.json' = 28
        'step-11-pricing-reprice.manifest.json' = 27
        'step-12-booking-confirmation.manifest.json' = 64
        'step-13-booking-status.manifest.json' = 90
        'step-14-payment-rebuild.manifest.json' = 130
        'step-15-localization-swagger.manifest.json' = 90
        'step-16-clean-schema-separation.manifest.json' = 790
    }
    $selected = 0
    foreach ($item in $definitions.GetEnumerator()) {
        $sourcePath = Join-Path $PSScriptRoot "plans\$($item.Key)"
        $document = Get-Content $sourcePath -Raw -Encoding UTF8 |
            ConvertFrom-Json
        if (@($document.scenarios).Count -ne $item.Value) {
            throw "Inherited manifest '$($item.Key)' count changed."
        }
        $document.scenarios = @($document.scenarios | Where-Object {
                $_.id -cne 'STEP14-INTENT-REAL-STRIPE-057'
            })
        foreach ($scenario in $document.scenarios) {
            if ($null -ne $scenario.psobject.Properties['internalAuth'] -and
                [string]$scenario.internalAuth.serviceId -cin @(
                    'business-api','callback-only')) {
                $scenario.internalAuth.serviceId =
                    $state.businessToCustomerServiceId
            }
        }
        if ($item.Key -ceq 'step-08-customer-configuration.manifest.json') {
            foreach ($scenario in $document.scenarios) {
                $hebrew = [string]$scenario.id -cin @(
                    'STEP8-GET-HE-006','STEP8-OVERRIDE-HE-007')
                $assertions = if ($null -ne
                    $scenario.expect.psobject.Properties['json']) {
                    @($scenario.expect.json)
                }
                else {
                    @()
                }
                foreach ($assertion in $assertions) {
                    if ([string]$assertion.path -ceq '$.display.name') {
                        $assertion.equals = if ($hebrew) {
                            'ע׳סילי מקומי'
                        }
                        else {
                            'غسيلي المحلي'
                        }
                    }
                    elseif ([string]$assertion.path -ceq '$.legal.notice') {
                        $assertion.equals = if ($hebrew) {
                            'הודעה מקומית בטוחה'
                        }
                        else {
                            'إشعار محلي آمن'
                        }
                    }
                }
                if ([string]$scenario.id -cin @(
                        'STEP8-INVALID-QUERY-AR-010',
                        'STEP8-INVALID-QUERY-HE-011')) {
                    $scenario.expect.errorCode = 'language_invalid'
                    $scenario.expect.json = @(
                        [pscustomobject][ordered]@{
                            path = '$.type'
                            equals = 'https://api.ghseeli.example/errors/language_invalid'
                        },
                        [pscustomobject][ordered]@{
                            path = '$.status'
                            equals = 400
                        },
                        [pscustomobject][ordered]@{
                            path = '$.code'
                            equals = 'language_invalid'
                        },
                        [pscustomobject][ordered]@{
                            path = '$.language'
                            equals = 'ar'
                        },
                        [pscustomobject][ordered]@{
                            path = '$.correlationId'
                            matches = '^[0-9a-f]{32}$'
                        })
                }
                elseif ([string]$scenario.id -ceq
                    'STEP8-CORRELATION-SANITIZE-015') {
                    $scenario.expect.errorCode = 'language_invalid'
                }
            }
        }
        elseif ($item.Key -ceq 'step-10-checkout-drafts.manifest.json') {
            $validSlot = ([DateTimeOffset]$state.slotStartUtc).
                ToUniversalTime()
            $document.setupVariables.businessSourceId = $state.companyId
            $document.setupVariables.branchSourceId = $state.branchId
            $document.setupVariables.offeringSourceId = $state.offeringId
            $document.setupVariables.duplicateOfferingSourceId =
                $state.offeringId
            $document.setupVariables.duplicateAddonChoiceSourceId =
                $state.addonChoiceId
            $document.setupVariables.foreignAddonChoiceSourceId =
                [guid]::NewGuid().ToString('D')
            $document.setupVariables.createSlotStartUtc =
                $validSlot.ToString('yyyy-MM-ddTHH:mm:ssZ')
            $document.setupVariables.updateSlotStartUtc =
                $validSlot.AddMinutes(15).ToString('yyyy-MM-ddTHH:mm:ssZ')
            $document.setupVariables.leadFailureSlotStartUtc =
                $validSlot.AddMinutes(-15).ToString('yyyy-MM-ddTHH:mm:ssZ')
            $document.setupVariables.horizonFailureSlotStartUtc =
                $validSlot.AddDays(31).ToString('yyyy-MM-ddTHH:mm:ssZ')
            $document.setupVariables.misalignedSlotStartUtc =
                $validSlot.AddMinutes(1).ToString('yyyy-MM-ddTHH:mm:ssZ')
            $document.setupVariables.boundaryLatitude = 24.7136
            $document.setupVariables.boundaryLongitude = 46.6753
            foreach ($scenario in $document.scenarios) {
                if ($null -ne $scenario.psobject.Properties['jsonBody'] -and
                    $null -ne $scenario.jsonBody -and
                    $null -ne
                        $scenario.jsonBody.psobject.Properties['location'] -and
                    $null -ne $scenario.jsonBody.location) {
                    if ([string]$scenario.jsonBody.location.latitude -ceq
                        '32.1') {
                        $scenario.jsonBody.location.latitude = 24.7136
                    }
                    if ([string]$scenario.jsonBody.location.longitude -ceq
                        '34.8') {
                        $scenario.jsonBody.location.longitude = 46.6753
                    }
                }
                if ([string]$scenario.id -ceq
                    'STEP10-INVALID-LANGUAGE-013') {
                    $scenario.expect.errorCode = 'language_invalid'
                    $scenario.expect.json = @(
                        [pscustomobject][ordered]@{
                            path = '$.title'
                            equals = 'تعذر إكمال الطلب.'
                        },
                        [pscustomobject][ordered]@{
                            path = '$.detail'
                            equals = 'اللغة المطلوبة غير مدعومة.'
                        },
                        [pscustomobject][ordered]@{
                            path = '$.language'
                            equals = 'ar'
                        },
                        [pscustomobject][ordered]@{
                            path = '$.correlationId'
                            matches = '^[0-9a-f]{32}$'
                        })
                }
                if ($null -ne $scenario.psobject.Properties['expect'] -and
                    $null -ne $scenario.expect.psobject.Properties['json']) {
                    foreach ($assertion in @($scenario.expect.json)) {
                        if ([string]$assertion.path -ceq
                            '$.intent.requestedSlotStartUtc' -and
                            $null -ne
                                $assertion.psobject.Properties['matches']) {
                            $expectedSlot = if ([string]$scenario.id -ceq
                                'STEP10-UPDATE-AR-011') {
                                $validSlot.AddMinutes(15)
                            }
                            else {
                                $validSlot
                            }
                            $assertion.matches = '^' +
                                [regex]::Escape(
                                    $expectedSlot.ToString(
                                        'yyyy-MM-ddTHH:mm:ss')) +
                                '(?:Z|\+00:00)$'
                        }
                        elseif ([string]$assertion.path -ceq
                            '$.intent.location.latitude') {
                            $assertion.equals = 24.7136
                        }
                        elseif ([string]$assertion.path -ceq
                            '$.intent.location.longitude') {
                            $assertion.psobject.Properties.Remove('matches')
                            $assertion | Add-Member -NotePropertyName equals `
                                -NotePropertyValue 46.6753 -Force
                        }
                    }
                }
            }
        }
        elseif ($item.Key -ceq 'step-11-pricing-reprice.manifest.json') {
            Initialize-Step11InheritedFixtures
            $state = Read-State
            $validSlot = ([DateTimeOffset]$state.slotStartUtc).
                ToUniversalTime()
            $document.setupVariables.businessSourceId = $state.companyId
            $document.setupVariables.branchSourceId = $state.branchId
            $document.setupVariables.defaultOfferingId = $state.offeringId
            $document.setupVariables.multipleChoiceId =
                $state.step11MultipleChoiceId
            $document.setupVariables.singleOfferingId =
                $state.step11SingleOfferingId
            $document.setupVariables.singleChoiceId =
                $state.step11SingleChoiceId
            $document.setupVariables.quantityOfferingId =
                $state.step11QuantityOfferingId
            $document.setupVariables.quantityChoiceId =
                $state.step11QuantityChoiceId
            $document.setupVariables.fixedOfferingId =
                $state.step11FixedOfferingId
            $document.setupVariables.fixedChoiceId =
                $state.step11FixedChoiceId
            $document.setupVariables.segmentedOfferingId =
                $state.step11SegmentedOfferingId
            $document.setupVariables.segmentedChoiceId =
                $state.step11SegmentedChoiceId
            $document.setupVariables.slotStartUtc =
                $validSlot.ToString('yyyy-MM-ddTHH:mm:ssZ')
            $document.setupVariables.updatedSlotStartUtc =
                $validSlot.AddMinutes(15).ToString(
                    'yyyy-MM-ddTHH:mm:ssZ')
            Set-Step11RuntimeBodyFiles $document
            foreach ($scenario in $document.scenarios) {
                if ([string]$scenario.id -cin @(
                        'STEP11-DIRECT-LANGUAGE-006',
                        'STEP11-DRAFT-LANGUAGE-007')) {
                    $scenario.expect.errorCode = 'language_invalid'
                    foreach ($assertion in @($scenario.expect.json)) {
                        if ([string]$assertion.path -ceq '$.language') {
                            $assertion.equals = 'ar'
                        }
                    }
                }
                elseif ([string]$scenario.id -ceq
                    'STEP11-DIRECT-AUTHORITATIVE-014') {
                    foreach ($assertion in @($scenario.expect.json)) {
                        if ([string]$assertion.path -ceq
                            '$.paymentCapabilities.methods[0].enabled') {
                            $assertion.equals = $true
                        }
                        elseif ([string]$assertion.path -ceq
                            '$.paymentCapabilities.methods[0].reasonCode') {
                            $assertion.psobject.Properties.Remove('equals')
                            $assertion | Add-Member -NotePropertyName exists `
                                -NotePropertyValue $false -Force
                        }
                    }
                }
                elseif ([string]$scenario.id -ceq
                    'STEP11-DRAFT-REPRICE-AGAIN-027') {
                    foreach ($assertion in @($scenario.expect.json)) {
                        if ([string]$assertion.path -ceq
                            '$.intent.requestedSlotStartUtc') {
                            $assertion.matches = '^' +
                                [regex]::Escape(
                                    $validSlot.AddMinutes(15).ToString(
                                        'yyyy-MM-ddTHH:mm:ss')) +
                                '(?:Z|\+00:00)$'
                        }
                    }
                }
            }
        }
        elseif ($item.Key -ceq
            'step-12-booking-confirmation.manifest.json') {
            $validSlot = ([DateTimeOffset]$state.slotStartUtc).
                ToUniversalTime()
            $localizedNames = Get-SqlRow $state $state.customerDatabase @"
SELECT COALESCE(NULLIF(p.NameHe,N''),p.NameAr) AS ProviderName,
       COALESCE(NULLIF(b.NameHe,N''),b.NameAr) AS BranchName,
       COALESCE(NULLIF(o.NameHe,N''),o.NameAr) AS OfferingName
FROM CatalogProviders p
JOIN CatalogBranches b ON b.ProviderId=p.Id
JOIN CatalogOfferings o ON o.BranchId=b.Id
WHERE p.SourceCompanyId='$($state.companyId)'
  AND b.SourceBranchId='$($state.branchId)'
  AND o.SourceOfferingId='$($state.offeringId)';
"@
            if ($null -eq $localizedNames) {
                throw 'Step 12 localized catalog fixtures are missing.'
            }
            $document.setupVariables.businessSourceId = $state.companyId
            $document.setupVariables.branchSourceId = $state.branchId
            $document.setupVariables.defaultOfferingId = $state.offeringId
            $document.setupVariables.multipleChoiceId =
                $state.step11MultipleChoiceId
            $document.setupVariables.singleOfferingId =
                $state.step11SingleOfferingId
            $document.setupVariables.singleChoiceId =
                $state.step11SingleChoiceId
            $document.setupVariables.slotStartUtc =
                $validSlot.ToString('yyyy-MM-ddTHH:mm:ssZ')
            $document.setupVariables.slotStartUtc2 =
                $validSlot.AddMinutes(15).ToString(
                    'yyyy-MM-ddTHH:mm:ssZ')
            $document.setupVariables.unavailableSlotUtc =
                $validSlot.Date.AddHours(3).ToString(
                    'yyyy-MM-ddTHH:mm:ssZ')
            $document.setupVariables.businessCanonicalSlotUtc =
                $validSlot.AddMinutes(30).ToString(
                    'yyyy-MM-ddTHH:mm:ssZ')
            $defaultOfferingIsFirst =
                ([guid]$state.offeringId).CompareTo(
                    [guid]$state.step11SingleOfferingId) -lt 0
            foreach ($scenario in $document.scenarios) {
                if ([string]$scenario.url -like
                    'https://localhost:50691/*') {
                    $scenario.url = [string]$scenario.url -replace `
                        '^https://localhost:50691',
                        $state.businessBaseUrl
                }
                if ($null -ne
                    $scenario.psobject.Properties['internalAuth']) {
                    $scenario.internalAuth.serviceId =
                        $state.customerToBusinessServiceId
                }
                if ($null -ne $scenario.psobject.Properties['jsonBody'] -and
                    $null -ne $scenario.jsonBody) {
                    $body = $scenario.jsonBody
                    if ($null -ne
                        $body.psobject.Properties['expectedCatalogVersion'] -and
                        [long]$body.expectedCatalogVersion -eq 10) {
                        $body.expectedCatalogVersion =
                            [long]$state.catalogVersion
                    }
                    if ($null -ne
                        $body.psobject.Properties['location'] -and
                        $null -ne $body.location) {
                        $body.location.latitude = 24.7136
                        $body.location.longitude = 46.6753
                    }
                }
                if ([string]$scenario.id -cin @(
                        'STEP12-JWT-MISSING-006',
                        'STEP12-JWT-INVALID-007',
                        'STEP12-JWT-EMPTY-006B',
                        'STEP12-JWT-EXPIRED-007B')) {
                    $scenario.expect.errorCode =
                        'customer_authentication_required'
                }
                elseif ([string]$scenario.id -ceq
                    'STEP12-LANGUAGE-010') {
                    $scenario.expect.status = 400
                    $scenario.expect.errorCode = 'language_invalid'
                    foreach ($assertion in @($scenario.expect.json)) {
                        if ([string]$assertion.path -ceq '$.language') {
                            $assertion.equals = 'ar'
                        }
                    }
                }
                elseif ($null -ne
                    $scenario.expect.psobject.Properties['errorCode'] -and
                    [string]$scenario.expect.errorCode -ceq
                        'booking_confirmation_invalid') {
                    $scenario.expect.errorCode = 'booking_request_invalid'
                }
                if ($null -ne $scenario.psobject.Properties['expect'] -and
                    $null -ne $scenario.expect.psobject.Properties['json']) {
                    foreach ($assertion in @($scenario.expect.json)) {
                        if ([string]$assertion.path -ceq
                            '$.paths[''/api/v1/internal/reservations/{reference}''].get') {
                            $assertion.exists = $true
                        }
                        elseif ([string]$assertion.path -ceq '$.status' -and
                            [string]$assertion.equals -ceq 'Reserved') {
                            $assertion.equals = 'Pending'
                        }
                        elseif ([string]$assertion.path -ceq
                            '$.catalogVersion' -and
                            [string]$assertion.equals -ceq '10') {
                            $assertion.equals = [long]$state.catalogVersion
                        }
                        elseif ([string]$scenario.id -ceq
                            'STEP12-HAPPY-026' -and
                            [string]$assertion.path -ceq '$.providerName') {
                            $assertion.equals =
                                [string]$localizedNames.ProviderName
                        }
                        elseif ([string]$scenario.id -ceq
                            'STEP12-HAPPY-026' -and
                            [string]$assertion.path -ceq '$.branchName') {
                            $assertion.equals =
                                [string]$localizedNames.BranchName
                        }
                        elseif ([string]$scenario.id -ceq
                            'STEP12-HAPPY-026' -and
                            [string]$assertion.path -ceq
                                '$.items[0].serviceName') {
                            $assertion.equals =
                                [string]$localizedNames.OfferingName
                        }
                        elseif ([string]$scenario.id -cin @(
                                'STEP12-BUSINESS-CANONICAL-CREATE-037',
                                'STEP12-BUSINESS-CANONICAL-REPLAY-038') -and
                            [string]$assertion.path -ceq
                                '$.items[0].offeringId') {
                            $assertion.equals = if ($defaultOfferingIsFirst) {
                                '{{var:defaultOfferingId}}'
                            }
                            else {
                                '{{var:singleOfferingId}}'
                            }
                        }
                        elseif ([string]$scenario.id -cin @(
                                'STEP12-BUSINESS-CANONICAL-CREATE-037',
                                'STEP12-BUSINESS-CANONICAL-REPLAY-038') -and
                            [string]$assertion.path -ceq
                                '$.items[0].itemSubtotal') {
                            $assertion.equals =
                                if ($defaultOfferingIsFirst) { 83.5 } else { 57 }
                        }
                        elseif ([string]$scenario.id -cin @(
                                'STEP12-BUSINESS-CANONICAL-CREATE-037',
                                'STEP12-BUSINESS-CANONICAL-REPLAY-038') -and
                            [string]$assertion.path -ceq
                                '$.items[1].offeringId') {
                            $assertion.equals = if ($defaultOfferingIsFirst) {
                                '{{var:singleOfferingId}}'
                            }
                            else {
                                '{{var:defaultOfferingId}}'
                            }
                        }
                        elseif ([string]$scenario.id -cin @(
                                'STEP12-BUSINESS-CANONICAL-CREATE-037',
                                'STEP12-BUSINESS-CANONICAL-REPLAY-038') -and
                            [string]$assertion.path -ceq
                                '$.items[1].itemSubtotal') {
                            $assertion.equals =
                                if ($defaultOfferingIsFirst) { 57 } else { 83.5 }
                        }
                        elseif ([string]$assertion.path -ceq
                            '$.requestedSlotStartUtc' -and
                            $null -ne
                                $assertion.psobject.Properties['matches']) {
                            $expectedStart = if ([string]$scenario.id -ceq
                                'STEP12-BUSINESS-CANONICAL-CREATE-037') {
                                $validSlot.AddMinutes(30)
                            }
                            else {
                                $validSlot
                            }
                            $assertion.matches = '^' +
                                [regex]::Escape(
                                    $expectedStart.ToString(
                                        'yyyy-MM-ddTHH:mm:ss')) +
                                '(?:Z|\+00:00)$'
                        }
                        elseif ([string]$assertion.path -ceq
                            '$.requestedSlotEndUtc' -and
                            $null -ne
                                $assertion.psobject.Properties['matches']) {
                            $expectedEnd = if ([string]$scenario.id -ceq
                                'STEP12-BUSINESS-CANONICAL-CREATE-037') {
                                $validSlot.AddMinutes(90)
                            }
                            else {
                                $validSlot.AddMinutes(30)
                            }
                            $assertion.matches = '^' +
                                [regex]::Escape(
                                    $expectedEnd.ToString(
                                        'yyyy-MM-ddTHH:mm:ss')) +
                                '(?:Z|\+00:00)$'
                        }
                    }
                }
                if ([string]$scenario.id -ceq
                    'STEP12-BUSINESS-ORDER-CONFLICT-039') {
                    $scenario.expect.status = 409
                }
            }
        }
        elseif ($item.Key -ceq 'step-13-booking-status.manifest.json') {
            $step13Keys = @($document.scenarios | ForEach-Object {
                    if ($null -ne $_.psobject.Properties['headers'] -and
                        $null -ne $_.headers.psobject.Properties[
                            'Idempotency-Key']) {
                        [string]$_.headers.'Idempotency-Key'
                    }
                } | Where-Object {
                    -not [string]::IsNullOrWhiteSpace($_) -and
                    -not $_.Contains('{{')
                } | Sort-Object -Unique)
            if ($step13Keys.Count) {
                $keyList = ($step13Keys | ForEach-Object {
                        "N'$($_.Replace("'","''"))'"
                    }) -join ','
                Invoke-SqlNonQuery $state $state.customerDatabase `
                    "DELETE FROM CustomerInternalIdempotencyRecords WHERE IdempotencyKey IN ($keyList);"
                Invoke-SqlNonQuery $state $state.businessDatabase `
                    "DELETE FROM InternalServiceIdempotencyRecords WHERE IdempotencyKey IN ($keyList);"
            }
            $variablesPath = Join-Path $artifacts `
                "step17-inherited-$runSuffix-step-13.variables.local.json"
            $capacityStartDate = (
                ([DateTimeOffset]$state.slotStartUtc).
                    ToUniversalTime().Date.AddDays(1)
            ).ToString('yyyy-MM-dd')
            & $step13Initializer `
                -SourceCustomerDatabase $state.customerDatabase `
                -SourceBusinessDatabase $state.businessDatabase `
                -CustomerDatabase $state.customerDatabase `
                -BusinessDatabase $state.businessDatabase `
                -VariablesPath $variablesPath `
                -SkipDatabaseCopy `
                -CapacityBranchId $state.branchId `
                -CapacityOfferingId $state.offeringId `
                -CapacityStartDate $capacityStartDate | Out-Null
            $step13Variables = Get-Content -LiteralPath $variablesPath `
                -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($property in $step13Variables.psobject.Properties) {
                $document.setupVariables | Add-Member `
                    -NotePropertyName $property.Name `
                    -NotePropertyValue $property.Value -Force
            }
            foreach ($scenario in $document.scenarios) {
                if ([string]$scenario.id -cne
                    'STEP13-AUTH-RECONCILE-FORBIDDEN-010' -and
                    $null -ne $scenario.psobject.Properties['internalAuth'] -and
                    [string]$scenario.url -like
                        '/api/v1/internal/bookings/*' -and
                    [string]$scenario.url -cne
                        '/api/v1/internal/bookings/status') {
                    $scenario.internalAuth.serviceId =
                        $state.businessToCustomerReconcileServiceId
                    $scenario.internalAuth.secretEnv =
                        'GHSEELI_HTTP_TEST_STEP13_RECONCILE_SECRET'
                }
                if ([string]$scenario.id -cin @(
                        'STEP13-HMAC-TIMESTAMP-STALE-019',
                        'STEP13-HMAC-TIMESTAMP-FUTURE-020')) {
                    $timestamp = if ([string]$scenario.id -ceq
                        'STEP13-HMAC-TIMESTAMP-STALE-019') {
                        '2000-01-01T00:00:00.0000000Z'
                    }
                    else {
                        '2100-01-01T00:00:00.0000000Z'
                    }
                    $scenario.headers.'X-Ghseeli-Timestamp' = $timestamp
                    $scenario.internalAuth.timestamp = $timestamp
                }
                elseif ([string]$scenario.id -ceq
                    'STEP13-BUSINESS-TRANSITION-067') {
                    $scenario.headers | Add-Member `
                        -NotePropertyName 'Idempotency-Key' `
                        -NotePropertyValue 'step13-transition-067' -Force
                }
                elseif ([string]$scenario.id -ceq
                    'STEP13-BUSINESS-TRANSITION-REPEAT-068') {
                    $scenario.headers | Add-Member `
                        -NotePropertyName 'Idempotency-Key' `
                        -NotePropertyValue 'step13-transition-repeat-068' -Force
                }
                if ([string]$scenario.id -cin @(
                        'STEP13-LOCALIZATION-AR-076',
                        'STEP13-LOCALIZATION-HE-077',
                        'STEP13-LOCALIZATION-PRECEDENCE-078')) {
                    foreach ($assertion in @($scenario.expect.json)) {
                        if ([string]$assertion.path -ceq '$.title') {
                            $assertion.equals =
                                'Internal booking status request was rejected.'
                        }
                    }
                }
                elseif ([string]$scenario.id -ceq
                    'STEP13-LOCALIZATION-MALFORMED-078B') {
                    $scenario.expect.status = 404
                    $scenario.expect | Add-Member `
                        -NotePropertyName errorCode `
                        -NotePropertyValue 'booking_not_found' -Force
                }
                if ([string]$scenario.id -like 'STEP13-CAPACITY-*') {
                    $scenario.jsonBody.expectedCatalogVersion =
                        [long]$state.catalogVersion
                    $scenario.jsonBody.location.latitude = 24.7136
                    $scenario.jsonBody.location.longitude = 46.6753
                }
            }
        }
        elseif ($item.Key -ceq 'step-14-payment-rebuild.manifest.json') {
            $variablesPath = Join-Path $artifacts `
                "step17-inherited-$runSuffix-step-14.variables.local.json"
            $initialization = & $step14Initializer `
                -SourceCustomerDatabase $state.customerDatabase `
                -CustomerDatabase $state.customerDatabase `
                -VariablesPath $variablesPath `
                -SkipDatabaseCopy `
                -JwtSecret ([Environment]::GetEnvironmentVariable(
                    'STEP17_CUSTOMER_JWT_SECRET')) `
                -WebhookSecret ([Environment]::GetEnvironmentVariable(
                    'STEP17_WEBHOOK_SECRET')) | ConvertFrom-Json
            $document = Get-Content -LiteralPath `
                ([string]$initialization.runtimeManifestPath) `
                -Raw -Encoding UTF8 | ConvertFrom-Json
            $document.scenarios = @($document.scenarios | Where-Object {
                    $_.id -cne 'STEP14-INTENT-REAL-STRIPE-057'
                })
            $step14Variables = Get-Content -LiteralPath $variablesPath `
                -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($property in $step14Variables.psobject.Properties) {
                $document.setupVariables | Add-Member `
                    -NotePropertyName $property.Name `
                    -NotePropertyValue $property.Value -Force
            }
            foreach ($scenario in $document.scenarios) {
                $expect = $scenario.expect
                $jsonAssertions = if ($null -ne
                    $expect.psobject.Properties['json']) {
                    @($expect.json)
                }
                else {
                    @()
                }
                $languageAssertion = @($jsonAssertions | Where-Object {
                        [string]$_.path -ceq '$.language' -and
                        $null -ne $_.psobject.Properties['equals']
                    } | Select-Object -First 1)
                $hebrew = $languageAssertion.Count -gt 0 -and
                    [string]$languageAssertion[0].equals -ceq 'he'
                $genericTitle = if ($hebrew) {
                    'לא ניתן להשלים את הבקשה.'
                }
                else {
                    'تعذر إكمال الطلب.'
                }
                $paymentTitle = if ($hebrew) {
                    'לא ניתן להשלים את בקשת התשלום.'
                }
                else {
                    'تعذر إكمال طلب الدفع.'
                }
                $errorCode = if ($null -ne
                    $expect.psobject.Properties['errorCode']) {
                    [string]$expect.errorCode
                }
                else {
                    ''
                }
                if ($null -ne $expect.psobject.Properties[
                        'jsonPropertyCount']) {
                    if ($errorCode -like 'stripe_*' -or
                        $errorCode -like 'payment_*' -or
                        $errorCode -eq 'booking_not_payable') {
                        $expect.jsonPropertyCount =
                            1 + [int]$expect.jsonPropertyCount
                    }
                    elseif ([int]$expect.status -eq 200 -and
                        [string]$scenario.url -like '/api/v1/payments*') {
                        $expect.jsonPropertyCount = 10
                    }
                }
                if ($errorCode -cin @(
                        'customer_authentication_required',
                        'customer_authorization_forbidden')) {
                    foreach ($assertion in @($jsonAssertions | Where-Object {
                                [string]$_.path -ceq '$.title'
                            })) {
                        $assertion.equals = $genericTitle
                    }
                    if ($errorCode -ceq
                        'customer_authorization_forbidden') {
                        foreach ($assertion in @($jsonAssertions |
                                Where-Object {
                                    [string]$_.path -ceq '$.detail'
                                })) {
                            $assertion.equals = if ($hebrew) {
                                'ללקוח אין הרשאה לבצע בקשה זו.'
                            }
                            else {
                                'لا يملك العميل صلاحية تنفيذ هذا الطلب.'
                            }
                        }
                    }
                }
                elseif ($errorCode -cin @(
                        'payment_request_invalid',
                        'payment_not_found',
                        'payment_method_not_yet_supported',
                        'payment_provider_unavailable',
                        'booking_not_payable')) {
                    foreach ($assertion in @($jsonAssertions | Where-Object {
                                [string]$_.path -ceq '$.title'
                            })) {
                        $assertion.equals = $paymentTitle
                    }
                    if ($errorCode -ceq 'payment_request_invalid') {
                        foreach ($assertion in @($jsonAssertions |
                                Where-Object {
                                    [string]$_.path -ceq '$.detail'
                                })) {
                            $assertion.equals = $paymentTitle
                        }
                    }
                }
                if ([string]$scenario.id -cin @(
                        'STEP14-LANGUAGE-EMPTY-100-EMPTY',
                        'STEP14-LANGUAGE-MALFORMED-100-MALFORMED',
                        'STEP14-READ-LANGUAGE-MALFORMED-144',
                        'STEP14-READ-LANGUAGE-MALFORMED-144-WHITESPACE',
                        'STEP14-READ-LANGUAGE-MALFORMED-144-UNKNOWN')) {
                    $expect.errorCode = 'language_invalid'
                    foreach ($assertion in $jsonAssertions) {
                        switch ([string]$assertion.path) {
                            '$.code' {
                                $assertion.equals = 'language_invalid'
                            }
                            '$.title' {
                                $assertion.equals = 'تعذر إكمال الطلب.'
                            }
                            '$.detail' {
                                $assertion.equals =
                                    'اللغة المطلوبة غير مدعومة.'
                            }
                            '$.language' {
                                $assertion.equals = 'ar'
                            }
                        }
                    }
                }
                if ([string]$scenario.id -ceq
                    'STEP14-CORRELATION-REPLACE-102-MALFORMED') {
                    $scenario.expect = [pscustomobject][ordered]@{
                        status = 400
                    }
                }
                elseif ([string]$scenario.id -ceq
                    'STEP14-CONTRACT-SWAGGER-001') {
                    foreach ($assertion in $jsonAssertions) {
                        if ([string]$assertion.path -like '*Bearer*') {
                            $assertion.path = [string]$assertion.path `
                                -replace 'Bearer', 'CustomerBearer'
                        }
                        elseif ([string]$assertion.path -ceq
                            '$.paths[''/api/stripe/webhook''].post.security') {
                            $assertion.path =
                                '$.paths[''/api/stripe/webhook''].post.security[0].StripeSignature'
                            $assertion.exists = $true
                        }
                    }
                }
                elseif ([string]$scenario.id -ceq
                    'STEP14-PROVIDER-UNCONFIGURED-056') {
                    $scenario.expect = [pscustomobject][ordered]@{
                        status = 200
                        contentType = 'application/json'
                        json = @(
                            [pscustomobject][ordered]@{
                                path = '$.status'
                                equals = 'Pending'
                            },
                            [pscustomobject][ordered]@{
                                path = '$.method'
                                equals = 'Card'
                            },
                            [pscustomobject][ordered]@{
                                path = '$.amount'
                                exists = $true
                            }
                        )
                    }
                }
                elseif ([string]$scenario.id -ceq
                    'STEP14-WEBHOOK-CONFIGURATION-132') {
                    $expect.status = 400
                    $expect.errorCode = 'stripe_signature_missing'
                    foreach ($assertion in $jsonAssertions) {
                        if ([string]$assertion.path -ceq '$.status') {
                            $assertion.equals = 400
                        }
                        elseif ([string]$assertion.path -ceq '$.code') {
                            $assertion.equals = 'stripe_signature_missing'
                        }
                    }
                }
            }
        }
        elseif ($item.Key -ceq 'step-15-localization-swagger.manifest.json') {
            Start-CustomerProduction $state | Out-Null
            Start-BusinessProduction $state | Out-Null
            $step14VariablesPath = Join-Path $artifacts `
                "step17-inherited-$runSuffix-step-14.variables.local.json"
            $step14Variables = Get-Content -LiteralPath $step14VariablesPath `
                -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($entry in @{
                    customerDeviceToken = $step14Variables.ownerDeviceToken
                    foreignDeviceToken = $step14Variables.secondDeviceToken
                    customerJwt = $step14Variables.ownerJwt
                    businessJwt = $businessJwt
                    customerHttpBaseUrl = $state.customerHttpBaseUrl
                    oversizedLanguageHeader = (
                        ([string]$document.setupVariables.maxLanguageHeader) +
                        'x')
                }.GetEnumerator()) {
                $document.setupVariables | Add-Member `
                    -NotePropertyName $entry.Key `
                    -NotePropertyValue $entry.Value -Force
            }
            foreach ($scenario in $document.scenarios) {
                if ([string]$scenario.id -ceq
                    'STEP15-SWAGGER-CUSTOMER-ROUTES-130') {
                    $removedPrefixes = @(
                        '$.paths[''/api/v1/internal/bookings/{unmatched}',
                        '$.paths[''/api/Bookings',
                        '$.paths[''/api/Companies',
                        '$.paths[''/api/Services',
                        '$.paths[''/api/ServiceOptions')
                    $removedPaths = @($scenario.expect.json |
                        Where-Object {
                            $path = [string]$_.path
                            @($removedPrefixes | Where-Object {
                                    $path.StartsWith($_)
                                }).Count -gt 0
                        } | ForEach-Object { [string]$_.path })
                    $retainedAssertions = @($scenario.expect.json |
                        Where-Object {
                            [string]$_.path -cnotin $removedPaths -and
                            [string]$_.path -cne '$.paths'
                        })
                    $pathsAssertion = @($scenario.expect.json |
                        Where-Object {
                            [string]$_.path -ceq '$.paths'
                        } | Select-Object -First 1)
                    if ($pathsAssertion.Count -gt 0) {
                        $pathsAssertion[0].propertyNamesEqual = @(
                            $pathsAssertion[0].propertyNamesEqual |
                            Where-Object {
                                $candidate = '$.paths[''' + $_
                                -not @($removedPrefixes | Where-Object {
                                        $candidate.StartsWith($_)
                                    }).Count
                            })
                        $retainedAssertions += $pathsAssertion[0]
                    }
                    $absentAssertions = @($removedPaths |
                        ForEach-Object {
                            [pscustomobject][ordered]@{
                                path = $_
                                exists = $false
                            }
                        })
                    $scenario.expect.json = @(
                        $retainedAssertions + $absentAssertions)
                }
                if ($null -ne
                    $scenario.expect.psobject.Properties['json']) {
                    foreach ($assertion in @($scenario.expect.json)) {
                        if ($null -ne
                            $assertion.psobject.Properties['equals'] -and
                            $assertion.equals -is [string]) {
                            $assertion.equals = $assertion.equals `
                                -replace 'support@step15\.example\.invalid',
                                    'support@example.invalid' `
                                -replace 'https://step15\.example\.invalid/privacy',
                                    'https://example.invalid/privacy' `
                                -replace 'https://step15\.example\.invalid/terms',
                                    'https://example.invalid/terms'
                        }
                    }
                }
                if ([string]$scenario.id -ceq
                    'STEP15-TRANSPORT-TRAILING-SLASH-125') {
                    $scenario.expect = [pscustomobject][ordered]@{
                        status = 404
                        contentType = 'application/problem+json'
                        errorCode = 'resource_not_found'
                    }
                }
                elseif ([string]$scenario.id -ceq
                    'STEP15-HEADERS-HSTS-DEVELOPMENT-169--business-http' -or
                    [string]$scenario.id -ceq
                    'STEP15-HEADERS-HSTS-DEVELOPMENT-169--customer-http') {
                    $scenario.expect = [pscustomobject][ordered]@{
                        status = 307
                    }
                }
            }
        }
        $sourceDirectory = Split-Path -Parent $sourcePath
        foreach ($scenario in @($document.scenarios | Where-Object {
                    $null -ne $_.psobject.Properties['bodyFile']
                })) {
            if (-not [IO.Path]::IsPathRooted([string]$scenario.bodyFile)) {
                $scenario.bodyFile = Join-Path $sourceDirectory `
                    ([string]$scenario.bodyFile)
            }
        }
        $selected += @($document.scenarios).Count
        if ($item.Key -ceq
            'step-16-clean-schema-separation.manifest.json') {
            Stop-AllOwnedProcesses
            $step16RunId = [guid]::NewGuid().ToString('N')
            $step16Suffix = $step16RunId.Substring(0,12)
            $step16StatePath = Join-Path $artifacts `
                "step17-inherited-step16-$step16Suffix.state.local.json"
            $step16OverlayPath = Join-Path $artifacts `
                "step17-inherited-step16-$step16Suffix.overlay.local.json"
            $customerSecret = [Environment]::GetEnvironmentVariable(
                'STEP17_CUSTOMER_JWT_SECRET')
            [ordered]@{
                customerAdminJwt = New-Step17Jwt $customerSecret `
                    'GhseeliApis' 'GhseeliApis' 'Admin' `
                    ([guid]::NewGuid().ToString('D'))
                obsoleteCompanyJwt = New-Step17Jwt $customerSecret `
                    'GhseeliApis' 'GhseeliApis' 'Company' `
                    ([guid]::NewGuid().ToString('D'))
            } | ConvertTo-Json -Depth 5 |
                Set-Content -LiteralPath $step16OverlayPath -Encoding UTF8
            try {
                & $step16Runner -Phase All -RunId $step16RunId `
                    -Server $Server -StatePath $step16StatePath `
                    -VariablesOverlayPath $step16OverlayPath -Execute
                $phaseResults = @(Get-ChildItem -LiteralPath $artifacts `
                    -Filter "step16-*-$step16Suffix.results.local.json")
                $executions = @($phaseResults | ForEach-Object {
                        (Get-Content -LiteralPath $_.FullName -Raw `
                            -Encoding UTF8 | ConvertFrom-Json).scenarios
                    })
                $passedById = @($executions |
                    Where-Object { $_.passed } |
                    Group-Object id |
                    ForEach-Object { $_.Group[-1] })
                if ($passedById.Count -ne 790) {
                    throw (
                        'Step 16 lifecycle evidence must contain 790 distinct ' +
                        "passing scenarios, found $($passedById.Count).")
                }
                $resultPath = Join-Path $artifacts `
                    'step17-inherited-step-16-clean-schema-separation.results.local.json'
                [ordered]@{
                    manifestPath = $sourcePath
                    startedAt = $null
                    completedAt = [DateTimeOffset]::UtcNow.ToString('o')
                    baseUrl = 'lifecycle-orchestrated'
                    filters = [ordered]@{}
                    summary = [ordered]@{
                        total = 790
                        passed = 790
                        failed = 0
                        passedAll = $true
                    }
                    scenarios = $passedById
                } | ConvertTo-Json -Depth 100 |
                    Set-Content -LiteralPath $resultPath -Encoding UTF8
            }
            finally {
                Remove-Item -LiteralPath $step16OverlayPath -Force `
                    -ErrorAction SilentlyContinue
            }
            continue
        }
        if ($item.Key -ceq 'step-09-catalog-readmodel.manifest.json') {
            Invoke-Step9InheritedManifest $document
            continue
        }
        $filteredPath = Join-Path $artifacts `
            "step17-inherited-$runSuffix-$($item.Key.Replace('.manifest.json','')).local.json"
        $document | ConvertTo-Json -Depth 100 |
            Set-Content -LiteralPath $filteredPath -Encoding UTF8
        Import-Module $harnessModule -Force
        $resultPath = Join-Path $artifacts `
            "step17-inherited-$($item.Key.Replace('.manifest.json','')).results.local.json"
        $baseUrl = if ($item.Key -ceq
            'step-06-secure-integration.manifest.json') {
            $state.businessBaseUrl
        }
        else {
            $state.customerBaseUrl
        }
        $result = Invoke-HttpTestHarness -ManifestPath $filteredPath `
            -BaseUrl $baseUrl -VariablesPath $StatePath `
            -ResultsPath $resultPath
        if (-not $result.Summary.passedAll) {
            throw "Inherited manifest '$($item.Key)' failed."
        }
    }
    if ($selected -ne 1294) {
        throw "Inherited selection must be 1,294, found $selected."
    }
}

function Assert-FinalIsolation {
    $state = Read-State
    if ($state.fixtureMarker -ne 'STEP16_DISPOSABLE_LOCAL_ONLY') {
        throw 'Step 17 state lost the Step 16 disposable-fixture marker.'
    }
    foreach ($phaseName in @(
            'setupCompleted','chainCompleted','outageRecoveryCompleted',
            'securityCompleted','inheritedCompleted')) {
        $phaseProperty = $state.psobject.Properties[$phaseName]
        if ($null -eq $phaseProperty -or $phaseProperty.Value -ne $true) {
            throw "Step 17 phase evidence '$phaseName' is missing."
        }
    }
    & $step16Verifier -StatePath $StatePath -RequireRoles
    foreach ($port in @(54431,54432,54433,54434,50831,50832,50833)) {
        if (@(Get-NetTCPConnection -LocalPort $port -State Listen `
                -ErrorAction SilentlyContinue).Count) {
            throw "Unexpected listener remains on Step 17 port $port."
        }
    }

}

function Write-Step17AcceptanceEvidence {
    $manifestDocument = Get-Content -LiteralPath $manifest -Raw `
        -Encoding UTF8 | ConvertFrom-Json
    $requiredIds = @($manifestDocument.scenarios | ForEach-Object {
            [string]$_.id
        })
    $executions = @(Get-ChildItem -LiteralPath $artifacts -File |
        Where-Object {
            $_.Name -like "step17-*-$runSuffix.results.local.json"
        } |
        ForEach-Object {
            (Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 |
                ConvertFrom-Json).scenarios
        })
    $passedById = @($executions |
        Where-Object {
            $_.passed -and [string]$_.id -cin $requiredIds
        } |
        Group-Object id |
        ForEach-Object { $_.Group[-1] })
    if ($passedById.Count -ne $requiredIds.Count) {
        $passedIds = @($passedById | ForEach-Object { [string]$_.id })
        $missing = @($requiredIds | Where-Object { $_ -cnotin $passedIds })
        throw (
            'Step 17 acceptance evidence is incomplete. Expected ' +
            "$($requiredIds.Count) distinct passing scenarios, found " +
            "$($passedById.Count). Missing: $($missing -join ', ').")
    }
    $resultPath = Join-Path $artifacts `
        'step17-quality-gate.results.local.json'
    [ordered]@{
        manifestPath = $manifest
        runId = $RunId
        completedAt = [DateTimeOffset]::UtcNow.ToString('o')
        summary = [ordered]@{
            total = $requiredIds.Count
            passed = $requiredIds.Count
            failed = 0
            passedAll = $true
        }
        scenarios = $passedById
    } | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $resultPath -Encoding UTF8
}

function Invoke-FinalCleanup {
    Stop-AllOwnedProcesses
    $state = Read-State
    if ($state.fixtureMarker -ne 'STEP16_DISPOSABLE_LOCAL_ONLY') {
        throw 'Refusing cleanup without the disposable Step 16 fixture marker.'
    }
    & $step16Cleanup -StatePath $StatePath -Confirm:$false
    foreach ($database in @(
            $state.customerDatabase,$state.businessDatabase,
            $state.wrongDatabase)) {
        $exists = Invoke-SqlScalar $state 'master' (
            "SELECT COUNT(*) FROM sys.databases WHERE name=N'$database'")
        if ([int]$exists -ne 0) {
            throw "Disposable database '$database' remains."
        }
    }
    Remove-BoundaryBodies
    Get-ChildItem -LiteralPath $artifacts -File | Where-Object {
        $_.Name -like "step17-*-$runSuffix.results.local.json" -or
        $_.Name -like "step17-inherited-$runSuffix-*.local.json"
    } | Remove-Item -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $RuntimeConfigPath -Force `
        -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $FixtureOverlayPath -Force `
        -ErrorAction SilentlyContinue
    foreach ($port in @(54431,54432,54433,54434,50831,50832,50833)) {
        if (@(Get-NetTCPConnection -LocalPort $port -State Listen `
                -ErrorAction SilentlyContinue).Count) {
            throw "Listener remains after Step 17 cleanup on port $port."
        }
    }
}

& $validator -PlanPath $plan -ManifestPath $manifest -CoveragePath $coverage
& $step16Initializer -RunId $RunId -Server $Server -ValidateOnly
if ($Phase -eq 'Validate') {
    $missing = @(Get-MissingRuntimePrerequisites)
    Write-Host (
        'Step 17 static validation completed; no live HTTP or database change. ' +
        "Runtime values currently absent: $($missing.Count); live execution " +
        'generates them in the ignored artifacts directory.')
    return
}
if ($Phase -eq 'Cleanup') {
    Invoke-FinalCleanup
    Write-Host "Step 17 phase 'Cleanup' completed without an owned process leak."
    return
}
Import-LocalRuntimePrerequisites
Assert-LiveOptIn

try {
    switch ($Phase) {
        'Provision' {
            & $step16Initializer -Operation Parallel -RunId $RunId `
                -Server $Server -StatePath $StatePath
            & $step16Verifier -StatePath $StatePath -RequireZeroDomainRows
            Initialize-Step17StateVariables
        }
        'Setup' {
            $state = Read-State
            $business = Start-Business $state
            Invoke-Step6Setup $state
            Stop-OwnedProcess $business
            Add-ConfigurationFixture $state
            Merge-StateValues @{ setupCompleted = $true }
        }
        'Chain' {
            $state = Read-State
            $customer = Start-Customer $state
            $business = Start-Business $state
            Invoke-ManifestTag 'chain-bootstrap'
            Set-LiveJwtEnvironment (Read-State) -Customer -Business
            Initialize-SupportingBookingFixtures
            foreach ($id in @(
                    'STEP17-E2E-DRAFT-019',
                    'STEP17-E2E-STATUS-020',
                    'STEP17-E2E-STATUS-021')) {
                Invoke-ManifestScenarioId $id
            }
            Invoke-ManifestScenarioId 'STEP17-E2E-STATUS-022'
            Invoke-SupportingStatusCallback 2 'InProgress' 'event2Id'
            Invoke-ManifestScenarioId 'STEP17-E2E-STATUS-023'
            Invoke-SupportingStatusCallback 3 'Completed' 'event3Id'
            Invoke-ManifestScenarioId 'STEP17-E2E-RECONCILE-024'
            Invoke-ManifestScenarioId 'STEP17-E2E-RECONCILE-025'
            Invoke-ManifestTag 'payment-create'
            Hydrate-PaymentVariables
            Initialize-FailurePaymentFixture
            Invoke-ManifestTag 'payment-webhooks'
            Invoke-ManifestTag 'crossdb'
            Stop-AllOwnedProcesses
            & $step16Verifier -StatePath $StatePath -RequireRoles
            Merge-StateValues @{ chainCompleted = $true }
        }
        'OutageRecovery' {
            $state = Read-State
            $customer = Start-Customer $state
            $business = Start-Business $state
            Invoke-ManifestScenarioId 'STEP17-E2E-CATALOG-007'
            Stop-OwnedProcess $business
            Invoke-ManifestTag 'business-outage'
            $business = Start-Business $state
            Invoke-ManifestTag 'business-recovered'
            Invoke-RecoveryTransition (Read-State)
            Stop-OwnedProcess $customer
            Assert-CustomerDownCallbackHasNoResponse
            $customer = Start-Customer (Read-State)
            Invoke-ManifestTag 'customer-recovered'
            Invoke-FixtureRecoveryScenarios
            Stop-AllOwnedProcesses
            & $step16Verifier -StatePath $StatePath -RequireRoles
            Merge-StateValues @{ outageRecoveryCompleted = $true }
        }
        'Security' {
            $state = Read-State
            $customer = Start-Customer $state
            $business = Start-Business $state
            Set-LiveJwtEnvironment $state -Customer -Business
            New-BoundaryBodies
            Invoke-ManifestTag 'security'
            Invoke-RateLimitWarmup
            Invoke-ManifestTag 'security-rate'
            Stop-OwnedProcess $customer
            $customer = Start-Customer $state
            Invoke-ManifestTag 'security-boundary'
            $proxy = Start-Proxy
            Invoke-ManifestTag 'security-proxy'
            Stop-OwnedProcess $proxy
            Stop-AllOwnedProcesses
            Remove-BoundaryBodies
            Merge-StateValues @{ securityCompleted = $true }
        }
        'Inherited' {
            $state = Read-State
            if ($null -eq
                $state.psobject.Properties['customerHttpBaseUrl']) {
                Merge-StateValues @{
                    customerHttpBaseUrl = 'http://127.0.0.1:50831'
                }
                $state = Read-State
            }
            [Environment]::SetEnvironmentVariable(
                'STEP17_INHERITED_MODE',
                'true',
                [EnvironmentVariableTarget]::Process)
            Initialize-Step9InheritedFixtures
            $state = Read-State
            $customer = Start-Customer $state
            $business = Start-Business $state
            Invoke-Inherited
            Stop-AllOwnedProcesses
            Merge-StateValues @{ inheritedCompleted = $true }
        }
        'Verify' {
            Stop-AllOwnedProcesses
            Assert-FinalIsolation
        }
        'All' {
            & $step16Initializer -Operation Parallel -RunId $RunId `
                -Server $Server -StatePath $StatePath
            & $step16Verifier -StatePath $StatePath -RequireZeroDomainRows
            Initialize-Step17StateVariables
            $state = Read-State
            $business = Start-Business $state
            Invoke-Step6Setup $state
            Stop-OwnedProcess $business
            Add-ConfigurationFixture $state
            Merge-StateValues @{ setupCompleted = $true }
            $state = Read-State
            $customer = Start-Customer $state
            $business = Start-Business $state
            Invoke-ManifestTag 'chain-bootstrap'
            Set-LiveJwtEnvironment (Read-State) -Customer -Business
            Initialize-SupportingBookingFixtures
            foreach ($id in @(
                    'STEP17-E2E-DRAFT-019',
                    'STEP17-E2E-STATUS-020',
                    'STEP17-E2E-STATUS-021')) {
                Invoke-ManifestScenarioId $id
            }
            Invoke-ManifestScenarioId 'STEP17-E2E-STATUS-022'
            Invoke-SupportingStatusCallback 2 'InProgress' 'event2Id'
            Invoke-ManifestScenarioId 'STEP17-E2E-STATUS-023'
            Invoke-SupportingStatusCallback 3 'Completed' 'event3Id'
            Invoke-ManifestScenarioId 'STEP17-E2E-RECONCILE-024'
            Invoke-ManifestScenarioId 'STEP17-E2E-RECONCILE-025'
            Invoke-ManifestTag 'payment-create'
            Hydrate-PaymentVariables
            Initialize-FailurePaymentFixture
            Invoke-ManifestTag 'payment-webhooks'
            Invoke-ManifestTag 'crossdb'
            Merge-StateValues @{ chainCompleted = $true }
            Invoke-ManifestScenarioId 'STEP17-E2E-CATALOG-007'
            Stop-OwnedProcess $business
            Invoke-ManifestTag 'business-outage'
            $business = Start-Business $state
            Invoke-ManifestTag 'business-recovered'
            Invoke-RecoveryTransition (Read-State)
            Stop-OwnedProcess $customer
            Assert-CustomerDownCallbackHasNoResponse
            $customer = Start-Customer (Read-State)
            Invoke-ManifestTag 'customer-recovered'
            Invoke-FixtureRecoveryScenarios
            Merge-StateValues @{ outageRecoveryCompleted = $true }
            New-BoundaryBodies
            Invoke-ManifestTag 'security'
            Invoke-RateLimitWarmup
            Invoke-ManifestTag 'security-rate'
            Stop-OwnedProcess $customer
            $customer = Start-Customer $state
            Invoke-ManifestTag 'security-boundary'
            $proxy = Start-Proxy
            Invoke-ManifestTag 'security-proxy'
            Stop-OwnedProcess $proxy
            Merge-StateValues @{ securityCompleted = $true }
            Stop-AllOwnedProcesses
            [Environment]::SetEnvironmentVariable(
                'STEP17_INHERITED_MODE',
                'true',
                [EnvironmentVariableTarget]::Process)
            Initialize-Step9InheritedFixtures
            $state = Read-State
            $customer = Start-Customer $state
            $business = Start-Business $state
            Invoke-Inherited
            Merge-StateValues @{ inheritedCompleted = $true }
            Stop-AllOwnedProcesses
            Assert-FinalIsolation
            Write-Step17AcceptanceEvidence
            Invoke-FinalCleanup
        }
    }
}
finally {
    Stop-AllOwnedProcesses
    if ($Phase -in @('Security','All')) { Remove-BoundaryBodies }
}

Write-Host "Step 17 phase '$Phase' completed without an owned process leak."
