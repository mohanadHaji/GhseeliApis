#requires -Version 5.1
<#
.SYNOPSIS
Runs Pester-free self-tests for the HTTP test harness.

.DESCRIPTION
Validates template interpolation, JSON-path lookups, safety guards, manifest
validation, redaction, and core assertion helpers without requiring a live API.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\http-tests\Run-SelfTests.ps1
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path -Path $PSScriptRoot -ChildPath 'HttpTestHarness.psm1') -Force

try {
    Invoke-HttpTestHarnessSelfTest | Out-Null
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
