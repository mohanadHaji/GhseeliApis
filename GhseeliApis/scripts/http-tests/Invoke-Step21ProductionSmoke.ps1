#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$BaseUrl = 'https://ghseelicustomer.runasp.net'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedHost = 'ghseelicustomer.runasp.net'
$baseUri = [Uri]$BaseUrl
if ($baseUri.Scheme -ne 'https' -or
    $baseUri.Host -ne $expectedHost -or
    -not $baseUri.IsDefaultPort) {
    throw "BaseUrl must be https://$expectedHost."
}

function Invoke-Request(
    [string]$Method,
    [string]$Path,
    [string]$Body = '') {
    $parameters = @{
        Uri = [Uri]::new($baseUri, $Path)
        Method = $Method
        SkipHttpErrorCheck = $true
        TimeoutSec = 30
    }
    if ($Method -ne 'GET') {
        $parameters.ContentType = 'application/json'
        $parameters.Body = $Body
    }
    Invoke-WebRequest @parameters
}

function Assert-Status(
    [string]$Name,
    [object]$Response,
    [int]$Expected) {
    if ([int]$Response.StatusCode -ne $Expected) {
        throw "$Name returned $($Response.StatusCode), expected $Expected."
    }
    Write-Host "[PASS] $Name"
}

function Read-ResponseText([object]$Response) {
    if ($Response.Content -is [byte[]]) {
        return [Text.Encoding]::UTF8.GetString($Response.Content)
    }
    return [string]$Response.Content
}

$health = Invoke-Request 'GET' '/api/Health/db'
Assert-Status 'Customer HTTPS database health' $health 200

$swagger = Invoke-Request 'GET' '/swagger/v1/swagger.json'
Assert-Status 'Production Swagger' $swagger 200
$document = Read-ResponseText $swagger | ConvertFrom-Json
foreach ($path in @(
    '/api/v1/payments/intents',
    '/api/v1/payments/{id}/verify',
    '/api/lahza/webhook'
)) {
    if ($null -eq $document.paths.PSObject.Properties[$path]) {
        throw "Production Swagger is missing $path."
    }
}
Write-Host '[PASS] Lahza operations are published'

$intent = Invoke-Request 'POST' '/api/v1/payments/intents' '{}'
Assert-Status 'Anonymous payment initialization rejection' $intent 401

$paymentId = [guid]::NewGuid().ToString('D')
$verify = Invoke-Request 'POST' "/api/v1/payments/$paymentId/verify"
Assert-Status 'Anonymous payment verification rejection' $verify 401

$webhook = Invoke-Request 'POST' '/api/lahza/webhook' '{}'
Assert-Status 'Unsigned webhook rejection' $webhook 400
$problem = Read-ResponseText $webhook | ConvertFrom-Json
if ($problem.code -ne 'lahza_signature_missing') {
    throw "Unsigned webhook returned unexpected code '$($problem.code)'."
}
Write-Host '[PASS] Unsigned webhook returns lahza_signature_missing'

Write-Host 'Step 21 Production smoke checks passed.'
