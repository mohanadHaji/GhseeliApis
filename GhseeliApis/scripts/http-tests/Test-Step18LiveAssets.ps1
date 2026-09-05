#requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$solution = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$manifestPath = Join-Path $PSScriptRoot `
    'plans\step-18-lahza-payment-migration.manifest.json'
$planPath = Join-Path $solution `
    'STEP_18_LAHZA_PAYMENT_MIGRATION_HTTP_TEST_PLAN.md'
$runnerPath = Join-Path $PSScriptRoot 'Invoke-Step18LiveLocal.ps1'
$initializerPath = Join-Path $PSScriptRoot 'Initialize-Step18Fixtures.ps1'
$databaseVerifierPath = Join-Path $PSScriptRoot `
    'Verify-Step18DatabaseInvariants.ps1'
$harnessPath = Join-Path $PSScriptRoot 'HttpTestHarness.psm1'

foreach ($path in @(
    $manifestPath, $planPath, $runnerPath, $initializerPath,
    $databaseVerifierPath, $harnessPath
)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Step 18 asset is missing: $path"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$scenarios = @($manifest.scenarios)
if ($scenarios.Count -lt 30) {
    throw "Step 18 manifest must contain at least 30 scenarios; found $($scenarios.Count)."
}
$duplicates = @($scenarios | Group-Object id | Where-Object Count -ne 1)
if ($duplicates.Count) {
    throw "Step 18 manifest contains duplicate IDs: $($duplicates.Name -join ', ')."
}
if (@($scenarios | Where-Object {
        $_.id -notlike 'STEP18-*' -or
        [string]::IsNullOrWhiteSpace([string]$_.planScenarioId)
    }).Count) {
    throw 'Every Step 18 scenario must have a STEP18 ID and planScenarioId.'
}

$plan = Get-Content -LiteralPath $planPath -Raw -Encoding UTF8
$planIds = [regex]::Matches(
    $plan,
    'STEP18-[A-Z0-9-]+-\d{3}') |
    ForEach-Object Value |
    Sort-Object -Unique
$unknownMappings = @($scenarios.planScenarioId |
    Sort-Object -Unique |
    Where-Object { $_ -notin $planIds })
if ($unknownMappings.Count) {
    throw "Manifest mappings are absent from the Step 18 plan: $($unknownMappings -join ', ')."
}

$requiredMappings = @(
    'STEP18-LAHZA-CONTRACT-001',
    'STEP18-LAHZA-CONFIG-002',
    'STEP18-LAHZA-INIT-004',
    'STEP18-LAHZA-INIT-005',
    'STEP18-LAHZA-INIT-006',
    'STEP18-LAHZA-VERIFY-014',
    'STEP18-LAHZA-VERIFY-017',
    'STEP18-LAHZA-WEBHOOK-018',
    'STEP18-LAHZA-WEBHOOK-019',
    'STEP18-LAHZA-WEBHOOK-020',
    'STEP18-LAHZA-WEBHOOK-021',
    'STEP18-LAHZA-WEBHOOK-022',
    'STEP18-LAHZA-WEBHOOK-023',
    'STEP18-LAHZA-WEBHOOK-024',
    'STEP18-LAHZA-WEBHOOK-025',
    'STEP18-LAHZA-REFUND-026',
    'STEP18-LAHZA-REFUND-027',
    'STEP18-LAHZA-REFUND-028',
    'STEP18-LAHZA-SECURITY-029',
    'STEP18-LAHZA-SWAGGER-030'
)
$missingMappings = @($requiredMappings |
    Where-Object { $_ -notin $scenarios.planScenarioId })
if ($missingMappings.Count) {
    throw "Required live-local mappings are missing: $($missingMappings -join ', ')."
}

$webhooks = @($scenarios | Where-Object {
    $_.url -eq '/api/lahza/webhook'
})
if ($webhooks.Count -lt 15) {
    throw 'Step 18 must exercise the Lahza webhook boundary comprehensively.'
}
$signedWebhooks = @($webhooks | Where-Object {
    $null -ne $_.psobject.Properties['lahzaSignature']
})
if ($signedWebhooks.Count -lt 12) {
    throw 'Step 18 valid webhook scenarios must generate local Lahza signatures.'
}
if (@($signedWebhooks | Where-Object {
        $_.lahzaSignature.secretVariable -cne 'webhookSecret'
    }).Count) {
    throw 'Every generated Lahza signature must use the local webhookSecret variable.'
}
if (@($scenarios | Where-Object {
        $null -ne $_.psobject.Properties['stripeSignature']
    }).Count) {
    throw 'The active Step 18 manifest cannot generate Stripe signatures.'
}

$manifestText = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
foreach ($forbidden in @(
    'sk_live_', 'pk_live_', 'whsec_', 'Bearer eyJ', 'api.lahza.io/transaction'
)) {
    if ($manifestText.IndexOf(
            $forbidden,
            [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Step 18 manifest contains forbidden credential/provider material: $forbidden"
    }
}
if (-not $manifestText.Contains('/api/stripe/webhook') -or
    -not $manifestText.Contains('"status": 404')) {
    throw 'Step 18 must prove the removed Stripe webhook route returns 404.'
}
if (-not $manifestText.Contains('"signBody": "{}"')) {
    throw 'Step 18 must prove exact raw-body changes invalidate the signature.'
}

foreach ($scriptPath in @(
        $runnerPath, $initializerPath, $databaseVerifierPath, $harnessPath)) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile(
        $scriptPath,
        [ref]$tokens,
        [ref]$errors)
    if ($errors.Count) {
        throw "PowerShell parse errors in $scriptPath`: $($errors.Message -join '; ')"
    }
}

$runner = Get-Content -LiteralPath $runnerPath -Raw -Encoding UTF8
foreach ($requiredText in @(
    '[switch]$Execute',
    'Step17TestFixtures__PaymentGateway',
    'DeterministicFake',
    "Lahza__BaseUrl = 'https://api.lahza.io'",
    'Lahza__SecretKey',
    'Stop-Process -Id',
    'Drop-CustomerDatabase',
    'Verify-Step18DatabaseInvariants.ps1',
    'realLahzaCredentialsUsed = $false'
)) {
    if (-not $runner.Contains($requiredText)) {
        throw "Step 18 runner is missing safety requirement: $requiredText"
    }
}
foreach ($forbidden in @('sk_test_', 'sk_live_', 'pk_test_', 'whsec_')) {
    if ($runner.IndexOf(
            $forbidden,
            [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Step 18 runner contains a credential-shaped literal: $forbidden"
    }
}

$harness = Get-Content -LiteralPath $harnessPath -Raw -Encoding UTF8
if (-not $harness.Contains('Apply-LahzaSignatureHeader') -or
    -not $harness.Contains("ResolvedHeaders['X-Lahza-Signature']") -or
    -not $harness.Contains('GetBytes($body)')) {
    throw 'HTTP harness lacks exact raw-body Lahza HMAC-SHA256 support.'
}

Write-Host (("Step 18 live assets valid: {0} scenarios, {1} Lahza webhooks, " +
    "{2} locally signed webhooks.") -f
    $scenarios.Count, $webhooks.Count, $signedWebhooks.Count)
