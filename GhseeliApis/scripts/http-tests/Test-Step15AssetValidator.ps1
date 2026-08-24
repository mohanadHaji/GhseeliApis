#requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$shell = (Get-Process -Id $PID).Path
$validator = Join-Path $PSScriptRoot 'Test-Step15LiveAssets.ps1'
$plan = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) `
    'STEP_15_HTTP_TEST_PLAN.md'
$manifest = Join-Path $PSScriptRoot 'plans\step-15-localization-swagger.manifest.json'
$scratch = Join-Path $PSScriptRoot "artifacts\step15-assets-$([guid]::NewGuid().ToString('N')).local.json"

function Invoke-Validator([string]$path, [bool]$expectSuccess, [string]$name) {
    $priorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & $shell -NoProfile -ExecutionPolicy Bypass -File $validator `
        -PlanPath $plan -ManifestPath $path 2>&1
    $ErrorActionPreference = $priorPreference
    $success = $LASTEXITCODE -eq 0
    if ($success -ne $expectSuccess) {
        throw "$name success was '$success', expected '$expectSuccess'. Output: $output"
    }
    Write-Host "[PASS] $name"
}

try {
    Invoke-Validator $manifest $true 'Validator accepts the complete Step 15 assets'
    $document = Get-Content $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
    ($document.scenarios | Where-Object id -eq `
        'STEP15-AUTH-CROSS-CUSTOMER-TO-BUSINESS-042').headers.Authorization = 'Bearer wrong'
    $document | ConvertTo-Json -Depth 100 | Set-Content $scratch -Encoding UTF8
    Invoke-Validator $scratch $false 'Validator rejects an inexact cross-host JWT reference'

    $document = Get-Content $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
    $document.scenarios = @($document.scenarios | Where-Object id -ne `
        'STEP15-TRANSPORT-DUPLICATE-HEADERS-128--hmac-signature')
    $document | ConvertTo-Json -Depth 100 | Set-Content $scratch -Encoding UTF8
    Invoke-Validator $scratch $false 'Validator rejects a missing required live variant'
}
finally {
    Remove-Item $scratch -Force -ErrorAction SilentlyContinue
}
