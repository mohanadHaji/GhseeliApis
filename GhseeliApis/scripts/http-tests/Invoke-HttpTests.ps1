#requires -Version 5.1
<#
.SYNOPSIS
Runs manifest-driven HTTP API scenarios.

.DESCRIPTION
Loads a JSON manifest, resolves runtime variables, enforces a local-only safety
guard by default, executes HTTP scenarios, writes a JSON results file, and exits
non-zero when any scenario fails.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Invoke-HttpTests.ps1 `
    -ManifestPath .\scripts\http-tests\sample.manifest.json `
    -BaseUrl https://localhost:5001 `
    -Tags smoke

.EXAMPLE
$vars = @{ existingCompanyId = '11111111-1111-1111-1111-111111111111' }
powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Invoke-HttpTests.ps1 `
    -ManifestPath .\scripts\http-tests\sample.manifest.json `
    -ConfigPath .\scripts\http-tests\sample.local.json `
    -VariablesPath .\scripts\http-tests\sample.variables.local.json `
    -Variables $vars `
    -Feature auth `
    -AllowNonLocal
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [string]$BaseUrl,

    [string]$ConfigPath,

    [string]$VariablesPath,

    [hashtable]$Variables,

    [string[]]$Tags,

    [string[]]$Feature,

    [string]$ResultsPath,

    [switch]$AllowNonLocal,

    [switch]$PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path -Path $PSScriptRoot -ChildPath 'HttpTestHarness.psm1') -Force

try {
    $result = Invoke-HttpTestHarness `
        -ManifestPath $ManifestPath `
        -BaseUrl $BaseUrl `
        -ConfigPath $ConfigPath `
        -VariablesPath $VariablesPath `
        -Variables $Variables `
        -Tags $Tags `
        -Feature $Feature `
        -ResultsPath $ResultsPath `
        -AllowNonLocal:$AllowNonLocal
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}

if ($PassThru) {
    $result
}

if (-not $result.Summary.passedAll) {
    exit 1
}
