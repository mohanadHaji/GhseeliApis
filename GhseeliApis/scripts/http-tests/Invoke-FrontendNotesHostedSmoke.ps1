#requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$CustomerBaseUri = [Uri]'https://ghseelicustomer.runasp.net'
$BusinessBaseUri = [Uri]'https://ghseelibusiness.runasp.net'
$ExpectedHosts = @(
    'ghseelicustomer.runasp.net',
    'ghseelibusiness.runasp.net'
)
$DemoDataPath = Join-Path (
    Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
) 'demo-data\frontend-demo-data.json'

$script:Passed = 0
$script:Failed = 0
$script:Skipped = 0
$script:VehicleId = $null
$script:FavouriteBusinessId = $null
$script:FavouriteOriginallyPresent = $false
$script:FavouriteRestoreHeaders = $null
$script:ReviewRestore = $null

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Invoke-Check([string]$Name, [scriptblock]$Action) {
    try {
        . $Action
        $script:Passed++
        Write-Host "PASS $Name"
    }
    catch {
        $script:Failed++
        Write-Host "FAIL $Name - $($_.Exception.Message)"
    }
}

function Skip-Check([string]$Name, [string]$Reason) {
    $script:Skipped++
    Write-Host "SKIP $Name - $Reason"
}

function ConvertTo-JsonBody([object]$Value) {
    $json = $Value | ConvertTo-Json -Depth 30 -Compress
    Assert-True ($json -notmatch '(?i)"(isDemo|dataPartition|partition)"\s*:') `
        'A forbidden public partition-selection property was detected.'
    return $json
}

function Get-ResponseText([object]$Response) {
    if ($null -eq $Response.Content) { return '' }
    if ($Response.Content -is [byte[]]) {
        return [Text.Encoding]::UTF8.GetString($Response.Content)
    }
    return [string]$Response.Content
}

function Read-Json([object]$Response) {
    $text = Get-ResponseText $Response
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return $text | ConvertFrom-Json -Depth 50
}

function Invoke-HostedRequest(
    [Uri]$BaseUri,
    [ValidateSet('GET','POST','PUT','DELETE')]
    [string]$Method,
    [string]$Path,
    [hashtable]$Headers = @{},
    [object]$Body = $null) {
    Assert-True ($ExpectedHosts -contains $BaseUri.Host) 'Unapproved host.'
    Assert-True ($BaseUri.Scheme -eq 'https' -and $BaseUri.IsDefaultPort) `
        'Only default-port HTTPS is allowed.'
    Assert-True ($Path.StartsWith('/')) 'Request paths must be absolute paths.'
    $uri = [Uri]::new($BaseUri, $Path)
    Assert-True ($uri.Host -eq $BaseUri.Host -and $uri.Scheme -eq 'https') `
        'Request URI escaped the approved host.'

    $correlation = 'hosted-smoke-' + [guid]::NewGuid().ToString('N')
    $requestHeaders = @{
        Accept = 'application/json'
        'X-Correlation-Id' = $correlation
    }
    foreach ($entry in $Headers.GetEnumerator()) {
        $requestHeaders[$entry.Key] = $entry.Value
    }
    $parameters = @{
        Uri = $uri
        Method = $Method
        Headers = $requestHeaders
        SkipHttpErrorCheck = $true
        MaximumRedirection = 0
        TimeoutSec = 45
    }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = ConvertTo-JsonBody $Body
    }
    try {
        $response = Invoke-WebRequest @parameters
    }
    catch {
        throw "Hosted request failed without exposing its response body."
    }
    $response | Add-Member -NotePropertyName SmokeCorrelation `
        -NotePropertyValue $correlation
    return $response
}

function Assert-Status([object]$Response, [int[]]$Expected) {
    if ($Expected -contains [int]$Response.StatusCode) { return }

    $code = ''
    $fieldNames = ''
    try {
        $problem = Read-Json $Response
        if ($null -ne $problem.PSObject.Properties['code']) {
            $code = [string]$problem.code
        }
        if ($null -ne $problem.PSObject.Properties['fieldErrors']) {
            $fieldNames = @($problem.fieldErrors.PSObject.Properties.Name) -join ','
        }
    }
    catch {
    }
    $diagnostic = if ([string]::IsNullOrWhiteSpace($code)) {
        ''
    }
    elseif ([string]::IsNullOrWhiteSpace($fieldNames)) {
        " Problem code: $code."
    }
    else {
        " Problem code: $code. Invalid fields: $fieldNames."
    }
    throw "Unexpected HTTP status $([int]$Response.StatusCode); expected $($Expected -join '/').$diagnostic"
}

function Get-DemoStableId([string]$Scope, [Guid]$Value) {
    $bytes = [Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes(
            "ghseeli-demo-$Scope-$($Value.ToString('N'))"))
    return [Guid]::new([byte[]]$bytes[0..15])
}

function Assert-Headers([object]$Response, [switch]$NoStore) {
    $correlation = @($Response.Headers['X-Correlation-Id']) -join ','
    $contentTypeOptions = @($Response.Headers['X-Content-Type-Options']) -join ','
    $referrerPolicy = @($Response.Headers['Referrer-Policy']) -join ','
    $contentSecurityPolicy =
        @($Response.Headers['Content-Security-Policy']) -join ','
    $strictTransportSecurity =
        @($Response.Headers['Strict-Transport-Security']) -join ','
    $cacheControl = @($Response.Headers['Cache-Control']) -join ','

    Assert-True ($correlation -eq $Response.SmokeCorrelation) `
        'Correlation ID was not echoed.'
    Assert-True ($contentTypeOptions -eq 'nosniff') `
        'X-Content-Type-Options is missing.'
    Assert-True ($referrerPolicy -eq 'no-referrer') `
        'Referrer-Policy is missing.'
    Assert-True (-not [string]::IsNullOrWhiteSpace($contentSecurityPolicy)) `
        'Content-Security-Policy is missing.'
    Assert-True (-not [string]::IsNullOrWhiteSpace($strictTransportSecurity)) `
        'Strict-Transport-Security is missing.'
    if ($NoStore) {
        Assert-True ($cacheControl -match 'no-store') `
            'Cache-Control no-store is missing.'
    }
}

function Assert-SafeBody([object]$Response, [string[]]$Secrets) {
    $text = Get-ResponseText $Response
    foreach ($secret in $Secrets) {
        if (-not [string]::IsNullOrWhiteSpace($secret)) {
            Assert-True (-not $text.Contains($secret, [StringComparison]::Ordinal)) `
                'A credential or token was reflected in a response.'
        }
    }
    Assert-True ($text -notmatch '(?i)(connection string|server=|password=|stack trace|exception:)') `
        'A response appears to expose internal or sensitive diagnostics.'
}

function Assert-Problem([object]$Response, [int]$Status, [string]$Code) {
    Assert-Status $Response @($Status)
    Assert-Headers $Response -NoStore
    $problem = Read-Json $Response
    Assert-True ($problem.code -eq $Code) "Expected problem code '$Code'."
    $correlationId = if ($null -ne $problem.PSObject.Properties['correlationId']) {
        [string]$problem.correlationId
    }
    elseif ($null -ne $problem.PSObject.Properties['traceId']) {
        [string]$problem.traceId
    }
    else {
        ''
    }
    Assert-True (-not [string]::IsNullOrWhiteSpace($correlationId)) `
        'Problem correlation identifier is missing.'
}

function New-AuthHeaders([string]$Token, [string]$DeviceToken = '') {
    $headers = @{ Authorization = "Bearer $Token" }
    if (-not [string]::IsNullOrWhiteSpace($DeviceToken)) {
        $headers['X-Device-Token'] = $DeviceToken
    }
    return $headers
}

function New-PricingBody([object]$Business, [object]$Offering) {
    $branch = if ($null -ne $Offering.branch) {
        @($Business.branches |
            Where-Object sourceId -eq $Offering.branch.sourceId)[0]
    }
    else {
        @($Business.branches)[0]
    }
    Assert-True ($null -ne $branch) `
        'No matching branch is available for direct pricing.'
    $selections = @()
    foreach ($group in @($Offering.addonGroups)) {
        $selectedIds = [Collections.Generic.HashSet[string]]::new()
        foreach ($choice in @($group.choices |
            Where-Object defaultQuantity -gt 0)) {
            $selections += @{
                addonChoiceSourceId = [string]$choice.sourceId
                quantity = [int]$choice.defaultQuantity
            }
            [void]$selectedIds.Add([string]$choice.sourceId)
        }
        $required = [Math]::Max(0, [int]$group.minimumSelections - $selectedIds.Count)
        foreach ($choice in @($group.choices |
            Where-Object { -not $selectedIds.Contains([string]$_.sourceId) } |
            Select-Object -First $required)) {
            $selections += @{
                addonChoiceSourceId = [string]$choice.sourceId
                quantity = 1
            }
        }
    }
    return @{
        businessSourceId = [string]$Business.sourceId
        branchSourceId = [string]$branch.sourceId
        requestedSlotStartUtc = [DateTimeOffset]::UtcNow.AddDays(7).
            ToString('yyyy-MM-ddT10:00:00Z')
        vehicle = @{
            vehicleType = 'Sedan'
            imageUrl = 'https://example.test/hosted-smoke/vehicle.png'
            licensePlate = 'DEMO-SMOKE'
            make = 'Demo'
            model = 'Smoke'
            color = 'White'
        }
        location = @{
            addressLine = 'Hosted smoke fictional address'
            city = 'Demo'
            area = 'Demo'
            latitude = if ($null -ne $branch.latitude) { $branch.latitude } else { 31.9 }
            longitude = if ($null -ne $branch.longitude) { $branch.longitude } else { 35.2 }
        }
        items = @(@{
            offeringSourceId = [string]$Offering.sourceId
            selections = $selections
        })
    }
}

if (-not (Test-Path -LiteralPath $DemoDataPath -PathType Leaf)) {
    throw 'The canonical frontend Demo data file is missing.'
}
$demo = Get-Content -LiteralPath $DemoDataPath -Raw -Encoding UTF8 |
    ConvertFrom-Json -Depth 100
Assert-True ($demo.metadata.datasetType -eq 'demo') `
    'Unexpected Demo dataset type.'

$maya = $demo.customers | Where-Object email -eq 'maya.demo@example.test'
$owner = $demo.businessUsers |
    Where-Object email -eq 'owner.sparkle@example.test'
Assert-True ($null -ne $maya -and $null -ne $owner) `
    'Required deterministic Demo identities are missing.'
$demoDevice = @($maya.devices | Where-Object isActive)[0]
$demoCompany = @($demo.companies |
    Where-Object id -eq $owner.companyId)[0]
Assert-True ($null -ne $demoDevice -and $null -ne $demoCompany) `
    'Required deterministic Demo device/company is missing.'

$script:customerToken = $null
$script:businessToken = $null
$script:reviewToken = $null
$script:reviewDeviceToken = $null
$script:demoCatalog = $null
$script:anonymousCatalog = $null
$allSecrets = @(
    [string]$maya.password,
    [string]$owner.password,
    [string]$demoDevice.token
)

try {
    Invoke-Check 'Customer database health' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET '/api/Health/db'
        Assert-Status $r @(200)
        Assert-Headers $r
    }
    Invoke-Check 'Business database health' {
        $r = Invoke-HostedRequest $BusinessBaseUri GET '/api/health'
        Assert-Status $r @(200)
        Assert-Headers $r
    }

    $customerSwagger = $null
    Invoke-Check 'Customer Swagger changed contracts' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET '/swagger/v1/swagger.json'
        Assert-Status $r @(200)
        Assert-Headers $r
        $customerSwagger = Read-Json $r
        foreach ($path in @(
            '/api/Vehicles',
            '/api/Vehicles/{id}',
            '/api/Vehicles/my-vehicles',
            '/api/v1/configuration',
            '/api/v1/catalog/categories',
            '/api/v1/catalog/businesses',
            '/api/v1/catalog/businesses/{id}',
            '/api/v1/catalog/businesses/{id}/offerings',
            '/api/v1/catalog/offerings/{id}',
            '/api/v1/catalog/businesses/{businessId}/favourite',
            '/api/v1/catalog/businesses/{businessId}/reviews',
            '/api/v1/bookings/{bookingId}/review',
            '/api/v1/banners',
            '/api/v1/admin/banners',
            '/api/v1/catalog/businesses/availability-search',
            '/api/v1/catalog/businesses/{businessId}/branches/{branchId}/available-slots',
            '/api/v1/pricing/reprice'
        )) {
            Assert-True ($null -ne $customerSwagger.paths.PSObject.Properties[$path]) `
                "Customer Swagger is missing $path."
        }
        $vehicleType = $customerSwagger.components.schemas.PSObject.Properties |
            Where-Object { $_.Name -match 'VehicleType$' } |
            Select-Object -First 1 -ExpandProperty Value
        Assert-True (@($vehicleType.enum).Count -eq 5) `
            'Customer Swagger does not publish all five vehicle types.'
    }
    Invoke-Check 'Business Swagger changed contracts' {
        $r = Invoke-HostedRequest $BusinessBaseUri GET '/swagger/v1/swagger.json'
        Assert-Status $r @(200)
        Assert-Headers $r
        $swagger = Read-Json $r
        foreach ($path in @(
            '/api/v1/business/company',
            '/api/v1/business/catalog/categories',
            '/api/v1/business/catalog/categories/{categoryId}',
            '/api/v1/business/catalog/offerings',
            '/api/v1/business/catalog/offerings/{offeringId}',
            '/api/v1/internal/catalog/snapshot',
            '/api/v1/internal/appointments/availability-discovery'
        )) {
            Assert-True ($null -ne $swagger.paths.PSObject.Properties[$path]) `
                "Business Swagger is missing $path."
        }
    }

    Invoke-Check 'Customer Demo login' {
        $r = Invoke-HostedRequest $CustomerBaseUri POST '/api/Auth/login' @{} @{
            email = $maya.email
            password = $maya.password
        }
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $body = Read-Json $r
        Assert-True ($body.userId -eq $maya.id) 'Customer login user ID mismatch.'
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$body.token)) `
            'Customer login token is missing.'
        $script:customerToken = [string]$body.token
    }
    Invoke-Check 'Business Demo login' {
        $r = Invoke-HostedRequest $BusinessBaseUri POST `
            '/api/v1/business/auth/login' @{} @{
                email = $owner.email
                password = $owner.password
            }
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $body = Read-Json $r
        Assert-True ($body.userId -eq $owner.id) 'Business login user ID mismatch.'
        Assert-True ($body.companyId -eq $owner.companyId) `
            'Business login company ID mismatch.'
        $script:businessToken = [string]$body.token
    }
    $allSecrets += @($customerToken, $businessToken)

    Invoke-Check 'Anonymous public configuration not configured' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET '/api/v1/configuration'
        Assert-Problem $r 503 'configuration_unavailable'
        Assert-SafeBody $r $allSecrets
        Assert-True ((Read-Json $r).detail -eq 'لم يتم تكوين إعدادات التطبيق بعد.') `
            'Missing configuration detail does not say that configuration is not configured yet.'
    }
    Invoke-Check 'Anonymous public categories' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            '/api/v1/catalog/categories?language=ar'
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        Assert-SafeBody $r $allSecrets
        Assert-True ($null -ne (Read-Json $r).categories) `
            'Categories collection is missing.'
    }
    Invoke-Check 'Anonymous business list and projections' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            '/api/v1/catalog/businesses?language=ar'
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $script:anonymousCatalog = Read-Json $r
        Assert-True ($null -ne $anonymousCatalog.PSObject.Properties['businesses']) `
            'Production business collection is missing.'
        foreach ($business in @($anonymousCatalog.businesses)) {
            Assert-True ($business.isFavourite -eq $false) `
                'Anonymous business projection exposed a favourite.'
            Assert-True ($null -ne $business.averageRating -and
                $null -ne $business.ratingCount) 'Rating projection is missing.'
        }
    }
    Invoke-Check 'Anonymous business search' {
        $term = if (@($anonymousCatalog.businesses).Count -gt 0) {
            [Uri]::EscapeDataString([string]$anonymousCatalog.businesses[0].name)
        }
        else {
            '__hosted_smoke_no_match__'
        }
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            "/api/v1/catalog/businesses?search=$term&language=ar"
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $items = @((Read-Json $r).businesses)
        if (@($anonymousCatalog.businesses).Count -gt 0) {
            Assert-True ($items.Count -ge 1) `
                'Search did not return its exact source business.'
        }
        else {
            Assert-True ($items.Count -eq 0) `
                'Search returned a business from an empty Production catalog.'
        }
    }
    Invoke-Check 'Anonymous top-five businesses' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            '/api/v1/catalog/businesses?top=5&language=ar'
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        Assert-True (@((Read-Json $r).businesses).Count -le 5) `
            'Top-five returned more than five businesses.'
    }
    Invoke-Check 'Anonymous invalid top contract' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            '/api/v1/catalog/businesses?top=6'
        Assert-Problem $r 400 'catalog_top_invalid'
    }

    $productionBusiness = @($anonymousCatalog.businesses) |
        Select-Object -First 1
    $script:productionOfferings = $null
    if ($null -ne $productionBusiness) {
        Invoke-Check 'Anonymous business detail' {
            $r = Invoke-HostedRequest $CustomerBaseUri GET `
                "/api/v1/catalog/businesses/$($productionBusiness.id)?language=ar"
            Assert-Status $r @(200)
            Assert-Headers $r -NoStore
            $body = Read-Json $r
            Assert-True ($body.business.id -eq $productionBusiness.id) `
                'Business detail ID mismatch.'
        }
        Invoke-Check 'Anonymous business offerings' {
            $r = Invoke-HostedRequest $CustomerBaseUri GET `
                "/api/v1/catalog/businesses/$($productionBusiness.id)/offerings?language=ar"
            Assert-Status $r @(200)
            Assert-Headers $r -NoStore
            $script:productionOfferings = Read-Json $r
            Assert-True (@($productionOfferings.offerings).Count -gt 0) `
                'Production business has no offerings.'
        }
        Invoke-Check 'Anonymous offering detail' {
            $offering = $productionOfferings.offerings[0]
            $r = Invoke-HostedRequest $CustomerBaseUri GET `
                "/api/v1/catalog/offerings/$($offering.id)?language=ar"
            Assert-Status $r @(200)
            Assert-Headers $r -NoStore
            Assert-True ((Read-Json $r).offering.id -eq $offering.id) `
                'Offering detail ID mismatch.'
        }
    }
    else {
        Skip-Check 'Anonymous business detail' 'Production catalog is empty.'
        Skip-Check 'Anonymous business offerings' 'Production catalog is empty.'
        Skip-Check 'Anonymous offering detail' 'Production catalog is empty.'
    }
    Invoke-Check 'Anonymous public banners' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET '/api/v1/banners'
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $items = @((Read-Json $r).banners)
        for ($i = 1; $i -lt $items.Count; $i++) {
            Assert-True ($items[$i - 1].displayOrder -le $items[$i].displayOrder) `
                'Production banners are not ordered by displayOrder.'
        }
    }
    if ($null -ne $productionBusiness) {
        Invoke-Check 'Anonymous public reviews privacy' {
            $r = Invoke-HostedRequest $CustomerBaseUri GET `
                "/api/v1/catalog/businesses/$($productionBusiness.id)/reviews?page=1&pageSize=20"
            Assert-Status $r @(200)
            Assert-Headers $r -NoStore
            $text = Get-ResponseText $r
            Assert-True ($text -notmatch
                '(?i)"(bookingId|bookingReferenceId|customerId|userId|email|phone|address|licensePlate|rowVersion)"\s*:') `
                'Public reviews exposed a private field.'
        }
    }
    else {
        Skip-Check 'Anonymous public reviews privacy' 'Production catalog is empty.'
    }
    Invoke-Check 'Anonymous advisory availability search' {
        $body = @{
            vehicleType = 'Sedan'
            date = [DateTime]::UtcNow.Date.AddDays(7).ToString('yyyy-MM-dd')
            preferredLocalTime = '10:30:00'
        }
        $r = Invoke-HostedRequest $CustomerBaseUri POST `
            '/api/v1/catalog/businesses/availability-search?language=ar' @{} $body
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $result = Read-Json $r
        Assert-True ($result.isAdvisory -eq $true -and
            $null -ne $result.results) `
            'Anonymous availability contract is incomplete.'
    }
    if ($null -ne $productionOfferings) {
        Invoke-Check 'Anonymous direct pricing is stateless' {
            $body = New-PricingBody $productionBusiness `
                $productionOfferings.offerings[0]
            $r = Invoke-HostedRequest $CustomerBaseUri POST `
                '/api/v1/pricing/reprice?language=ar' @{} $body
            Assert-Status $r @(200)
            Assert-Headers $r -NoStore
            $quote = Read-Json $r
            Assert-True ($quote.intent.vehicle.vehicleType -eq 'Sedan' -and
                $quote.intent.vehicle.imageUrl -eq
                    'https://example.test/hosted-smoke/vehicle.png' -and
                $quote.pricing.grandTotal -gt 0) `
                'Anonymous direct-pricing contract is incomplete.'
        }
    }
    else {
        Skip-Check 'Anonymous direct pricing is stateless' `
            'Production catalog is empty.'
    }

    $demoHeaders = @{ 'X-Device-Token' = [string]$demoDevice.token }
    Invoke-Check 'Demo-device categories exact metadata' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            '/api/v1/catalog/categories?language=ar' $demoHeaders
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $categories = @((Read-Json $r).categories)
        Assert-True ($categories.Count -eq $demo.companies.categories.Count) `
            'Demo category count mismatch.'
        foreach ($expected in $demo.companies.categories) {
            $actual = $categories | Where-Object sourceId -eq $expected.id
            Assert-True ($null -ne $actual) "Demo category $($expected.id) is missing."
            Assert-True ($actual.imageUrl -eq $expected.imageUrl -and
                $actual.colorHex -eq $expected.colorHex) `
                "Demo category metadata mismatch for $($expected.id)."
        }
    }
    Invoke-Check 'Demo-device five businesses and seeded projections' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            '/api/v1/catalog/businesses?language=ar' $demoHeaders
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $script:demoCatalog = Read-Json $r
        Assert-True (@($demoCatalog.businesses).Count -eq 5) `
            'Demo catalog must contain exactly five businesses.'
        $expectedIds = @($demo.companies.id | Sort-Object)
        $actualIds = @($demoCatalog.businesses.sourceId | Sort-Object)
        Assert-True (($expectedIds -join ',') -eq ($actualIds -join ',')) `
            'Demo business source IDs differ from the canonical dataset.'
    }
    Invoke-Check 'Demo JWT favourites and ratings' {
        $headers = New-AuthHeaders $customerToken $demoDevice.token
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            '/api/v1/catalog/businesses?language=ar' $headers
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $businesses = @((Read-Json $r).businesses)
        $expected = @($demo.favourites |
            Where-Object customerId -eq $maya.id |
            ForEach-Object companyId)
        foreach ($business in $businesses) {
            Assert-True ($business.isFavourite -eq
                ($expected -contains [string]$business.sourceId)) `
                "Favourite projection mismatch for $($business.sourceId)."
            Assert-True ($business.ratingCount -ge 0 -and
                $business.averageRating -ge 0) 'Invalid rating projection.'
        }
    }
    Invoke-Check 'Demo search and top' {
        $term = [Uri]::EscapeDataString([string]$demoCompany.nameAr)
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            "/api/v1/catalog/businesses?search=$term&top=5&language=ar" $demoHeaders
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $items = @((Read-Json $r).businesses)
        Assert-True ($items.Count -eq 1 -and
            $items[0].sourceId -eq $demoCompany.id) `
            'Demo search/top did not isolate the expected company.'
    }

    $demoBusiness = $demoCatalog.businesses |
        Where-Object sourceId -eq $demoCompany.id
    $script:demoOfferingContext = $null
    Invoke-Check 'Demo business detail projection' {
        $headers = New-AuthHeaders $customerToken $demoDevice.token
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            "/api/v1/catalog/businesses/$($demoBusiness.id)?language=ar" $headers
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $body = Read-Json $r
        Assert-True ($body.business.sourceId -eq $demoCompany.id) `
            'Demo detail source ID mismatch.'
    }
    Invoke-Check 'Demo offerings metadata' {
        $headers = New-AuthHeaders $customerToken $demoDevice.token
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            "/api/v1/catalog/businesses/$($demoBusiness.id)/offerings?language=ar" $headers
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $script:demoOfferingContext = Read-Json $r
        Assert-True (@($demoOfferingContext.offerings).Count -eq 4) `
            'Demo company must contain exactly four offerings.'
        $first = $demoOfferingContext.offerings |
            Where-Object sourceId -eq $demoCompany.offerings[0].id
        Assert-True ($first.qualifier -eq $demoCompany.offerings[0].qualifierAr -and
            $first.badgeCode -eq 'MostRequested') `
            'Offering qualifier/badge metadata mismatch.'
    }
    Invoke-Check 'Demo public reviews privacy and aggregates' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            "/api/v1/catalog/businesses/$($demoBusiness.id)/reviews?page=1&pageSize=50" `
            $demoHeaders
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $body = Read-Json $r
        Assert-True ($body.totalCount -eq $body.ratingCount) `
            'Review count and rating count differ.'
        Assert-True ([string]$r.Content -notmatch
            '(?i)"(bookingId|bookingReferenceId|customerId|userId|email|phone|address|licensePlate|rowVersion)"\s*:') `
            'Demo public reviews exposed a private field.'
    }
    Invoke-Check 'Demo banners active-only and ordered' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET '/api/v1/banners' $demoHeaders
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $actual = @((Read-Json $r).banners)
        $expected = @($demo.banners | Where-Object isActive |
            Sort-Object displayOrder, id)
        Assert-True ($actual.Count -eq $expected.Count) 'Active banner count mismatch.'
        Assert-True (($actual.id -join ',') -eq ($expected.id -join ',')) `
            'Demo banners are not in deterministic active order.'
        Assert-True ([string]$r.Content -notmatch
            '(?i)"(isActive|createdAtUtc|updatedAtUtc|rowVersion)"\s*:') `
            'Public banners exposed admin-only fields.'
    }

    $availabilityCompany = $demo.companies |
        Where-Object {
            @($_.offerings |
                Where-Object { $_.durationMinutes % 30 -eq 0 }).Count -gt 0
        } |
        Select-Object -First 1
    Assert-True ($null -ne $availabilityCompany) `
        'No Demo company has an offering aligned to the seeded slot duration.'
    $availabilityDate = [DateTime]::UtcNow.Date.AddDays(7).ToString('yyyy-MM-dd')
    $availabilityBody = @{
        vehicleType = 'Sedan'
        date = $availabilityDate
        preferredLocalTime = '10:30:00'
        categoryId = [string]$availabilityCompany.categories[0].id
        latitude = [double]$availabilityCompany.branches[0].latitude
        longitude = [double]$availabilityCompany.branches[0].longitude
    }
    $script:availability = $null
    $script:availabilityBusiness = $null
    $script:availabilityOffering = $null
    Invoke-Check 'Demo advisory availability search capacity' {
        $r = Invoke-HostedRequest $CustomerBaseUri POST `
            '/api/v1/catalog/businesses/availability-search?language=ar' `
            $demoHeaders $availabilityBody
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $script:availability = Read-Json $r
        Assert-True ($availability.isAdvisory -eq $true) `
            'Availability response is not marked advisory.'
        Assert-True (@($availability.results).Count -gt 0) `
            'Demo availability search returned no configured slots.'
        foreach ($result in $availability.results) {
            Assert-True ($result.configuredCapacity -ge 1) `
                'Configured capacity is invalid.'
            Assert-True ($result.remainingCapacity -ge 0 -and
                $result.remainingCapacity -le $result.configuredCapacity) `
                'Remaining capacity is invalid.'
        }
    }
    Invoke-Check 'Demo detailed authoritative slots' {
        $result = @($availability.results)[0]
        $catalogBusiness = $demoCatalog.businesses |
            Where-Object id -eq $result.businessId
        $catalogBranch = $catalogBusiness.branches |
            Where-Object id -eq $result.branchId
        $sourceCompany = $demo.companies |
            Where-Object id -eq $catalogBusiness.sourceId
        $offeringsResponse = Invoke-HostedRequest $CustomerBaseUri GET `
            "/api/v1/catalog/businesses/$($catalogBusiness.id)/offerings?branchId=$($catalogBranch.id)&language=ar" `
            $demoHeaders
        Assert-Status $offeringsResponse @(200)
        $offerings = @((Read-Json $offeringsResponse).offerings)
        $catalogOffering = @($offerings |
            Where-Object { $_.durationMinutes % 30 -eq 0 })[0]
        Assert-True ($null -ne $catalogOffering) `
            'The selected availability branch has no slot-aligned offering.'
        $slotSelections = @()
        foreach ($group in @($catalogOffering.addonGroups)) {
            foreach ($choice in @($group.choices |
                Where-Object defaultQuantity -gt 0)) {
                $slotSelections += @{
                    addonChoiceId = [string]$choice.id
                    quantity = [int]$choice.defaultQuantity
                }
            }
        }
        $body = @{
            date = $availabilityDate
            expectedCatalogVersion = [long]$sourceCompany.catalogVersion
            customerLocation = @{
                latitude = [double]$sourceCompany.branches[0].latitude
                longitude = [double]$sourceCompany.branches[0].longitude
            }
            items = @(@{
                offeringId = [string]$catalogOffering.id
                selectedAddons = $slotSelections
            })
            includeUnavailable = $true
            language = 'ar'
        }
        $r = Invoke-HostedRequest $CustomerBaseUri POST `
            "/api/v1/catalog/businesses/$($catalogBusiness.id)/branches/$($catalogBranch.id)/available-slots" `
            $demoHeaders $body
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $slots = @((Read-Json $r).slots)
        Assert-True ($slots.Count -gt 0) 'Detailed slots are empty.'
        Assert-True ($slots[0].configuredCapacity -ge $slots[0].remainingCapacity) `
            'Detailed slot capacity is invalid.'
        $script:availabilityBusiness = $catalogBusiness
        $script:availabilityOffering = $catalogOffering
    }
    Invoke-Check 'Demo direct pricing vehicle projection' {
        $body = New-PricingBody $availabilityBusiness $availabilityOffering
        $r = Invoke-HostedRequest $CustomerBaseUri POST `
            '/api/v1/pricing/reprice?language=ar' $demoHeaders $body
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $quote = Read-Json $r
        Assert-True ($quote.intent.vehicle.vehicleType -eq 'Sedan' -and
            $quote.intent.vehicle.imageUrl -eq
                'https://example.test/hosted-smoke/vehicle.png') `
            'Direct pricing did not preserve vehicle type/image.'
        Assert-True ($quote.pricing.grandTotal -gt 0) 'Direct price is not positive.'
    }

    $invalidDevice = 'invalid-hosted-smoke-token'
    $invalidCases = @(
        @{ Name='configuration'; Method='GET'; Path='/api/v1/configuration' },
        @{ Name='categories'; Method='GET'; Path='/api/v1/catalog/categories' },
        @{ Name='business list'; Method='GET'; Path='/api/v1/catalog/businesses' },
        @{ Name='business detail'; Method='GET'; Path="/api/v1/catalog/businesses/$($demoBusiness.id)" },
        @{ Name='business offerings'; Method='GET'; Path="/api/v1/catalog/businesses/$($demoBusiness.id)/offerings" },
        @{ Name='offering detail'; Method='GET'; Path="/api/v1/catalog/offerings/$($demoOfferingContext.offerings[0].id)" },
        @{ Name='banners'; Method='GET'; Path='/api/v1/banners' },
        @{ Name='reviews'; Method='GET'; Path="/api/v1/catalog/businesses/$($demoBusiness.id)/reviews" },
        @{ Name='availability'; Method='POST'; Path='/api/v1/catalog/businesses/availability-search'; Body=$availabilityBody },
        @{ Name='detailed slots'; Method='POST'; Path="/api/v1/catalog/businesses/$($demoBusiness.id)/branches/$($demoBusiness.branches[0].id)/available-slots"; Body=@{} },
        @{ Name='pricing'; Method='POST'; Path='/api/v1/pricing/reprice'; Body=@{} }
    )
    foreach ($case in $invalidCases) {
        Invoke-Check "Invalid optional device rejects $($case.Name)" {
            $caseBody = if ($case.ContainsKey('Body')) { $case.Body } else { $null }
            $r = Invoke-HostedRequest $CustomerBaseUri $case.Method $case.Path `
                @{ 'X-Device-Token' = $invalidDevice } $caseBody
            Assert-Problem $r 401 'device_token_invalid'
        }
    }

    Invoke-Check 'Anonymous vehicle auth rejection' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET '/api/Vehicles/my-vehicles'
        Assert-Status $r @(401)
        Assert-Headers $r -NoStore
    }
    Invoke-Check 'Anonymous favourite auth rejection' {
        $r = Invoke-HostedRequest $CustomerBaseUri PUT `
            "/api/v1/catalog/businesses/$($demoBusiness.id)/favourite"
        Assert-Problem $r 401 'customer_authentication_required'
    }
    Invoke-Check 'Anonymous owned-review auth rejection' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            "/api/v1/bookings/$([guid]::Empty)/review"
        Assert-Status $r @(401)
        Assert-Headers $r -NoStore
    }
    Invoke-Check 'Customer token rejected by Business API' {
        $r = Invoke-HostedRequest $BusinessBaseUri GET '/api/v1/business/company' `
            (New-AuthHeaders $customerToken)
        Assert-Status $r @(401)
        Assert-Headers $r -NoStore
    }
    Invoke-Check 'Business token rejected by Customer API' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET '/api/Vehicles/my-vehicles' `
            (New-AuthHeaders $businessToken)
        Assert-Status $r @(401)
        Assert-Headers $r -NoStore
    }

    Invoke-Check 'Customer JWT and Demo device match' {
        $r = Invoke-HostedRequest $CustomerBaseUri GET `
            '/api/v1/catalog/businesses?top=5' `
            (New-AuthHeaders $customerToken $demoDevice.token)
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        Assert-True (@((Read-Json $r).businesses).Count -eq 5) `
            'Matching Demo credentials did not select Demo.'
    }
    $productionIdentity = $demo.PSObject.Properties['productionIdentity']
    if ($null -ne $productionIdentity -and
        -not [string]::IsNullOrWhiteSpace(
            [string]$productionIdentity.Value.deviceToken)) {
        Invoke-Check 'Deterministic cross-partition mismatch rejection' {
            $r = Invoke-HostedRequest $CustomerBaseUri GET `
                '/api/v1/catalog/businesses' `
                (New-AuthHeaders $customerToken `
                    ([string]$productionIdentity.Value.deviceToken))
            Assert-Problem $r 403 'data_partition_mismatch'
        }
    }
    else {
        Skip-Check 'Deterministic cross-partition mismatch rejection' `
            'Canonical dataset contains no deterministic Production identity.'
    }

    Invoke-Check 'Demo vehicle create' {
        $plate = 'DSM-' + [guid]::NewGuid().ToString('N').Substring(0, 10)
        $r = Invoke-HostedRequest $CustomerBaseUri POST '/api/Vehicles' `
            (New-AuthHeaders $customerToken) @{
                make = 'Hosted'
                model = 'Smoke'
                year = '2026'
                licensePlate = $plate
                color = 'White'
                vehicleType = 'Sedan'
                imageUrl = 'https://example.test/hosted-smoke/vehicle-created.png'
            }
        Assert-Status $r @(201)
        Assert-Headers $r -NoStore
        $vehicle = Read-Json $r
        $script:VehicleId = [string]$vehicle.id
        Assert-True ($vehicle.vehicleType -eq 'Sedan') 'Created vehicle type mismatch.'
    }
    Invoke-Check 'Demo vehicle list/read' {
        $headers = New-AuthHeaders $customerToken
        $list = Invoke-HostedRequest $CustomerBaseUri GET `
            '/api/Vehicles/my-vehicles' $headers
        Assert-Status $list @(200)
        Assert-Headers $list -NoStore
        Assert-True (@(Read-Json $list |
            Where-Object id -eq $script:VehicleId).Count -eq 1) `
            'Created vehicle is absent from the owner list.'
        $read = Invoke-HostedRequest $CustomerBaseUri GET `
            "/api/Vehicles/$($script:VehicleId)" $headers
        Assert-Status $read @(200)
        Assert-True ((Read-Json $read).id -eq $script:VehicleId) `
            'Created vehicle detail mismatch.'
    }
    Invoke-Check 'Demo vehicle update' {
        $r = Invoke-HostedRequest $CustomerBaseUri PUT `
            "/api/Vehicles/$($script:VehicleId)" `
            (New-AuthHeaders $customerToken) @{
                make = 'Hosted'
                model = 'Smoke Updated'
                year = '2026'
                licensePlate = 'DSM-UPDATED'
                color = 'Blue'
                vehicleType = 'Suv5Seater'
                imageUrl = 'https://example.test/hosted-smoke/vehicle-updated.png'
            }
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $vehicle = Read-Json $r
        Assert-True ($vehicle.vehicleType -eq 'Suv5Seater' -and
            $vehicle.imageUrl.EndsWith('vehicle-updated.png')) `
            'Updated vehicle type/image mismatch.'
    }
    Invoke-Check 'Demo vehicle delete/read-after-delete' {
        $headers = New-AuthHeaders $customerToken
        $delete = Invoke-HostedRequest $CustomerBaseUri DELETE `
            "/api/Vehicles/$($script:VehicleId)" $headers
        Assert-Status $delete @(204)
        Assert-Headers $delete -NoStore
        $read = Invoke-HostedRequest $CustomerBaseUri GET `
            "/api/Vehicles/$($script:VehicleId)" $headers
        Assert-Status $read @(404)
        $script:VehicleId = $null
    }

    $script:FavouriteBusinessId = [string]$demoBusiness.id
    $script:FavouriteOriginallyPresent =
        @($demo.favourites | Where-Object {
            $_.customerId -eq $maya.id -and
            $_.companyId -eq $demoBusiness.sourceId
        }).Count -eq 1
    $script:FavouriteRestoreHeaders =
        New-AuthHeaders $customerToken $demoDevice.token
    Invoke-Check 'Favourite idempotent PUT' {
        1..2 | ForEach-Object {
            $r = Invoke-HostedRequest $CustomerBaseUri PUT `
                "/api/v1/catalog/businesses/$($script:FavouriteBusinessId)/favourite" `
                $script:FavouriteRestoreHeaders
            Assert-Status $r @(204)
            Assert-Headers $r -NoStore
        }
    }
    Invoke-Check 'Favourite idempotent DELETE' {
        1..2 | ForEach-Object {
            $r = Invoke-HostedRequest $CustomerBaseUri DELETE `
                "/api/v1/catalog/businesses/$($script:FavouriteBusinessId)/favourite" `
                $script:FavouriteRestoreHeaders
            Assert-Status $r @(204)
            Assert-Headers $r -NoStore
        }
    }
    Invoke-Check 'Favourite seeded state restored' {
        $method = if ($script:FavouriteOriginallyPresent) { 'PUT' } else { 'DELETE' }
        $r = Invoke-HostedRequest $CustomerBaseUri $method `
            "/api/v1/catalog/businesses/$($script:FavouriteBusinessId)/favourite" `
            $script:FavouriteRestoreHeaders
        Assert-Status $r @(204)
        $script:FavouriteBusinessId = $null
    }

    $seedReview = @($demo.reviews)[0]
    $reviewBooking = $demo.bookings |
        Where-Object customerReferenceId -eq $seedReview.bookingReferenceId
    $reviewCustomer = $demo.customers |
        Where-Object id -eq $seedReview.customerId
    $reviewDevice = @($reviewCustomer.devices | Where-Object isActive)[0]
    if ($null -ne $seedReview -and $null -ne $reviewBooking -and
        $null -ne $reviewCustomer -and $null -ne $reviewDevice) {
        $reviewBookingId = Get-DemoStableId `
            'customer-booking' ([Guid]$reviewBooking.customerReferenceId)
        Invoke-Check 'Review owner Demo login' {
            $r = Invoke-HostedRequest $CustomerBaseUri POST '/api/Auth/login' @{} @{
                email = $reviewCustomer.email
                password = $reviewCustomer.password
            }
            Assert-Status $r @(200)
            $script:reviewToken = [string](Read-Json $r).token
            $script:reviewDeviceToken = [string]$reviewDevice.token
            Assert-True (-not [string]::IsNullOrWhiteSpace($reviewToken)) `
                'Review owner token is missing.'
        }
        Invoke-Check 'Owned review read and exact fixture' {
            $headers = New-AuthHeaders $reviewToken $reviewDeviceToken
            $r = Invoke-HostedRequest $CustomerBaseUri GET `
                "/api/v1/bookings/$reviewBookingId/review" `
                $headers
            Assert-Status $r @(200)
            Assert-Headers $r -NoStore
            $owned = Read-Json $r
            Assert-True ($owned.rating -eq $seedReview.rating -and
                $owned.comment -eq $seedReview.comment) `
                'Owned review differs from the canonical fixture.'
            $script:ReviewRestore = @{
                BookingId = [string]$reviewBookingId
                Rating = [int]$owned.rating
                Comment = $owned.comment
                RowVersion = [string]$owned.rowVersion
                Headers = $headers
            }
        }
        Invoke-Check 'Owned review reversible update' {
            $newRating = if ($script:ReviewRestore.Rating -eq 5) { 4 } else { 5 }
            $r = Invoke-HostedRequest $CustomerBaseUri PUT `
                "/api/v1/bookings/$($script:ReviewRestore.BookingId)/review" `
                $script:ReviewRestore.Headers @{
                    rating = $newRating
                    comment = 'Hosted smoke reversible Demo review.'
                    expectedRowVersion = $script:ReviewRestore.RowVersion
                }
            Assert-Status $r @(200)
            Assert-Headers $r -NoStore
            $updated = Read-Json $r
            Assert-True ($updated.rating -eq $newRating) 'Review update did not apply.'
            $script:ReviewRestore.RowVersion = [string]$updated.rowVersion
        }
        Invoke-Check 'Owned review exact restore' {
            $r = Invoke-HostedRequest $CustomerBaseUri PUT `
                "/api/v1/bookings/$($script:ReviewRestore.BookingId)/review" `
                $script:ReviewRestore.Headers @{
                    rating = $script:ReviewRestore.Rating
                    comment = $script:ReviewRestore.Comment
                    expectedRowVersion = $script:ReviewRestore.RowVersion
                }
            Assert-Status $r @(200)
            $restored = Read-Json $r
            Assert-True ($restored.rating -eq $seedReview.rating -and
                $restored.comment -eq $seedReview.comment) `
                'Review was not restored exactly.'
            $script:ReviewRestore = $null
        }
    }
    else {
        Skip-Check 'Owned review reversible update' `
            'No deterministic seeded review owner fixture exists.'
    }

    Invoke-Check 'Business owner company read' {
        $r = Invoke-HostedRequest $BusinessBaseUri GET `
            '/api/v1/business/company' (New-AuthHeaders $businessToken)
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $company = Read-Json $r
        Assert-True ($company.id -eq $demoCompany.id) `
            'Business owner company ID mismatch.'
    }
    Invoke-Check 'Business owner category metadata read' {
        $r = Invoke-HostedRequest $BusinessBaseUri GET `
            '/api/v1/business/catalog/categories' `
            (New-AuthHeaders $businessToken)
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        $categories = @(Read-Json $r)
        Assert-True ($categories.Count -eq $demoCompany.categories.Count) `
            'Business category count mismatch.'
        foreach ($expected in $demoCompany.categories) {
            $actual = $categories | Where-Object id -eq $expected.id
            Assert-True ($actual.imageUrl -eq $expected.imageUrl -and
                $actual.colorHex -eq $expected.colorHex) `
                "Business category metadata mismatch for $($expected.id)."
        }
    }
    Invoke-Check 'Business owner offering metadata read' {
        $r = Invoke-HostedRequest $BusinessBaseUri GET `
            '/api/v1/business/catalog/offerings' `
            (New-AuthHeaders $businessToken)
        Assert-Status $r @(200)
        Assert-Headers $r -NoStore
        Assert-True (@(Read-Json $r).Count -eq $demoCompany.offerings.Count) `
            'Business offering count mismatch.'
    }

    $demoAdmin = $demo.customers | Where-Object {
        $_.PSObject.Properties['role'] -and $_.role -eq 'Admin'
    } | Select-Object -First 1
    if ($null -eq $demoAdmin) {
        Skip-Check 'Banner Admin mutation' `
            'Canonical dataset contains no deterministic Demo Customer Admin.'
    }
    else {
        Skip-Check 'Banner Admin mutation' `
            'Admin identity exists but mutation is intentionally outside this read-safe smoke.'
    }
}
finally {
    if ($null -ne $script:ReviewRestore) {
        try {
            $current = Invoke-HostedRequest $CustomerBaseUri GET `
                "/api/v1/bookings/$($script:ReviewRestore.BookingId)/review" `
                $script:ReviewRestore.Headers
            if ([int]$current.StatusCode -eq 200) {
                $currentBody = Read-Json $current
                [void](Invoke-HostedRequest $CustomerBaseUri PUT `
                    "/api/v1/bookings/$($script:ReviewRestore.BookingId)/review" `
                    $script:ReviewRestore.Headers @{
                        rating = $script:ReviewRestore.Rating
                        comment = $script:ReviewRestore.Comment
                        expectedRowVersion = [string]$currentBody.rowVersion
                    })
            }
        }
        catch {
            $script:Failed++
            Write-Host 'FAIL finally review restore - cleanup could not be verified.'
        }
    }
    if ($null -ne $script:FavouriteBusinessId -and
        $null -ne $script:FavouriteRestoreHeaders) {
        try {
            $method = if ($script:FavouriteOriginallyPresent) { 'PUT' } else { 'DELETE' }
            [void](Invoke-HostedRequest $CustomerBaseUri $method `
                "/api/v1/catalog/businesses/$($script:FavouriteBusinessId)/favourite" `
                $script:FavouriteRestoreHeaders)
        }
        catch {
            $script:Failed++
            Write-Host 'FAIL finally favourite restore - cleanup could not be verified.'
        }
    }
    if ($null -ne $script:VehicleId -and $null -ne $customerToken) {
        try {
            [void](Invoke-HostedRequest $CustomerBaseUri DELETE `
                "/api/Vehicles/$($script:VehicleId)" `
                (New-AuthHeaders $customerToken))
        }
        catch {
            $script:Failed++
            Write-Host 'FAIL finally vehicle cleanup - cleanup could not be verified.'
        }
    }
}

$total = $script:Passed + $script:Failed + $script:Skipped
Write-Host "TOTAL checks=$total passed=$($script:Passed) failed=$($script:Failed) skipped=$($script:Skipped)"
if ($script:Failed -gt 0) { exit 1 }
exit 0
