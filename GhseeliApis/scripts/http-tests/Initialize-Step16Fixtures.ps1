#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Apply','CustomerFirst','BusinessFirst','Repeat',
        'ResetCustomer','ResetBusiness','Parallel',
        'MigrateCustomer','MigrateBusiness')]
    [string]$Operation = 'Apply',
    [string]$RunId = ([guid]::NewGuid().ToString('N')),
    [string]$Server = '(localdb)\MSSQLLocalDB',
    [string]$StatePath = '.\scripts\http-tests\artifacts\step-16.state.local.json',
    [switch]$ValidateOnly,
    [switch]$PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$solution = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Test-IsExplicitLocalSqlServer([string]$value) {
    return $value -match '^(?i:\(localdb\)\\[A-Za-z0-9_.-]+)$' -or
        $value -match '^(?i:localhost|127\.0\.0\.1)(?:\\[A-Za-z0-9_.-]+|,[1-9][0-9]{0,4})?$' -or
        $value -match '^\.[\\][A-Za-z0-9_.-]+$'
}

if ($RunId -notmatch '^[a-fA-F0-9]{12,32}$') {
    throw 'RunId must be a unique 12-32 character hexadecimal value.'
}
if ($Server -match '(?i)prod(uction)?' -or
    -not (Test-IsExplicitLocalSqlServer $Server)) {
    throw 'Step 16 fixtures require an explicitly local SQL Server.'
}
$suffix = $RunId.Substring(0, 12).ToLowerInvariant()
$customerDatabase = "GhseeliStep16_${suffix}_Customer"
$businessDatabase = "GhseeliStep16_${suffix}_Business"
$wrongDatabase = "GhseeliStep16_${suffix}_Wrong"
$customerPrincipal = "Step16Customer_$suffix"
$businessPrincipal = "Step16Business_$suffix"
$names = @($customerDatabase, $businessDatabase, $wrongDatabase)
if (@($names | Sort-Object -Unique).Count -ne 3 -or
    @($names | Where-Object { $_ -match '(?i)prod(uction)?' }).Count) {
    throw 'Disposable database names are not safe and distinct.'
}
if ($ValidateOnly) {
    Write-Host "Step 16 fixture safety passed for run '$suffix' on local SQL."
    if ($PassThru) {
        return [pscustomobject]@{
            CustomerDatabase = $customerDatabase
            BusinessDatabase = $businessDatabase
            WrongDatabase = $wrongDatabase
            CustomerPrincipal = $customerPrincipal
            BusinessPrincipal = $businessPrincipal
        }
    }
    return
}

function Open-Sql([string]$database) {
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$Server;Database=$database;Integrated Security=true;TrustServerCertificate=true")
    $connection.Open()
    return $connection
}
function Invoke-Sql([string]$database, [string]$text) {
    $connection = Open-Sql $database
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $text
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}
function Test-Database([string]$name) {
    $connection = Open-Sql master
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = 'SELECT COUNT(*) FROM sys.databases WHERE name=@name'
        [void]$command.Parameters.AddWithValue('@name', $name)
        return [int]$command.ExecuteScalar() -eq 1
    }
    finally { $connection.Dispose() }
}
function New-Database([string]$name) {
    if (Test-Database $name) { throw "Disposable database '$name' already exists." }
    Invoke-Sql master "CREATE DATABASE [$name];"
}
function Remove-Database([string]$name) {
    [Data.SqlClient.SqlConnection]::ClearAllPools()
    if (Test-Database $name) {
        Invoke-Sql master "ALTER DATABASE [$name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$name];"
    }
}
function Add-RestrictedPrincipal([string]$database, [string]$principal) {
    Invoke-Sql $database @"
CREATE USER [$principal] WITHOUT LOGIN;
GRANT CONNECT TO [$principal];
ALTER ROLE [db_datareader] ADD MEMBER [$principal];
ALTER ROLE [db_datawriter] ADD MEMBER [$principal];
"@
}
function Invoke-Migration([ValidateSet('Customer','Business')][string]$owner) {
    $isCustomer = $owner -eq 'Customer'
    $database = if ($isCustomer) { $customerDatabase } else { $businessDatabase }
    $project = if ($isCustomer) {
        Join-Path $solution 'GhseeliApis\GhseeliApis.csproj'
    } else {
        Join-Path $solution 'Ghseeli.BusinessApi\Ghseeli.BusinessApi.csproj'
    }
    $connectionName = if ($isCustomer) {
        'ConnectionStrings__CustomerConnection'
    } else {
        'ConnectionStrings__BusinessConnection'
    }
    $prior = [Environment]::GetEnvironmentVariable($connectionName)
    [Environment]::SetEnvironmentVariable($connectionName,
        "Server=$Server;Database=$database;Integrated Security=true;TrustServerCertificate=true")
    try {
        & dotnet ef database update --project $project --startup-project $project `
            --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw "$owner migration failed." }
    }
    finally { [Environment]::SetEnvironmentVariable($connectionName, $prior) }
}

$created = [Collections.Generic.List[string]]::new()
try {
    if ($Operation -in @('Apply','Parallel')) {
        foreach ($name in $names) { New-Database $name; $created.Add($name) }
    }
    elseif ($Operation -eq 'CustomerFirst') {
        foreach ($name in @($customerDatabase,$wrongDatabase)) {
            New-Database $name
            $created.Add($name)
        }
    }
    elseif ($Operation -eq 'BusinessFirst') {
        foreach ($name in @($businessDatabase,$wrongDatabase)) {
            New-Database $name
            $created.Add($name)
        }
    }
    elseif (-not (Test-Database $customerDatabase) -or
        -not (Test-Database $businessDatabase) -or
        -not (Test-Database $wrongDatabase)) {
        throw 'Repeat/reset requires the exact prior Step 16 disposable databases.'
    }

    switch ($Operation) {
        'Apply' { Invoke-Migration Customer; Invoke-Migration Business }
        'CustomerFirst' {
            Invoke-Migration Customer
            if (Test-Database $businessDatabase) {
                throw 'Business database unexpectedly existed during Customer-first migration.'
            }
        }
        'BusinessFirst' {
            Invoke-Migration Business
            if (Test-Database $customerDatabase) {
                throw 'Customer database unexpectedly existed during Business-first migration.'
            }
        }
        'Repeat' {
            Invoke-Migration Customer; Invoke-Migration Business
            Invoke-Migration Customer; Invoke-Migration Business
        }
        'ResetCustomer' {
            Remove-Database $customerDatabase
            New-Database $customerDatabase
            Invoke-Migration Customer
            Invoke-Migration Customer
            Add-RestrictedPrincipal $customerDatabase $customerPrincipal
        }
        'ResetBusiness' {
            Remove-Database $businessDatabase
            New-Database $businessDatabase
            Invoke-Migration Business
            Invoke-Migration Business
            Add-RestrictedPrincipal $businessDatabase $businessPrincipal
        }
        'Parallel' {
            $shell = (Get-Process -Id $PID).Path
            $jobs = @(
                Start-Job -ScriptBlock {
                    param($shell,$script,$run,$server)
                    & $shell -NoProfile -ExecutionPolicy Bypass -File $script `
                        -Operation MigrateCustomer -RunId $run -Server $server
                    if ($LASTEXITCODE -ne 0) {
                        throw "Customer migration child process failed with exit code $LASTEXITCODE."
                    }
                } -ArgumentList $shell,$PSCommandPath,$RunId,$Server
            )
            $jobs += Start-Job -ScriptBlock {
                param($shell,$script,$run,$server)
                & $shell -NoProfile -ExecutionPolicy Bypass -File $script `
                    -Operation MigrateBusiness -RunId $run -Server $server
                if ($LASTEXITCODE -ne 0) {
                    throw "Business migration child process failed with exit code $LASTEXITCODE."
                }
            } -ArgumentList $shell,$PSCommandPath,$RunId,$Server
            $jobs | Wait-Job | Out-Null
            $failed = @($jobs | Where-Object State -ne 'Completed')
            $output = $jobs | Receive-Job
            $jobs | Remove-Job -Force
            if ($failed.Count) { throw "Parallel migrations failed. $output" }
        }
        'MigrateCustomer' { Invoke-Migration Customer }
        'MigrateBusiness' { Invoke-Migration Business }
    }
    if ($Operation -in @('MigrateCustomer','MigrateBusiness')) {
        return
    }
    if ($Operation -in @('Apply','CustomerFirst','BusinessFirst','Parallel')) {
        if (Test-Database $customerDatabase) {
            Add-RestrictedPrincipal $customerDatabase $customerPrincipal
        }
        if (Test-Database $businessDatabase) {
            Add-RestrictedPrincipal $businessDatabase $businessPrincipal
        }
    }

    $state = [ordered]@{
        fixtureMarker = 'STEP16_DISPOSABLE_LOCAL_ONLY'
        runId = $RunId
        server = $Server
        customerDatabase = $customerDatabase
        businessDatabase = $businessDatabase
        wrongDatabase = $wrongDatabase
        customerPrincipal = $customerPrincipal
        businessPrincipal = $businessPrincipal
        customerBaseUrl = 'https://localhost:54431'
        businessBaseUrl = 'https://localhost:54432'
        businessHttpBaseUrl = 'http://localhost:50832'
        customerProductionBaseUrl = 'https://localhost:54433'
        businessProductionBaseUrl = 'https://localhost:54434'
        customerSecondBaseUrl = 'https://localhost:54435'
        businessSecondBaseUrl = 'https://localhost:54436'
        fixtureRunId = $suffix
        unknownId = [guid]::NewGuid().ToString('D')
        correlationId = "step16-$suffix"
    }
    $fullStatePath = [IO.Path]::GetFullPath($StatePath)
    New-Item -ItemType Directory -Path (Split-Path $fullStatePath) -Force | Out-Null
    $state | ConvertTo-Json -Depth 5 | Set-Content $fullStatePath -Encoding UTF8
    Write-Host "Step 16 $Operation completed for three uniquely named disposable local databases."
    if ($PassThru) { return [pscustomobject]$state }
}
catch {
    if ($Operation -in @('Apply','CustomerFirst','BusinessFirst','Parallel')) {
        foreach ($name in $created) {
            try { Remove-Database $name } catch { Write-Warning 'A failed setup database requires cleanup.' }
        }
    }
    throw
}
