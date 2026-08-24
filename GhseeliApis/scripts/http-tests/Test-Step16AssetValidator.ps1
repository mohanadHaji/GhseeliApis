#requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$shell = (Get-Process -Id $PID).Path
$validator = Join-Path $PSScriptRoot 'Test-Step16LiveAssets.ps1'
$plan = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) `
    'STEP_16_HTTP_TEST_PLAN.md'
$manifest = Join-Path $PSScriptRoot 'plans\step-16-clean-schema-separation.manifest.json'
$scratch = Join-Path $PSScriptRoot "artifacts\step16-validator-$([guid]::NewGuid().ToString('N')).local.json"

function Invoke-Check([string]$path, [bool]$expected, [string]$name) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & $shell -NoProfile -ExecutionPolicy Bypass -File $validator `
        -PlanPath $plan -ManifestPath $path 2>&1
    $ErrorActionPreference = $old
    if (($LASTEXITCODE -eq 0) -ne $expected) {
        throw "$name did not produce expected success=$expected. $output"
    }
    Write-Host "[PASS] $name"
}

try {
    Invoke-Check $manifest $true 'Validator accepts complete Step 16 assets'
    $document = Get-Content $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
    $document.lifecycleScenarios = @($document.lifecycleScenarios | Where-Object {
            $_.planScenarioId -ne 'STEP16-CLEANUP-ISOLATION-163'
        })
    $document | ConvertTo-Json -Depth 100 | Set-Content $scratch -Encoding UTF8
    Invoke-Check $scratch $false 'Validator rejects a missing live-local mapping'

    $document = Get-Content $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
    $document.scenarios[0].url = 'https://example.com/api/health'
    $document | ConvertTo-Json -Depth 100 | Set-Content $scratch -Encoding UTF8
    Invoke-Check $scratch $false 'Validator rejects a non-local URL'

    $document = Get-Content $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
    $document.sensitiveFields = @($document.sensitiveFields | Where-Object {
            $_ -ne 'authorization'
        })
    $document | ConvertTo-Json -Depth 100 | Set-Content $scratch -Encoding UTF8
    Invoke-Check $scratch $false 'Validator rejects incomplete redaction'

    $document = Get-Content $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
    $matrixEntry = $document.scenarios | Where-Object id -eq `
        'STEP16-REMOVED-WALLET-NOTIFICATION-ROUTES-167--wallet-get-admin'
    $matrixEntry.id =
        'STEP16-REMOVED-WALLET-NOTIFICATION-ROUTES-167--wallet-get-admin-missing'
    $document | ConvertTo-Json -Depth 100 | Set-Content $scratch -Encoding UTF8
    Invoke-Check $scratch $false 'Validator rejects a missing concrete matrix operation'

    $document = Get-Content $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
    $validate = $document.scenarios | Where-Object id -eq `
        'STEP16-CUSTOMER-AUTH-077--03-validate-invalid-json-string'
    $validate.jsonBody = [pscustomobject]@{ token = 'invalid' }
    $document | ConvertTo-Json -Depth 100 | Set-Content $scratch -Encoding UTF8
    Invoke-Check $scratch $false 'Validator rejects object-shaped Auth validate body'
}
finally {
    Remove-Item $scratch -Force -ErrorAction SilentlyContinue
}
