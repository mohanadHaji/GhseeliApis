param(
    [Parameter(Mandatory = $true)]
    [string]$CustomerDatabase
)

$ErrorActionPreference = 'Stop'
if ($CustomerDatabase -notmatch '^[A-Za-z0-9_]+$') {
    throw 'Unsafe database name.'
}

$solution = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $solution 'GhseeliApis\GhseeliApis.csproj'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

$cases = @(
    @{
        Name = 'computed renewal interval below practical minimum'
        Fraction = '0.099'
        SafetyMargin = '100'
        Port = '50720'
    },
    @{
        Name = 'computed renewal interval reaches safety deadline'
        Fraction = '0.9'
        SafetyMargin = '100'
        Port = '50721'
    }
)

$passed = 0
foreach ($case in $cases) {
    $slug = $case.Name -replace '[^a-z0-9]+', '-'
    $stdout = Join-Path $artifacts "step-13-lease-startup-$slug.stdout.txt"
    $stderr = Join-Path $artifacts "step-13-lease-startup-$slug.stderr.txt"
    Remove-Item $stdout, $stderr -Force -ErrorAction SilentlyContinue

    $environment = @{
        ASPNETCORE_ENVIRONMENT = 'Development'
        ASPNETCORE_URLS = "https://localhost:$($case.Port)"
        ConnectionStrings__RemoteTest =
            "Server=(localdb)\MSSQLLocalDB;Database=$CustomerDatabase;Integrated Security=true;TrustServerCertificate=true"
        CustomerInternalServiceAuthentication__InProgressRecoverySeconds = '1'
        CustomerInternalServiceAuthentication__InProgressLeaseRenewalFraction = $case.Fraction
        CustomerInternalServiceAuthentication__InProgressLeaseSafetyMarginMilliseconds = $case.SafetyMargin
    }
    $process = Start-Process dotnet -PassThru -WorkingDirectory $solution `
        -ArgumentList @(
            'run', '--project', $project, '-c', 'Release', '--no-build',
            '--no-launch-profile'
        ) `
        -Environment $environment `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr

    try {
        Wait-Process -Id $process.Id -Timeout 30 -ErrorAction Stop
    }
    catch {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        throw "Invalid lease startup case '$($case.Name)' did not terminate."
    }

    $process.Refresh()
    $output = ((Get-Content $stdout, $stderr -Raw -ErrorAction SilentlyContinue) -join "`n")
    if ($process.ExitCode -eq 0 -or
        $output -notmatch 'OptionsValidationException' -or
        $output -notmatch 'computed renewal interval') {
        throw "Invalid lease startup case '$($case.Name)' did not fail with the expected validation."
    }

    $passed++
    Write-Output "[PASS] $($case.Name)"
}

Write-Output "Lease startup validation summary: $passed/$($cases.Count) passed"
