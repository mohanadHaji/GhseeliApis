#requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$manifestPath = Join-Path $PSScriptRoot 'plans\step-20-available-slots.manifest.json'
$planPath = Join-Path $root 'STEP_20_AVAILABLE_SLOTS_HTTP_TEST_PLAN.md'
$runnerPath = Join-Path $PSScriptRoot 'Invoke-Step17LiveLocal.ps1'

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$plan = Get-Content -LiteralPath $planPath -Raw -Encoding UTF8
$runner = Get-Content -LiteralPath $runnerPath -Raw -Encoding UTF8
$ids = @($manifest.scenarios | ForEach-Object { [string]$_.id })

if ($ids.Count -ne 7 -or @($ids | Select-Object -Unique).Count -ne $ids.Count) {
    throw 'Step 20 manifest must contain seven unique live-local scenarios.'
}

foreach ($scenario in @($manifest.scenarios)) {
    if ([string]::IsNullOrWhiteSpace([string]$scenario.planScenarioId) -or
        -not $plan.Contains([string]$scenario.planScenarioId)) {
        throw "Scenario '$($scenario.id)' does not map to the Step 20 plan."
    }
}

foreach ($required in @(
        "'AvailableSlots'",
        'step-20-available-slots.manifest.json',
        'Invoke-Step20Manifest',
        "'appointment_available_slots'")) {
    if (-not $runner.Contains($required)) {
        throw "Step 20 runner is missing required marker '$required'."
    }
}

Write-Host "Step 20 live assets passed: $($ids.Count) unique local scenarios."
