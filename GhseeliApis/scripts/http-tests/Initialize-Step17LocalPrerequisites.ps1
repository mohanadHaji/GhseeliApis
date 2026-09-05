#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolved = $ExecutionContext.SessionState.Path.
    GetUnresolvedProviderPathFromPSPath($OutputPath)
$parent = Split-Path -Parent $resolved
if ((Split-Path -Leaf $parent) -cne 'artifacts') {
    throw 'Step 17 runtime configuration must be written under artifacts.'
}
if ((Split-Path -Leaf $resolved) -notlike '*.local.json') {
    throw 'Step 17 runtime configuration must use a *.local.json name.'
}
New-Item -ItemType Directory -Path $parent -Force | Out-Null

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
    'STEP17_WEBHOOK_SECRET',
    'STEP17_ENABLE_DETERMINISTIC_FAKE_PAYMENT_GATEWAY')

function New-RandomText([int]$ByteCount) {
    $bytes = [byte[]]::new($ByteCount)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) }
    finally { $generator.Dispose() }
    return [Convert]::ToBase64String($bytes).
        TrimEnd('=').Replace('+','-').Replace('/','_')
}

function Assert-Configuration([object]$Configuration) {
    $names = @($Configuration.psobject.Properties.Name)
    $missing = @($required | Where-Object { $_ -notin $names })
    $unexpected = @($names | Where-Object { $_ -notin $required })
    if ($missing.Count -or $unexpected.Count) {
        throw ('Invalid Step 17 runtime configuration fields. Missing: ' +
            "$($missing -join ', '); unexpected: $($unexpected -join ', ').")
    }
    foreach ($name in $required) {
        if ([string]::IsNullOrWhiteSpace(
                [string]$Configuration.psobject.Properties[$name].Value)) {
            throw "Step 17 runtime configuration '$name' is empty."
        }
    }
    foreach ($name in @(
            'STEP17_CUSTOMER_JWT_SECRET',
            'STEP17_BUSINESS_JWT_SECRET',
            'STEP17_CUSTOMER_TO_BUSINESS_HMAC_SECRET',
            'STEP17_BUSINESS_TO_CUSTOMER_HMAC_SECRET',
            'STEP17_BUSINESS_TO_CUSTOMER_NEXT_HMAC_SECRET',
            'STEP17_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET',
            'STEP17_WEBHOOK_SECRET')) {
        if (([string]$Configuration.psobject.Properties[$name].Value).Length -lt
            32) {
            throw "Step 17 runtime configuration '$name' is too short."
        }
    }
    if ($Configuration.STEP17_ENABLE_DETERMINISTIC_FAKE_PAYMENT_GATEWAY -cne
        'true') {
        throw 'The Step 17 local fake payment gateway opt-in must be true.'
    }
}

if (Test-Path -LiteralPath $resolved -PathType Leaf) {
    $existing = Get-Content -LiteralPath $resolved -Raw -Encoding UTF8 |
        ConvertFrom-Json
    Assert-Configuration $existing
    Write-Host 'Reusing existing ignored Step 17 runtime configuration.'
    return
}

$suffix = [guid]::NewGuid().ToString('N')
$configuration = [ordered]@{
    STEP17_CUSTOMER_PASSWORD = "Ghseeli17!c$(New-RandomText 18)"
    STEP17_BUSINESS_OWNER_PASSWORD = "Ghseeli17!b$(New-RandomText 18)"
    STEP17_CUSTOMER_JWT_SECRET = New-RandomText 48
    STEP17_BUSINESS_JWT_SECRET = New-RandomText 48
    STEP17_CUSTOMER_TO_BUSINESS_SERVICE_ID = "step17-customer-$suffix"
    STEP17_CUSTOMER_TO_BUSINESS_HMAC_SECRET = New-RandomText 48
    STEP17_BUSINESS_TO_CUSTOMER_SERVICE_ID = "step17-business-$suffix"
    STEP17_BUSINESS_TO_CUSTOMER_HMAC_SECRET = New-RandomText 48
    STEP17_BUSINESS_TO_CUSTOMER_NEXT_HMAC_SECRET = New-RandomText 48
    STEP17_BUSINESS_TO_CUSTOMER_RECONCILE_SERVICE_ID =
        "step17-reconcile-$suffix"
    STEP17_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET = New-RandomText 48
    STEP17_WEBHOOK_SECRET = "lahza_step17_$(New-RandomText 48)"
    STEP17_ENABLE_DETERMINISTIC_FAKE_PAYMENT_GATEWAY = 'true'
}

$json = $configuration | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText(
    $resolved,
    $json,
    [Text.UTF8Encoding]::new($false))
Write-Host 'Generated ignored Step 17 runtime configuration.'
