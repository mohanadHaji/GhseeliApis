#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$PlanPath,
    [string]$ManifestPath,
    [string]$CoveragePath,
    [string]$RunnerPath,
    [string]$ProxyPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$solution = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($PlanPath)) {
    $PlanPath = Join-Path $solution 'STEP_17_HTTP_TEST_PLAN.md'
}
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath =
        Join-Path $PSScriptRoot 'plans\step-17-quality-gate.manifest.json'
}
if ([string]::IsNullOrWhiteSpace($CoveragePath)) {
    $CoveragePath =
        Join-Path $PSScriptRoot 'plans\step-17-quality-gate.coverage.json'
}
if ([string]::IsNullOrWhiteSpace($RunnerPath)) {
    $RunnerPath = Join-Path $PSScriptRoot 'Invoke-Step17LiveLocal.ps1'
}
if ([string]::IsNullOrWhiteSpace($ProxyPath)) {
    $ProxyPath = Join-Path $PSScriptRoot 'Invoke-Step17LoopbackProxy.ps1'
}

foreach ($path in @(
        $PlanPath, $ManifestPath, $CoveragePath, $RunnerPath, $ProxyPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Step 17 asset is missing: '$path'."
    }
}

$planText = Get-Content -LiteralPath $PlanPath -Raw -Encoding UTF8
$planIds = @([regex]::Matches(
        $planText,
        '`(STEP17-(?:E2E|DET|SEC)-[A-Z0-9-]+)`') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique)

$groups = [ordered]@{
    E2E = @($planIds | Where-Object { $_ -clike 'STEP17-E2E-*' })
    DET = @($planIds | Where-Object { $_ -clike 'STEP17-DET-*' })
    SEC = @($planIds | Where-Object { $_ -clike 'STEP17-SEC-*' })
}
if ($groups.E2E.Count -ne 39 -or
    $groups.DET.Count -ne 48 -or
    $groups.SEC.Count -ne 31 -or
    $planIds.Count -ne 118) {
    throw "Step 17 plan IDs must be E2E=39, DET=48, SEC=31, total=118."
}

$coverage = Get-Content -LiteralPath $CoveragePath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$coverageEntries = @($coverage.scenarios)
$coverageIds = @($coverageEntries | ForEach-Object { [string]$_.id })
if ($coverageIds.Count -ne 118 -or
    @($coverageIds | Sort-Object -Unique).Count -ne 118) {
    throw 'Step 17 coverage map must contain exactly 118 unique scenario IDs.'
}
$missingCoverage = @($planIds | Where-Object { $_ -cnotin $coverageIds })
$unknownCoverage = @($coverageIds | Where-Object { $_ -cnotin $planIds })
if ($missingCoverage.Count -or $unknownCoverage.Count) {
    throw "Step 17 coverage map and plan IDs differ."
}

$allowedAutomation = @(
    'automated-live',
    'automated-testserver',
    'automated-relational',
    'automated-contract')
$allowedStatus = @('planned','red','passed')
foreach ($entry in $coverageEntries) {
    if ([string]::IsNullOrWhiteSpace([string]$entry.testReference) -or
        [string]$entry.automation -cnotin $allowedAutomation -or
        [string]$entry.status -cnotin $allowedStatus) {
        throw "Coverage entry '$($entry.id)' is incomplete."
    }
    if ([string]$entry.status -ceq 'passed' -and
        [string]::IsNullOrWhiteSpace([string]$entry.evidence)) {
        throw "Passed coverage entry '$($entry.id)' lacks evidence."
    }
}

function Get-DiscoveredTests([string]$ProjectPath) {
    $output = @(& dotnet test $ProjectPath --no-restore --list-tests `
        --nologo --verbosity quiet 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Test discovery failed for '$ProjectPath': $($output -join [Environment]::NewLine)"
    }

    return @($output |
        ForEach-Object { ([string]$_).Trim() } |
        Where-Object {
            $_ -clike 'GhseeliApis.Tests.*' -or
            $_ -clike 'Ghseeli.BusinessApi.Tests.*'
        })
}

$discoveredTests = @(
    Get-DiscoveredTests (
        Join-Path $solution 'GhseeliApis.Tests\GhseeliApis.Tests.csproj')
    Get-DiscoveredTests (
        Join-Path $solution `
            'Ghseeli.BusinessApi.Tests\Ghseeli.BusinessApi.Tests.csproj')
)
$customerStep17Count = @($discoveredTests | Where-Object {
        $_ -clike 'GhseeliApis.Tests.*' -and $_ -like '*Step17*'
    }).Count
$businessStep17Count = @($discoveredTests | Where-Object {
        $_ -clike 'Ghseeli.BusinessApi.Tests.*' -and $_ -like '*Step17*'
    }).Count
$hostSecurityCount = @($discoveredTests | Where-Object {
        $_ -clike 'GhseeliApis.Tests.Integration.Step17HostSecurityContractTests.*'
    }).Count
$businessRateCount = @($discoveredTests | Where-Object {
        $_ -clike 'Ghseeli.BusinessApi.Tests.Infrastructure.BusinessInternalRateLimitingIntegrationTests.*'
    }).Count
foreach ($entry in @($coverageEntries | Where-Object {
            [string]$_.status -ceq 'passed'
        })) {
    $reference = [string]$entry.testReference
    $matchingTests = @($discoveredTests | Where-Object {
            $_ -ceq $reference -or $_.StartsWith(
                "$reference(",
                [StringComparison]::Ordinal)
        })
    if ($matchingTests.Count -eq 0) {
        throw "Passed coverage entry '$($entry.id)' references an undiscoverable test."
    }

    $evidence = [string]$entry.evidence
    $isCurrentStep17Run = $evidence -match (
        '^dotnet test --filter FullyQualifiedName~Step17: Customer ' +
        "$customerStep17Count/$customerStep17Count and Business " +
        "$businessStep17Count/$businessStep17Count passed \(\d{4}-\d{2}-\d{2}\)$")
    $isCurrentHostRun = $evidence -match (
        '^dotnet test GhseeliApis\.Tests --filter FullyQualifiedName~' +
        'Step17HostSecurityContractTests: ' +
        "$hostSecurityCount/$hostSecurityCount passed \(\d{4}-\d{2}-\d{2}\)$")
    $isCurrentBusinessRateRun = $evidence -match (
        '^dotnet test Ghseeli\.BusinessApi\.Tests --filter FullyQualifiedName~' +
        'BusinessInternalRateLimitingIntegrationTests: ' +
        "$businessRateCount/$businessRateCount passed \(\d{4}-\d{2}-\d{2}\)$")
    if (-not ($isCurrentStep17Run -or
            ($isCurrentHostRun -and
                $reference -clike '*Step17HostSecurityContractTests.*') -or
            ($isCurrentBusinessRateRun -and
                $reference -clike '*BusinessInternalRateLimitingIntegrationTests.*'))) {
        throw "Passed coverage entry '$($entry.id)' lacks current structured run evidence."
    }
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$manifestEntries = @($manifest.scenarios)
$manifestIds = @($manifestEntries | ForEach-Object { [string]$_.id })
if ($manifestIds.Count -ne 58 -or
    @($manifestIds | Sort-Object -Unique).Count -ne 58) {
    throw 'Step 17 live manifest must contain exactly 58 unique scenarios.'
}
$expectedLiveIds = @($coverageEntries |
    Where-Object { [string]$_.automation -ceq 'automated-live' } |
    ForEach-Object { [string]$_.id })
if ($expectedLiveIds.Count -ne 58 -or
    @($expectedLiveIds | Where-Object { $_ -cnotin $manifestIds }).Count -or
    @($manifestIds | Where-Object { $_ -cnotin $expectedLiveIds }).Count) {
    throw 'Step 17 live manifest must exactly match automated-live coverage entries.'
}
$expectedE2eIds = @($groups.E2E)
$expectedSecIds = @($expectedLiveIds | Where-Object {
        $_ -clike 'STEP17-SEC-*'
    })
if ($expectedE2eIds.Count -ne 39 -or $expectedSecIds.Count -ne 19 -or
    @($manifestIds | Where-Object { $_ -clike 'STEP17-DET-*' }).Count) {
    throw 'Live selection must be exactly all 39 E2E IDs and the 19 declared live SEC IDs, with no DET IDs.'
}

$approvedOrigins = @(
    '{{var:customerBaseUrl}}',
    '{{var:businessBaseUrl}}',
    '{{var:customerHttpBaseUrl}}',
    '{{var:businessHttpBaseUrl}}',
    '{{var:businessProxyBaseUrl}}')
$supportedMethods = @('GET','HEAD','POST','PUT','PATCH','DELETE','OPTIONS')
$requiredScenarioProperties = @(
    'planScenarioId','feature','tags','phase','prerequisitesSetup',
    'dataVersionSideEffects','cleanup','method','url','headers','expect')
$requiredLeakageMarkers = @(
    'stack','SqlException','ConnectionString','Bearer ','X-Device-Token',
    'X-Ghseeli-Signature','Stripe-Signature','sk_live_','whsec_',
    'client_secret','@example.com')
foreach ($scenario in $manifestEntries) {
    foreach ($property in $requiredScenarioProperties) {
        if ($scenario.psobject.Properties.Name -cnotcontains $property) {
            throw "Live scenario '$($scenario.id)' lacks '$property'."
        }
    }
    if ([string]$scenario.planScenarioId -cne [string]$scenario.id -or
        [string]$scenario.feature -cne 'step17' -or
        @($scenario.tags) -cnotcontains 'step17' -or
        @($scenario.tags) -cnotcontains 'live-local' -or
        [string]$scenario.method -cnotin $supportedMethods -or
        [string]::IsNullOrWhiteSpace([string]$scenario.prerequisitesSetup) -or
        [string]::IsNullOrWhiteSpace([string]$scenario.dataVersionSideEffects) -or
        [string]::IsNullOrWhiteSpace([string]$scenario.cleanup)) {
        throw "Live scenario '$($scenario.id)' has incomplete executable lifecycle metadata."
    }
    if ([string]::IsNullOrWhiteSpace([string]$scenario.method) -or
        [string]::IsNullOrWhiteSpace([string]$scenario.url) -or
        $null -eq $scenario.expect.status) {
        throw "Live scenario '$($scenario.id)' lacks executable HTTP fields."
    }
    if (-not @($approvedOrigins | Where-Object {
                ([string]$scenario.url).StartsWith($_)
            }).Count) {
        throw "Live scenario '$($scenario.id)' uses an unapproved host."
    }
    if (@($scenario.expect.bodyNotContains).Count -eq 0) {
        throw "Live scenario '$($scenario.id)' lacks leakage assertions."
    }
    foreach ($marker in $requiredLeakageMarkers) {
        if ($marker -cnotin @($scenario.expect.bodyNotContains)) {
            throw "Live scenario '$($scenario.id)' lacks leakage marker '$marker'."
        }
    }
    foreach ($headerName in @(
            'X-Content-Type-Options','X-Frame-Options','Referrer-Policy',
            'Permissions-Policy','Content-Security-Policy')) {
        $header = $scenario.expect.headers.psobject.Properties[$headerName]
        if ($null -eq $header -or $header.Value.count -ne 1) {
            throw "Live scenario '$($scenario.id)' must assert one '$headerName' value."
        }
    }
    if ($scenario.url -match '/api/v1/internal/' -and
        $null -eq $scenario.psobject.Properties['internalAuth'] -and
        $scenario.method -cne 'OPTIONS') {
        throw "Internal live scenario '$($scenario.id)' lacks runtime HMAC signing."
    }
    if ($scenario.id -clike 'STEP17-E2E-WEBHOOK-*' -and
        $null -eq $scenario.psobject.Properties['stripeSignature']) {
        throw "Webhook live scenario '$($scenario.id)' lacks local signature generation."
    }
    if ($scenario.id -clike 'STEP17-SEC-BOUND-*' -and
        [string]$scenario.prerequisitesSetup -notmatch '(?i)byte|65,53') {
        throw "Boundary scenario '$($scenario.id)' does not state its exact byte setup."
    }
}

$sensitive = @($manifest.sensitiveFields | ForEach-Object {
        ([string]$_).ToLowerInvariant()
    })
foreach ($field in @(
        'authorization','token','password','secret','connectionstring',
        'x-device-token','x-ghseeli-signature','x-ghseeli-nonce',
        'idempotency-key','stripe-signature','email','phonenumber',
        'addressline','clientsecret','paymentintentid','chargeid')) {
    if ($field -notin $sensitive) {
        throw "Step 17 manifest does not redact sensitive field '$field'."
    }
}

$manifestText = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8
if ($manifestText -match
    '(?i)(sk_live_[A-Za-z0-9]{8,}|whsec_[A-Za-z0-9]{8,}|password\s*[=:]\s*["''][^<{"]|Data Source=.*prod)') {
    throw 'Step 17 manifest contains a committed credential or production marker.'
}

$bodyFiles = @($manifestEntries |
    Where-Object { $null -ne $_.psobject.Properties['bodyFile'] })
if ($bodyFiles.Count -ne 10) {
    throw "Step 17 must generate exactly ten request-boundary body files; found $($bodyFiles.Count)."
}
foreach ($scenario in $bodyFiles) {
    if ([string]$scenario.bodyFile -notmatch
        '^\.\.[\\/]artifacts[\\/][A-Za-z0-9.-]+\.local\.json$') {
        throw "Boundary body for '$($scenario.id)' must be a direct ignored artifact."
    }
}
$chunked = @($bodyFiles | Where-Object {
        $_.id -ceq 'STEP17-SEC-BOUND-025' -and $_.forceChunked -eq $true
    })
if ($chunked.Count -ne 1) {
    throw 'The signed callback maximum-plus-one scenario must use chunked framing.'
}
$boundaryDraft = @($bodyFiles | Where-Object {
        $_.id -ceq 'STEP17-SEC-BOUND-018' -and
        [string]$_.extract.boundaryOrderGuid -ceq '$.orderGuid'
    })
if ($boundaryDraft.Count -ne 1) {
    throw 'The accepted maximum-size draft must export its order GUID.'
}
$authenticatedBoundaries = @($bodyFiles | Where-Object {
        $_.id -cin @('STEP17-SEC-BOUND-022','STEP17-SEC-BOUND-023') -and
        [string]$_.headers.'X-Device-Token' -ceq '{{var:deviceToken}}'
    })
if ($authenticatedBoundaries.Count -ne 2) {
    throw 'Booking and payment maximum-plus-one scenarios must pass device authentication.'
}

$declaredVariables = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($property in $manifest.setupVariables.psobject.Properties) {
    [void]$declaredVariables.Add($property.Name)
}
foreach ($scenario in $manifestEntries) {
    if ($null -eq $scenario.psobject.Properties['extract']) { continue }
    foreach ($property in $scenario.extract.psobject.Properties) {
        [void]$declaredVariables.Add($property.Name)
    }
}
foreach ($name in @(
        'customerBaseUrl','businessBaseUrl','businessHttpBaseUrl',
        'businessProxyBaseUrl','fixtureRunId','runStamp','installationId',
        'customerEmail','slotStartUtc','baseUtc','successEventId',
        'failureEventId','refundEventId','invalidStripeEventId',
        'recoveryEventId','recoveryOccurredAtUtc','recoveryIdempotencyKey',
        'companyId','branchId',
        'categoryId','offeringId','addonChoiceId','categoryNameAr',
        'companyNameAr','configurationNameAr','catalogVersion','providerId',
        'categoryLocalId','offeringLocalId','payableBookingReference',
        'payableBookingInternalId','paymentIntentId','paymentMinorAmount',
        'paymentCurrency','paymentChargeId','failurePaymentId',
        'failurePaymentIntentId','failurePaymentMinorAmount',
        'failurePaymentCurrency','failureBookingInternalId',
        'failureBookingReference','recoveryBookingReference',
        'recoveryReservationId','recoveryWorkOrderId','boundaryOrderGuid',
        'customerToBusinessServiceId','businessToCustomerServiceId',
        'businessToCustomerReconcileServiceId')) {
    [void]$declaredVariables.Add($name)
}
$referencedVariables = @([regex]::Matches(
        $manifestText, '\{\{var:([A-Za-z0-9_.-]+)\}\}') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$unknownVariables = @($referencedVariables | Where-Object {
        -not $declaredVariables.Contains($_)
    })
if ($unknownVariables.Count) {
    throw "Unknown Step 17 manifest variables: $($unknownVariables -join ', ')."
}

$corsIds = @($manifestIds | Where-Object { $_ -clike 'STEP17-SEC-CORS-*' })
$rateIds = @($manifestIds | Where-Object { $_ -clike 'STEP17-SEC-RATE-*' })
$boundIds = @($manifestIds | Where-Object { $_ -clike 'STEP17-SEC-BOUND-*' })
$proxyIds = @($manifestIds | Where-Object { $_ -clike 'STEP17-SEC-PROXY-*' })
if ($corsIds.Count -ne 4 -or $rateIds.Count -ne 2 -or
    $boundIds.Count -ne 10 -or $proxyIds.Count -ne 3) {
    throw 'Live SEC selection must be CORS=4, RATE=2, BOUND=10, PROXY=3.'
}

$inherited = [ordered]@{
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
$inheritedTotal = 0
$inheritedIds = [Collections.Generic.List[string]]::new()
foreach ($item in $inherited.GetEnumerator()) {
    $path = Join-Path (Split-Path -Parent $ManifestPath) $item.Key
    $document = Get-Content -LiteralPath $path -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $actual = @($document.scenarios).Count
    if ($actual -ne $item.Value) {
        throw "Inherited manifest '$($item.Key)' changed from $($item.Value) to $actual entries."
    }
    foreach ($scenario in @($document.scenarios)) {
        $inheritedIds.Add([string]$scenario.id)
    }
    $inheritedTotal += $actual
}
if ($inheritedTotal -ne 1295) {
    throw "Inherited Step 17 registry must contain exactly 1,295 entries."
}
$realStripe = @($inheritedIds | Where-Object {
        $_ -ceq 'STEP14-INTENT-REAL-STRIPE-057'
    })
if ($realStripe.Count -ne 1 -or
    @($inheritedIds | Where-Object {
            $_ -cne 'STEP14-INTENT-REAL-STRIPE-057'
        }).Count -ne 1294) {
    throw 'Inherited selection must contain exactly one excluded real-Stripe ID and 1,294 local entries.'
}

$runnerText = Get-Content -LiteralPath $RunnerPath -Raw -Encoding UTF8
foreach ($required in @(
        "'Validate','Provision','Setup','Chain','OutageRecovery'",
        "'Security','Inherited','Verify','Cleanup','All'",
        'Initialize-Step16Fixtures.ps1',
        'Verify-Step16DatabaseInvariants.ps1',
        'Remove-Step16Fixtures.ps1',
        'Initialize-Step17LocalPrerequisites.ps1',
        'Import-LocalRuntimePrerequisites',
        'STEP17_ENABLE_DETERMINISTIC_FAKE_PAYMENT_GATEWAY',
        'STEP17_BUSINESS_TO_CUSTOMER_NEXT_HMAC_SECRET',
        'STEP17_INHERITED_MODE',
        "Step17TestFixtures__PaymentGateway = 'DeterministicFake'",
        "ASPNETCORE_ENVIRONMENT = 'Development'",
        "ASPNETCORE_ENVIRONMENT') -eq",
        "'Production'",
        "STEP14-INTENT-REAL-STRIPE-057",
        'if ($selected -ne 1294)',
        'Stop-Process -Id $Process.Id',
        'Get-NetTCPConnection',
        'New-BoundaryBodies',
        'New-Step17Jwt',
        'Start-Proxy',
        "Authorization='Bearer {{env:STEP17_BUSINESS_JWT}}'",
        'Invoke-Inherited')) {
    if (-not $runnerText.Contains($required)) {
        throw "Step 17 runner is missing required safety/orchestration marker '$required'."
    }
}
if ($runnerText -match '(?i)Stop-Process\s+-Name|taskkill\s+/IM' -or
    $runnerText -match '(?i)sk_live_[A-Za-z0-9]{8,}|whsec_[A-Za-z0-9]{8,}') {
    throw 'Step 17 runner contains unsafe process cleanup or a committed secret.'
}
foreach ($topologyMarker in @(
        "businessHttpBaseUrl = 'http://127.0.0.1:50832'",
        "businessProxyBaseUrl = 'http://127.0.0.1:50833'",
        "businessProxyBackendBaseUrl = 'http://[::1]:50832'",
        "ForwardedHeaders__KnownProxies__0 = '::1'")) {
    if (-not $runnerText.Contains($topologyMarker)) {
        throw "Step 17 runner lacks explicit proxy topology '$topologyMarker'."
    }
}
if ($runnerText.Contains(
        "ForwardedHeaders__KnownProxies__0 = '127.0.0.1'")) {
    throw 'Customer direct traffic must not be configured as a trusted proxy.'
}

$proxyText = Get-Content -LiteralPath $ProxyPath -Raw -Encoding UTF8
foreach ($topologyMarker in @(
        "ListenPrefix = 'http://127.0.0.1:50833/'",
        "BackendBaseUrl = 'http://[::1]:50832'",
        '$listenUri.Host -cne ''127.0.0.1''',
        '$backendUri.DnsSafeHost -cne ''::1''',
        "'TE','Trailer','Transfer-Encoding','Upgrade'")) {
    if (-not $proxyText.Contains($topologyMarker)) {
        throw "Step 17 proxy lacks explicit topology '$topologyMarker'."
    }
}

$artifactGuard = [regex]::Matches(
    $runnerText,
    'direct child of the ignored artifacts directory').Count
if ($artifactGuard -lt 1) {
    throw 'Step 17 runner does not constrain runtime files to the ignored artifacts directory.'
}

$parseFailures = [Collections.Generic.List[string]]::new()
foreach ($script in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile(
        $script.FullName, [ref]$tokens, [ref]$errors)
    foreach ($parseError in @($errors)) {
        $parseFailures.Add("$($script.Name): $($parseError.Message)")
    }
}
if ($parseFailures.Count) {
    throw ($parseFailures -join [Environment]::NewLine)
}

Write-Host (
    'Step 17 assets passed: 118 new IDs, 58 live entries, ' +
    '60 deterministic/contract mappings, 1,295 inherited entries, ' +
    'and 1,294 inherited local selections.')
