[CmdletBinding()]
param(
    [string]$CustomerDatabase = "GhseeliCustomer_FrontendDemo",
    [string]$BusinessDatabase = "GhseeliBusiness_FrontendDemo",
    [string]$OutputPath = "",
    [switch]$ExportOnly
)

$ErrorActionPreference = "Stop"
$solutionRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $solutionRoot "demo-data\frontend-demo-data.json"
}

Push-Location $solutionRoot
try {
    dotnet run --project "Ghseeli.DemoData\Ghseeli.DemoData.csproj" -- export $OutputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Demo JSON export failed with exit code $LASTEXITCODE."
    }

    if ($ExportOnly) {
        return
    }

    $customerConnection =
        "Server=(localdb)\MSSQLLocalDB;Database=$CustomerDatabase;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
    $businessConnection =
        "Server=(localdb)\MSSQLLocalDB;Database=$BusinessDatabase;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"

    dotnet run --project "Ghseeli.DemoData\Ghseeli.DemoData.csproj" -- `
        seed $customerConnection $businessConnection
    if ($LASTEXITCODE -ne 0) {
        throw "Demo database seeding failed with exit code $LASTEXITCODE."
    }

    Write-Host ""
    Write-Host "Demo databases are ready:"
    Write-Host "  Customer: $CustomerDatabase"
    Write-Host "  Business: $BusinessDatabase"
    Write-Host "  Frontend JSON: $OutputPath"
}
finally {
    Pop-Location
}
