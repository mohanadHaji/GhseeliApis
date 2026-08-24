#requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$shell = (Get-Process -Id $PID).Path
$initializer = Join-Path $PSScriptRoot 'Initialize-Step16Fixtures.ps1'
$runner = Join-Path $PSScriptRoot 'Invoke-Step16LiveLocal.ps1'
$verifier = Join-Path $PSScriptRoot 'Verify-Step16DatabaseInvariants.ps1'
$cleanup = Join-Path $PSScriptRoot 'Remove-Step16Fixtures.ps1'
$run = '16161616161616161616161616161616'
$scratchRemote = Join-Path $PSScriptRoot "artifacts\step16-remote-$run.local.json"
$scratchTampered = Join-Path $PSScriptRoot "artifacts\step16-tampered-$run.local.json"
$scratchUppercase = Join-Path $PSScriptRoot "artifacts\step16-uppercase-$run.local.json"
$scratchWhatIf = Join-Path $PSScriptRoot "artifacts\step16-whatif-$run.local.json"
$scratchOutsideArtifacts = Join-Path $PSScriptRoot "step16-outside-$run.local.json"

function New-State([string]$server,[string]$path) {
    [ordered]@{
        fixtureMarker = 'STEP16_DISPOSABLE_LOCAL_ONLY'
        runId = $run
        server = $server
        customerDatabase = 'GhseeliStep16_161616161616_Customer'
        businessDatabase = 'GhseeliStep16_161616161616_Business'
        wrongDatabase = 'GhseeliStep16_161616161616_Wrong'
        customerPrincipal = 'Step16Customer_161616161616'
        businessPrincipal = 'Step16Business_161616161616'
    } | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding UTF8
}

try {
    New-Item -ItemType Directory (Split-Path $scratchRemote) -Force | Out-Null
    $first = & $initializer -RunId $run -Server '(localdb)\MSSQLLocalDB' `
        -ValidateOnly -PassThru
    $second = & $initializer -RunId $run -Server '(localdb)\MSSQLLocalDB' `
        -ValidateOnly -PassThru
    foreach ($property in @('CustomerDatabase','BusinessDatabase','WrongDatabase',
            'CustomerPrincipal','BusinessPrincipal')) {
        if ($first.$property -cne $second.$property) {
            throw "Fixture derivation is not deterministic for '$property'."
        }
    }
    if (@($first.CustomerDatabase,$first.BusinessDatabase,$first.WrongDatabase |
            Sort-Object -Unique).Count -ne 3) {
        throw 'Fixture database names are not distinct.'
    }
    Write-Host '[PASS] unique local fixture inputs are accepted deterministically'

    $rejected = $false
    try { & $initializer -RunId 'short' -ValidateOnly } catch {
        $rejected = $_.Exception.Message -match 'RunId'
    }
    if (-not $rejected) { throw 'Unsafe short/shared RunId was accepted.' }
    Write-Host '[PASS] shared or non-unique database suffix is rejected'

    $rejected = $false
    try { & $initializer -RunId $run -Server 'production-sql' -ValidateOnly } catch {
        $rejected = $_.Exception.Message -match 'local SQL'
    }
    if (-not $rejected) { throw 'Production-like SQL target was accepted.' }
    Write-Host '[PASS] non-local/production-like SQL target is rejected'

    foreach ($remoteAlias in @(
            'remote-host\SQLEXPRESS',
            'localhost.attacker.example',
            'sql-localhost.internal,1433')) {
        $rejected = $false
        try { & $initializer -RunId $run -Server $remoteAlias -ValidateOnly } catch {
            $rejected = $_.Exception.Message -match 'local SQL'
        }
        if (-not $rejected) {
            throw "Remote SQL alias '$remoteAlias' was accepted as local."
        }
    }
    Write-Host '[PASS] local-server guard rejects remote names containing local substrings'

    New-State 'sql-test.internal.example' $scratchRemote
    $rejected = $false
    try { & $verifier -StatePath $scratchRemote } catch {
        $rejected = $_.Exception.Message -match 'disposable-local'
    }
    if (-not $rejected) { throw 'Verifier accepted a forged remote non-production server.' }
    Write-Host '[PASS] verifier independently rejects remote non-production state'

    $rejected = $false
    try { & $cleanup -StatePath $scratchRemote -Confirm:$false } catch {
        $rejected = $_.Exception.Message -match 'non-local'
    }
    if (-not $rejected) { throw 'Cleanup accepted a forged remote non-production server.' }
    Write-Host '[PASS] cleanup independently rejects remote non-production state'

    New-State '(localdb)\MSSQLLocalDB' $scratchTampered
    $tamperedState = Get-Content -LiteralPath $scratchTampered -Raw |
        ConvertFrom-Json
    $tamperedState.customerDatabase = 'GhseeliStep16_161616161616_Arbitrary'
    $tamperedState | ConvertTo-Json |
        Set-Content -LiteralPath $scratchTampered -Encoding UTF8
    $rejected = $false
    try { & $cleanup -StatePath $scratchTampered -Confirm:$false } catch {
        $rejected = $_.Exception.Message -match 'Refusing cleanup'
    }
    if (-not $rejected) {
        throw 'Cleanup accepted a forged database with the disposable run prefix.'
    }
    Write-Host '[PASS] cleanup requires exact deterministic database names'

    [ordered]@{
        fixtureMarker = 'STEP16_DISPOSABLE_LOCAL_ONLY'
        runId = 'ABCDEFABCDEFABCDEFABCDEFABCDEFAB'
        server = '(localdb)\MSSQLLocalDB'
        customerDatabase = 'GhseeliStep16_abcdefabcdef_Customer'
        businessDatabase = 'GhseeliStep16_abcdefabcdef_Business'
        wrongDatabase = 'GhseeliStep16_abcdefabcdef_Wrong'
    } | ConvertTo-Json |
        Set-Content -LiteralPath $scratchUppercase -Encoding UTF8
    & $cleanup -StatePath $scratchUppercase -WhatIf
    if (-not (Test-Path -LiteralPath $scratchUppercase -PathType Leaf)) {
        throw 'Cleanup erased uppercase-run retry state under -WhatIf.'
    }
    Write-Host '[PASS] cleanup normalizes valid uppercase hexadecimal run IDs'

    New-State '(localdb)\MSSQLLocalDB' $scratchWhatIf
    & $cleanup -StatePath $scratchWhatIf -WhatIf
    if (-not (Test-Path -LiteralPath $scratchWhatIf -PathType Leaf)) {
        throw 'Cleanup erased retry state under -WhatIf.'
    }
    Write-Host '[PASS] cleanup -WhatIf retains state until every database drop completes'

    & $shell -NoProfile -ExecutionPolicy Bypass -File $runner -Phase Validate `
        -RunId $run -Server '(localdb)\MSSQLLocalDB'
    if ($LASTEXITCODE -ne 0) { throw 'Static runner validation failed.' }
    Write-Host '[PASS] runner defaults to static no-side-effect validation'

    $rejected = $false
    try {
        & $runner -Phase Validate -RunId $run `
            -StatePath $scratchOutsideArtifacts
    }
    catch { $rejected = $_.Exception.Message -match 'artifacts' }
    if (-not $rejected -or
        (Test-Path -LiteralPath $scratchOutsideArtifacts)) {
        throw 'Runner accepted a state path outside its ignored artifacts directory.'
    }
    Write-Host '[PASS] runner confines secret-bearing state to the ignored artifacts directory'

    $rejected = $false
    try {
        & $runner -Phase Provision -RunId $run -Server '(localdb)\MSSQLLocalDB'
    }
    catch { $rejected = $_.Exception.Message -match '-Execute' }
    if (-not $rejected) { throw 'Destructive fixture phase ran without explicit -Execute.' }
    Write-Host '[PASS] database lifecycle requires explicit execution opt-in'

    $initializerText = Get-Content -LiteralPath $initializer -Raw
    $runnerText = Get-Content -LiteralPath $runner -Raw
    if (-not $initializerText.Contains('ConnectionStrings__CustomerConnection') -or
        -not $runnerText.Contains('ConnectionStrings__CustomerConnection') -or
        -not $runnerText.Contains('BusinessApiClient__BaseUrl') -or
        -not $runnerText.Contains('BusinessApiClient__ServiceId') -or
        -not $runnerText.Contains('BusinessApiClient__ActiveSecret') -or
        -not $runnerText.Contains('reservation_status_read') -or
        -not $runnerText.Contains(
            'InternalServiceAuthentication__Services__1__ServiceId') -or
        -not $runnerText.Contains(
            'STEP16_CUSTOMER_TO_BUSINESS_CATALOG_HMAC_SECRET') -or
        -not $runnerText.Contains(
            'STEP16_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET') -or
        -not $runnerText.Contains(
            '$customerFullSecret -ceq $customerCatalogSecret') -or
        -not $runnerText.Contains(
            '$callbackServiceId -ceq $reconcileServiceId') -or
        -not $runnerText.Contains(
            '$callbackSecret -ceq $reconcileSecret') -or
        -not $runnerText.Contains(
            "Join-Path `$PSScriptRoot 'artifacts\step-16.state.local.json'") -or
        $initializerText.Contains('ConnectionStrings__RemoteTest') -or
        $runnerText.Contains('ConnectionStrings__RemoteTest')) {
        throw 'Step 16 lifecycle does not configure the owned database and outbound Business client explicitly.'
    }
    Write-Host '[PASS] runtime explicitly configures the Customer database and outbound Business client'

    if ($runnerText -notmatch
            '(?s)\$customerCrossWired\s*=\s*Start-CustomerHost.+?\$state\.businessDatabase' -or
        $runnerText -notmatch
            '(?s)\$businessCrossWired\s*=\s*Start-Host.+?ConnectionStrings__BusinessConnection.+?\$state\.customerDatabase' -or
        -not $runnerText.Contains(
            'ConnectionStrings__CustomerConnection=$customerDeniedBuilder.ConnectionString') -or
        -not $runnerText.Contains(
            'ConnectionStrings__BusinessConnection=$businessDeniedBuilder.ConnectionString')) {
        throw 'Lifecycle runner does not cross-wire both owned databases or use the active SQL-denial builders.'
    }
    foreach ($required in @(
            'Customer migration child process failed with exit code $LASTEXITCODE.',
            'Business migration child process failed with exit code $LASTEXITCODE.')) {
        if (-not $initializerText.Contains($required)) {
            throw "Parallel migration child exit validation is missing '$required'."
        }
    }
    $verifierText = Get-Content -LiteralPath $verifier -Raw
    if (-not $verifierText.Contains(
            "Query -database 'master'") -or
        -not $verifierText.Contains(
            '-sql "SELECT COUNT(*) FROM sys.databases') -or
        -not $verifierText.Contains(
            'Query -database $item.Database `') -or
        -not $verifierText.Contains(
            '-sql "SELECT COUNT(*) FROM [$table]"')) {
        throw 'Zero-domain-row verification must bind the database and SQL arguments explicitly.'
    }
    if (-not $verifierText.Contains('$customerPrincipals = @(if') -or
        -not $verifierText.Contains('$businessPrincipals = @(if')) {
        throw 'Principal verification must preserve empty query results as arrays.'
    }
    Write-Host '[PASS] cross-wire, login-denial, and parallel-migration failures use the intended inputs'

    $tokens = $null
    $parseErrors = $null
    $runnerAst = [Management.Automation.Language.Parser]::ParseFile(
        $runner, [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count) {
        throw "Runner AST could not be parsed: $($parseErrors -join '; ')"
    }
    foreach ($functionName in @(
            'Assert-FinalCleanup','Assert-RuntimeCredentialOverlay',
            'Set-Step16DatabaseOnline')) {
        $definition = @($runnerAst.FindAll({
                    param($node)
                    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                    $node.Name -ceq $functionName
                }, $true))
        if ($definition.Count -ne 1 -or
            $definition[0].Parent -isnot
                [Management.Automation.Language.NamedBlockAst]) {
            throw "Runner helper '$functionName' must be defined once at script scope."
        }
    }
    Write-Host '[PASS] lifecycle helpers are callable from every top-level phase'

    foreach ($required in @(
            '-Operation CustomerFirst',
            '-Operation BusinessFirst',
            '-Operation Parallel',
            '-Operation Repeat',
            '-Operation ResetCustomer',
            '-Operation ResetBusiness',
            "Invoke-ManifestTag 'customer-first'",
            "Invoke-ManifestTag 'business-first'",
            "Invoke-ManifestTag 'after-parallel'",
            "Invoke-ManifestTag 'after-repeat'",
            "Invoke-ManifestTag 'after-customer-reset'",
            "Invoke-ManifestTag 'after-business-reset'",
            "Invoke-ManifestTag 'after-reset-sequence'",
            "Invoke-ManifestTag 'production'",
            "Invoke-ManifestTag 'unmigrated'",
            "Invoke-ManifestTag 'wrong-schema'",
            "Invoke-ManifestTag 'customer-outage'",
            "Invoke-ManifestTag 'customer-recovered'",
            "Invoke-ManifestTag 'business-outage'",
            "Invoke-ManifestTag 'business-recovered'",
            "Invoke-ManifestTag 'business-down'",
            "Invoke-ManifestTag 'customer-down'",
            "Invoke-ManifestTag 'customer-restarting'",
            "Invoke-ManifestTag 'business-restarting'",
            "Invoke-ManifestTag 'after-restart'",
            "Invoke-ManifestTag 'after-second-restart'",
            "Invoke-ManifestTag 'concurrent-hosts'",
            "Invoke-ManifestTag 'customer-table-missing'",
            "Invoke-ManifestTag 'business-table-missing'",
            "Invoke-ManifestTag 'login-denied-customer'",
            "Invoke-ManifestTag 'login-denied-business'",
            '$customerDeniedBuilder[''Password''] = $deniedPassword',
            '$businessDeniedBuilder[''Password''] = $deniedPassword',
            "'customer-first-order'",
            "'business-first-order'",
            "'parallel-migration'",
            "'repeat-migration-twice'",
            "'combined-customer-business-reset-sequence'")) {
        if (-not $runnerText.Contains($required)) {
            throw "All lifecycle orchestration is missing explicit phase/evidence '$required'."
        }
    }
    Write-Host '[PASS] All explicitly executes and records lifecycle, failure, recovery, and cleanup phases'
}
finally {
    Remove-Item $scratchRemote,$scratchTampered,$scratchUppercase,`
        $scratchWhatIf,$scratchOutsideArtifacts -Force -ErrorAction SilentlyContinue
}
