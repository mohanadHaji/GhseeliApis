#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$PlanPath = '.\STEP_15_HTTP_TEST_PLAN.md',
    [string]$ManifestPath =
        '.\scripts\http-tests\plans\step-15-localization-swagger.manifest.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath $PlanPath -Encoding UTF8
$manifestText = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8
$manifest = $manifestText | ConvertFrom-Json

$allPlanIds = [Collections.Generic.List[string]]::new()
$livePlanIds = [Collections.Generic.List[string]]::new()
foreach ($line in $plan) {
    if ($line -match '^\| `(STEP15-[^`]+)` \| ([^|]+) \|') {
        $planId = $Matches[1]
        $level = $Matches[2]
        $allPlanIds.Add($planId)
        if ($level -match 'live-local') {
            $livePlanIds.Add($planId)
        }
    }
}
if ($allPlanIds.Count -ne 172 -or
    @($allPlanIds | Sort-Object -Unique).Count -ne 172) {
    throw "Expected 172 unique frozen plan IDs; found $($allPlanIds.Count)."
}
if ($livePlanIds.Count -ne 42) {
    throw "Expected 42 live-local plan IDs; found $($livePlanIds.Count)."
}

$scenarios = @($manifest.scenarios)
if ($scenarios.Count -ne 90) {
    throw "Manifest must contain exactly 90 stable entries; found $($scenarios.Count)."
}
if (@($scenarios.id | Sort-Object -Unique).Count -ne $scenarios.Count) {
    throw 'Manifest scenario IDs must be unique.'
}
$mapped = @($scenarios.planScenarioId | Sort-Object -Unique)
$missing = @($livePlanIds | Where-Object { $_ -notin $mapped })
$allowedMappings = @($livePlanIds + 'STEP15-TRANSPORT-QUERY-ENCODING-129')
$unexpected = @($mapped | Where-Object { $_ -notin $allowedMappings })
if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
    throw ("Live mapping mismatch. Missing: {0}; unexpected: {1}." -f
        ($missing -join ', '), ($unexpected -join ', '))
}
foreach ($planId in $livePlanIds) {
    if (@($scenarios | Where-Object {
            $_.id -eq $planId -and $_.planScenarioId -eq $planId
        }).Count -ne 1) {
        throw "Frozen scenario '$planId' must retain exactly one canonical entry."
    }
}

$requiredExpandedEntries = @(
    'STEP15-LANG-HEADER-OVERSIZE-014--max-8192',
    'STEP15-PROBLEM-BODY-BOUNDARY-024--fixed-65536',
    'STEP15-PROBLEM-BODY-BOUNDARY-024--fixed-65537',
    'STEP15-TRANSPORT-HEAD-OPTIONS-124--customer-options-mutation',
    'STEP15-TRANSPORT-HEAD-OPTIONS-124--business-options-mutation',
    'STEP15-TRANSPORT-TRAILING-SLASH-125--customer-config',
    'STEP15-TRANSPORT-TRAILING-SLASH-125--business-company',
    'STEP15-TRANSPORT-ACCEPT-126--customer-real-route',
    'STEP15-TRANSPORT-ACCEPT-126--business-real-route',
    'STEP15-TRANSPORT-CHARSET-127--utf8-arabic',
    'STEP15-TRANSPORT-CHARSET-127--utf8-hebrew',
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--customer-authorization-values',
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--business-authorization-values',
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--device-values',
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--idempotency-values',
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--order-guid-values',
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--hmac-service-id',
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--hmac-timestamp',
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--hmac-nonce',
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--hmac-signature',
    'STEP15-TRANSPORT-QUERY-ENCODING-129--percent-case',
    'STEP15-TRANSPORT-QUERY-ENCODING-129--duplicate-language',
    'STEP15-SWAGGER-ENVIRONMENT-152--business-production-disabled',
    'STEP15-SWAGGER-UI-153--customer-initializer-target',
    'STEP15-SWAGGER-UI-153--business-initializer-target',
    'STEP15-HEADERS-HSTS-PRODUCTION-168--customer-api',
    'STEP15-HEADERS-HSTS-PRODUCTION-168--business-api',
    'STEP15-HEADERS-HSTS-DEVELOPMENT-169--customer-http',
    'STEP15-HEADERS-HSTS-DEVELOPMENT-169--business-http',
    'STEP15-HEADERS-SWAGGER-CSP-170--customer-index',
    'STEP15-HEADERS-SWAGGER-CSP-170--business-index')
$missingExpandedEntries = @($requiredExpandedEntries | Where-Object {
    $_ -notin @($scenarios.id)
})
if ($missingExpandedEntries.Count -gt 0) {
    throw "Missing required expanded entries: $($missingExpandedEntries -join ', ')."
}

$crossCustomer = $scenarios | Where-Object {
    $_.id -eq 'STEP15-AUTH-CROSS-CUSTOMER-TO-BUSINESS-042'
}
$crossBusiness = $scenarios | Where-Object {
    $_.id -eq 'STEP15-AUTH-CROSS-BUSINESS-TO-CUSTOMER-043'
}
if ($crossCustomer.headers.Authorization -cne 'Bearer {{var:customerJwt}}' -or
    $crossBusiness.headers.Authorization -cne 'Bearer {{var:businessJwt}}') {
    throw 'Cross-host scenarios must reference the exact opposite-host JWT fixture variables.'
}

$requiredDuplicateHeaders = [ordered]@{
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--device-values' =
        [ordered]@{
            name = 'X-Device-Token'
            values = @('{{var:customerDeviceToken}}', '{{var:foreignDeviceToken}}')
        }
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--customer-authorization-values' =
        [ordered]@{
            name = 'Authorization'
            values = @('Bearer {{var:customerJwt}}', 'Bearer {{var:businessJwt}}')
        }
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--business-authorization-values' =
        [ordered]@{
            name = 'Authorization'
            values = @('Bearer {{var:businessJwt}}', 'Bearer {{var:customerJwt}}')
        }
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--idempotency-values' =
        [ordered]@{ name = 'Idempotency-Key'; values = @('step15-a', 'step15-b') }
    'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--order-guid-values' =
        [ordered]@{
            name = 'X-Order-Guid'
            values = @('{{var:unknownOrderGuid}}', '{{gen:guid}}')
        }
}
foreach ($requirement in $requiredDuplicateHeaders.GetEnumerator()) {
    $entry = $scenarios | Where-Object id -eq $requirement.Key
    $actual = @($entry.headers.psobject.Properties[
        $requirement.Value.name].Value)
    $expected = @($requirement.Value.values)
    if ($actual.Count -ne 2 -or
        ($actual -join "`0") -cne ($expected -join "`0")) {
        throw "Entry '$($requirement.Key)' must send the two exact separate header values."
    }
}
foreach ($headerSuffix in @('service-id', 'timestamp', 'nonce', 'signature')) {
    $entry = $scenarios | Where-Object id -eq `
        "STEP15-TRANSPORT-DUPLICATE-HEADERS-128--hmac-$headerSuffix"
    $matchingHeader = @($entry.headers.psobject.Properties | Where-Object {
        $_.Name -like 'X-Ghseeli-*'
    })
    if ($matchingHeader.Count -ne 1 -or
        @($matchingHeader[0].Value).Count -ne 2) {
        throw "HMAC duplicate entry '$($entry.id)' must send two separate values."
    }
}

$fixtureVariables = @(
    'customerBaseUrl', 'customerHttpBaseUrl', 'businessBaseUrl',
    'businessHttpBaseUrl', 'customerProductionBaseUrl',
    'businessProductionBaseUrl', 'customerDeviceToken',
    'expiredDeviceToken', 'foreignDeviceToken', 'customerJwt',
    'businessJwt', 'oversizedLanguageHeader')
$setupVariables = @($manifest.setupVariables.psobject.Properties.Name)
$knownVariables = @($fixtureVariables + $setupVariables | Sort-Object -Unique)
$referencedVariables = @(
    [regex]::Matches($manifestText, '\{\{var:([A-Za-z0-9_.-]+)\}\}') |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique)
$unknownVariables = @(
    $referencedVariables | Where-Object { $_ -notin $knownVariables })
if ($unknownVariables.Count -gt 0) {
    throw "Manifest references undeclared runtime variables: $($unknownVariables -join ', ')."
}
$unsupportedTokens = @(
    [regex]::Matches($manifestText, '\{\{([^{}]+)\}\}') |
        ForEach-Object { $_.Groups[1].Value } |
        Where-Object {
            $_ -notmatch '^var:[A-Za-z0-9_.-]+$' -and
            $_ -notmatch '^env:[A-Za-z_][A-Za-z0-9_]*$' -and
            $_ -notmatch '^gen:(guid|nonce|timestamp(?::[^{}]+)?)$'
        } |
        Sort-Object -Unique)
if ($unsupportedTokens.Count -gt 0) {
    throw "Manifest contains unsupported template tokens: $($unsupportedTokens -join ', ')."
}

foreach ($scenario in $scenarios) {
    if ([string]::IsNullOrWhiteSpace($scenario.method) -or
        [string]::IsNullOrWhiteSpace($scenario.url) -or
        $null -eq $scenario.expect -or
        $null -eq $scenario.expect.status) {
        throw "Scenario '$($scenario.id)' is missing method, URL, or expected status."
    }
    $assertionNames = @(
        'contentType', 'headers', 'json', 'jsonPropertyCount',
        'bodyEquals', 'bodyContains', 'bodyNotContains',
        'headerEqualsJsonPath', 'errorCode')
    if (@($assertionNames | Where-Object {
            $scenario.expect.psobject.Properties.Name -contains $_
        }).Count -eq 0) {
        throw "Scenario '$($scenario.id)' is status-only."
    }
    if (@($scenario.expect.bodyNotContains).Count -eq 0) {
        throw "Scenario '$($scenario.id)' lacks negative leakage assertions."
    }
    if ($scenario.expect.psobject.Properties.Name -contains 'contentType' -and
        $scenario.expect.contentType -eq 'application/problem+json' -and
        $null -eq $scenario.expect.jsonPropertyCount) {
        throw "Problem scenario '$($scenario.id)' lacks an exact JSON property count."
    }
}

$manifestDirectory = Split-Path -Parent (
    Resolve-Path -LiteralPath $ManifestPath).Path
foreach ($scenario in $scenarios) {
    if ($scenario.psobject.Properties.Name -contains 'bodyFile') {
        $bodyPath = Join-Path $manifestDirectory $scenario.bodyFile
        if (-not (Test-Path -LiteralPath $bodyPath -PathType Leaf)) {
            throw "Scenario '$($scenario.id)' body file is missing."
        }
    }
}

$parseFailures = [Collections.Generic.List[string]]::new()
foreach ($script in Get-ChildItem -LiteralPath (Split-Path $manifestDirectory) `
        -Filter '*.ps1' -File) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile(
        $script.FullName, [ref]$tokens, [ref]$errors)
    foreach ($parseError in @($errors)) {
        $parseFailures.Add("$($script.Name): $($parseError.Message)")
    }
}
if ($parseFailures.Count -gt 0) {
    throw ($parseFailures -join [Environment]::NewLine)
}

$customerCount = @($scenarios | Where-Object {
    $_.tags -contains 'customer' -or
    $_.tags -contains 'customerProduction'
}).Count
$businessCount = $scenarios.Count - $customerCount
Write-Host ("Step 15 assets passed: 172 frozen IDs, 42 live mappings, " +
    "$($scenarios.Count) stable manifest entries ($customerCount Customer, " +
    "$businessCount Business), $($referencedVariables.Count) resolved variable names.")
