#requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$initializer = Join-Path $PSScriptRoot 'Initialize-Step17LocalPrerequisites.ps1'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
$path = Join-Path $artifacts (
    "step17-prerequisite-test-$([guid]::NewGuid().ToString('N')).local.json")

function Assert-True([bool]$Condition,[string]$Message) {
    if (-not $Condition) { throw $Message }
}

try {
    & $initializer -OutputPath $path
    $firstText = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    $first = $firstText | ConvertFrom-Json
    Assert-True (@($first.psobject.Properties).Count -eq 13) (
        'Expected exactly 13 generated runtime fields.')
    Assert-True ($first.STEP17_CUSTOMER_PASSWORD -match '^Ghseeli17!c') (
        'Customer password lacks the safe generated prefix.')
    Assert-True ($first.STEP17_BUSINESS_OWNER_PASSWORD -match '^Ghseeli17!b') (
        'Business password lacks the safe generated prefix.')
    Assert-True ($first.STEP17_CUSTOMER_PASSWORD -cne
        $first.STEP17_BUSINESS_OWNER_PASSWORD) (
        'Customer and Business test passwords must differ.')
    foreach ($name in @(
            'STEP17_CUSTOMER_JWT_SECRET',
            'STEP17_BUSINESS_JWT_SECRET',
            'STEP17_CUSTOMER_TO_BUSINESS_HMAC_SECRET',
            'STEP17_BUSINESS_TO_CUSTOMER_HMAC_SECRET',
            'STEP17_BUSINESS_TO_CUSTOMER_NEXT_HMAC_SECRET',
            'STEP17_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET',
            'STEP17_WEBHOOK_SECRET')) {
        Assert-True (
            ([string]$first.psobject.Properties[$name].Value).Length -ge 32
        ) "$name must contain at least 32 characters."
    }
    Assert-True (
        $first.STEP17_ENABLE_DETERMINISTIC_FAKE_PAYMENT_GATEWAY -ceq 'true'
    ) 'The deterministic fake payment gateway opt-in must be true.'

    & $initializer -OutputPath $path
    Assert-True (
        (Get-Content -LiteralPath $path -Raw -Encoding UTF8) -ceq $firstText
    ) 'Existing generated runtime configuration must be reused unchanged.'
    Write-Host '[PASS] Step 17 prerequisites are generated once and reused.'
}
finally {
    Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
}
