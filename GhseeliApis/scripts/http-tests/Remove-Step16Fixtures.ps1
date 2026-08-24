#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory = $true)][string]$StatePath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$state = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
function Test-IsExplicitLocalSqlServer([string]$value) {
    return $value -match '^(?i:\(localdb\)\\[A-Za-z0-9_.-]+)$' -or
        $value -match '^(?i:localhost|127\.0\.0\.1)(?:\\[A-Za-z0-9_.-]+|,[1-9][0-9]{0,4})?$' -or
        $value -match '^\.[\\][A-Za-z0-9_.-]+$'
}
if ($state.fixtureMarker -ne 'STEP16_DISPOSABLE_LOCAL_ONLY' -or
    $state.runId -notmatch '^[a-fA-F0-9]{12,32}$' -or
    $state.server -match '(?i)prod(uction)?' -or
    -not (Test-IsExplicitLocalSqlServer ([string]$state.server))) {
    throw 'Refusing cleanup for non-Step16 or non-local state.'
}
[Data.SqlClient.SqlConnection]::ClearAllPools()
$allCompleted = $true
$suffix = $state.runId.Substring(0,12).ToLowerInvariant()
$expectedDatabases = [ordered]@{
    wrongDatabase = "GhseeliStep16_${suffix}_Wrong"
    businessDatabase = "GhseeliStep16_${suffix}_Business"
    customerDatabase = "GhseeliStep16_${suffix}_Customer"
}
foreach ($property in $expectedDatabases.Keys) {
    $database = [string]$state.$property
    if (-not [string]::Equals(
            $database,
            $expectedDatabases[$property],
            [StringComparison]::Ordinal)) {
        throw "Refusing cleanup of database '$database'."
    }
    $escapedDatabase = $database.Replace(']',']]')
    $connection = [Data.SqlClient.SqlConnection]::new(
        "Server=$($state.server);Database=master;Integrated Security=true;TrustServerCertificate=true")
    try {
        $connection.Open()
        $existsCommand = $connection.CreateCommand()
        $existsCommand.CommandText = 'SELECT COUNT(*) FROM sys.databases WHERE name=@name'
        [void]$existsCommand.Parameters.AddWithValue('@name', $database)
        $exists = [int]$existsCommand.ExecuteScalar() -eq 1
        if (-not $exists) { continue }
        if (-not $PSCmdlet.ShouldProcess(
                $database,'drop uniquely named disposable database')) {
            $allCompleted = $false
            continue
        }
        $command = $connection.CreateCommand()
        $command.CommandText = @"
IF DB_ID(N'$database') IS NOT NULL
BEGIN
 ALTER DATABASE [$escapedDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
 DROP DATABASE [$escapedDatabase];
END
"@
        [void]$command.ExecuteNonQuery()
        $verifyCommand = $connection.CreateCommand()
        $verifyCommand.CommandText = 'SELECT COUNT(*) FROM sys.databases WHERE name=@name'
        [void]$verifyCommand.Parameters.AddWithValue('@name', $database)
        if ([int]$verifyCommand.ExecuteScalar() -ne 0) {
            $allCompleted = $false
            throw "Disposable database '$database' still exists after its approved drop."
        }
    }
    finally { $connection.Dispose() }
}
if ($allCompleted) {
    if ($PSCmdlet.ShouldProcess($StatePath,'remove completed cleanup state file')) {
        Remove-Item -LiteralPath $StatePath -Force -Confirm:$false
        Write-Host 'Step 16 cleanup removed only the run-scoped databases and completed state file.'
    }
    else {
        Write-Warning 'State-file removal was not approved; retaining it for a safe retry.'
    }
}
else {
    Write-Warning 'Step 16 cleanup was not fully approved; retaining the state file for a safe retry.'
}
