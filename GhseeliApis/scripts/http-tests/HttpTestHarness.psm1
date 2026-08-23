#requires -Version 5.1

Set-StrictMode -Version Latest

function Test-IsSimpleValue {
    param(
        [object]$Value
    )

    if ($null -eq $Value) {
        return $true
    }

    return (
        $Value -is [string] -or
        $Value -is [char] -or
        $Value -is [bool] -or
        $Value -is [byte] -or
        $Value -is [sbyte] -or
        $Value -is [int16] -or
        $Value -is [uint16] -or
        $Value -is [int32] -or
        $Value -is [uint32] -or
        $Value -is [int64] -or
        $Value -is [uint64] -or
        $Value -is [single] -or
        $Value -is [double] -or
        $Value -is [decimal] -or
        $Value -is [datetime] -or
        $Value -is [guid] -or
        $Value -is [uri]
    )
}

function ConvertTo-PlainValue {
    param(
        [object]$Value
    )

    if (Test-IsSimpleValue -Value $Value) {
        return $Value
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in $Value.Keys) {
            $result[[string]$key] = ConvertTo-PlainValue -Value $Value[$key]
        }

        return $result
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        $items = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Value) {
            [void]$items.Add((ConvertTo-PlainValue -Value $item))
        }

        return ,($items.ToArray())
    }

    if ($Value -is [psobject]) {
        $result = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            if (-not $property.IsGettable) {
                continue
            }

            $result[$property.Name] = ConvertTo-PlainValue -Value $property.Value
        }

        return $result
    }

    return $Value
}

function Get-NormalizedArray {
    param(
        [object]$Value
    )

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [string] -or $Value -is [System.Collections.IDictionary]) {
        return @($Value)
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        return @($Value)
    }

    return @($Value)
}

function Get-StringArray {
    param(
        [object]$Value
    )

    $items = @(Get-NormalizedArray -Value $Value)
    $result = New-Object System.Collections.Generic.List[string]

    foreach ($item in $items) {
        if ($null -eq $item) {
            continue
        }

        [void]$result.Add([string]$item)
    }

    return $result.ToArray()
}

function Resolve-ExistingPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [string[]]$BaseDirectories = @()
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        if (-not (Test-Path -LiteralPath $Path)) {
            throw "File not found: $Path"
        }

        return (Resolve-Path -LiteralPath $Path).Path
    }

    $candidateDirectories = New-Object System.Collections.Generic.List[string]
    [void]$candidateDirectories.Add((Get-Location).Path)

    foreach ($baseDirectory in $BaseDirectories) {
        if ([string]::IsNullOrWhiteSpace($baseDirectory)) {
            continue
        }

        [void]$candidateDirectories.Add($baseDirectory)
    }

    foreach ($directory in $candidateDirectories) {
        $candidatePath = Join-Path -Path $directory -ChildPath $Path
        if (Test-Path -LiteralPath $candidatePath) {
            return (Resolve-Path -LiteralPath $candidatePath).Path
        }
    }

    throw "File not found: $Path"
}

function Resolve-OutputPath {
    param(
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$DefaultDirectory
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        $fileName = 'results-{0}.json' -f (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
        return Join-Path -Path $DefaultDirectory -ChildPath $fileName
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path -Path (Get-Location).Path -ChildPath $Path
}

function Read-JsonDocument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    try {
        $rawContent = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
    }
    catch {
        throw "Failed to read JSON file '$Path': $($_.Exception.Message)"
    }

    try {
        $parsed = $rawContent | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Invalid JSON in '$Path': $($_.Exception.Message)"
    }

    return ConvertTo-PlainValue -Value $parsed
}

function Try-GetNamedValue {
    param(
        [object]$Object,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [ref]$Value
    )

    if ($null -eq $Object) {
        return $false
    }

    if ($Object -is [System.Collections.IDictionary]) {
        foreach ($key in $Object.Keys) {
            if ([string]::Equals([string]$key, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
                $Value.Value = $Object[$key]
                return $true
            }
        }

        return $false
    }

    if ($Object -is [psobject]) {
        foreach ($property in $Object.PSObject.Properties) {
            if (-not $property.IsGettable) {
                continue
            }

            if ([string]::Equals($property.Name, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
                $Value.Value = $property.Value
                return $true
            }
        }

        return $false
    }

    return $false
}

function Get-ObjectPropertyValue {
    param(
        [object]$Object,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [switch]$Required
    )

    $value = $null
    if (Try-GetNamedValue -Object $Object -Name $Name -Value ([ref]$value)) {
        return $value
    }

    if ($Required) {
        throw "Missing required property '$Name'."
    }

    return $null
}

function Test-ObjectProperty {
    param(
        [object]$Object,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $unused = $null
    return (Try-GetNamedValue -Object $Object -Name $Name -Value ([ref]$unused))
}

function Get-ObjectEntries {
    param(
        [object]$Object
    )

    if ($null -eq $Object) {
        return @()
    }

    if ($Object -is [System.Collections.IDictionary]) {
        $entries = New-Object System.Collections.Generic.List[object]
        foreach ($key in $Object.Keys) {
            [void]$entries.Add([pscustomobject]@{
                    Name  = [string]$key
                    Value = $Object[$key]
                })
        }

        return $entries.ToArray()
    }

    if ($Object -is [psobject]) {
        $entries = New-Object System.Collections.Generic.List[object]
        foreach ($property in $Object.PSObject.Properties) {
            if (-not $property.IsGettable) {
                continue
            }

            [void]$entries.Add([pscustomobject]@{
                    Name  = $property.Name
                    Value = $property.Value
                })
        }

        return $entries.ToArray()
    }

    throw 'Expected an object with named properties.'
}

function Get-UtcNowWithOffset {
    param(
        [string]$OffsetToken
    )

    $utcNow = (Get-Date).ToUniversalTime()
    if ([string]::IsNullOrWhiteSpace($OffsetToken)) {
        return $utcNow
    }

    if ($OffsetToken -notmatch '^(?<sign>[+-])(?<value>\d+)(?<unit>[smhd])$') {
        throw "Unsupported generated time offset '$OffsetToken'. Use +30s, -10m, +2h, or +1d."
    }

    $magnitude = [int]$matches['value']
    $offset = switch ($matches['unit']) {
        's' { [System.TimeSpan]::FromSeconds($magnitude) }
        'm' { [System.TimeSpan]::FromMinutes($magnitude) }
        'h' { [System.TimeSpan]::FromHours($magnitude) }
        'd' { [System.TimeSpan]::FromDays($magnitude) }
        default { throw "Unsupported generated time offset unit '$($matches['unit'])'." }
    }

    if ($matches['sign'] -eq '-') {
        $offset = -$offset
    }

    return $utcNow.Add($offset)
}

function Resolve-GeneratedTimestampToken {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    $parts = $Token.Split(':')
    $format = 'yyyyMMddHHmmssfff'
    $offsetToken = $null

    if ($parts.Length -gt 2) {
        $candidateOffset = $parts[$parts.Length - 1]
        if ($candidateOffset -match '^[+-]\d+[smhd]$') {
            $offsetToken = $candidateOffset
            if ($parts.Length -gt 3) {
                $format = ($parts[2..($parts.Length - 2)] -join ':')
            }
        }
        else {
            $format = ($parts[2..($parts.Length - 1)] -join ':')
        }
    }

    return (Get-UtcNowWithOffset -OffsetToken $offsetToken).ToString($format)
}

function Resolve-GeneratedIso8601Token {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    $parts = $Token.Split(':')
    if ($parts.Length -gt 3) {
        throw "Unsupported ISO-8601 generator token '$Token'. Use {{gen:iso8601}} or {{gen:iso8601:+10m}}."
    }

    $offsetToken = if ($parts.Length -eq 3) { $parts[2] } else { $null }
    return (Get-UtcNowWithOffset -OffsetToken $offsetToken).ToString('o')
}

function New-GeneratedNonce {
    return ([guid]::NewGuid().ToString('N'))
}

function Resolve-TemplateTokenValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Token,

        [Parameter(Mandatory = $true)]
        [hashtable]$Variables
    )

    $trimmedToken = $Token.Trim()

    if ($trimmedToken -match '^var:(.+)$') {
        $name = $matches[1].Trim()
        if (-not $Variables.ContainsKey($name)) {
            throw "Variable '$name' is not defined."
        }

        return $Variables[$name]
    }

    if ($trimmedToken -match '^env:(.+)$') {
        $name = $matches[1].Trim()
        $value = [System.Environment]::GetEnvironmentVariable($name)
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "Environment variable '$name' is not set."
        }

        return $value
    }

    if ($trimmedToken -match '^gen:guid$') {
        return ([guid]::NewGuid().ToString())
    }

    if ($trimmedToken -match '^gen:nonce$') {
        return New-GeneratedNonce
    }

    if ($trimmedToken -match '^gen:timestamp(?::.+)?$') {
        return Resolve-GeneratedTimestampToken -Token $trimmedToken
    }

    if ($trimmedToken -match '^gen:iso8601(?::.+)?$') {
        return Resolve-GeneratedIso8601Token -Token $trimmedToken
    }

    if ($Variables.ContainsKey($trimmedToken)) {
        return $Variables[$trimmedToken]
    }

    throw "Unsupported template token '$trimmedToken'. Supported forms: {{var:name}}, {{env:NAME}}, {{gen:guid}}, {{gen:nonce}}, {{gen:timestamp[:format][:offset]}}, and {{gen:iso8601[:offset]}}."
}

function Resolve-TemplateString {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Template,

        [Parameter(Mandatory = $true)]
        [hashtable]$Variables
    )

    $pattern = [regex]'\{\{([^{}]+)\}\}'
    $matches = $pattern.Matches($Template)

    if ($matches.Count -eq 0) {
        return $Template
    }

    $builder = New-Object System.Text.StringBuilder
    $currentIndex = 0

    foreach ($match in $matches) {
        [void]$builder.Append($Template.Substring($currentIndex, $match.Index - $currentIndex))
        $resolvedValue = Resolve-TemplateTokenValue -Token $match.Groups[1].Value -Variables $Variables
        [void]$builder.Append([string]$resolvedValue)
        $currentIndex = $match.Index + $match.Length
    }

    [void]$builder.Append($Template.Substring($currentIndex))
    return $builder.ToString()
}

function Resolve-TemplatedValue {
    param(
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [hashtable]$Variables
    )

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [string]) {
        $singleTokenMatch = [regex]::Match($Value, '^\{\{([^{}]+)\}\}$')
        if ($singleTokenMatch.Success) {
            return Resolve-TemplateTokenValue -Token $singleTokenMatch.Groups[1].Value -Variables $Variables
        }

        return Resolve-TemplateString -Template $Value -Variables $Variables
    }

    if (Test-IsSimpleValue -Value $Value) {
        return $Value
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($entry in Get-ObjectEntries -Object $Value) {
            $result[$entry.Name] = Resolve-TemplatedValue -Value $entry.Value -Variables $Variables
        }

        return $result
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        $items = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Value) {
            [void]$items.Add((Resolve-TemplatedValue -Value $item -Variables $Variables))
        }

        return ,($items.ToArray())
    }

    return $Value
}

function Resolve-VariableAssignments {
    param(
        [object]$Assignments,

        [Parameter(Mandatory = $true)]
        [hashtable]$Variables
    )

    if ($null -eq $Assignments) {
        return
    }

    foreach ($entry in Get-ObjectEntries -Object $Assignments) {
        $Variables[$entry.Name] = Resolve-TemplatedValue -Value $entry.Value -Variables $Variables
    }
}

function Compute-Sha256Hex {
    param(
        [byte[]]$Bytes
    )

    if ($null -eq $Bytes) {
        $Bytes = New-Object byte[] 0
    }

    return [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA256]::Create().ComputeHash($Bytes)).
        Replace('-', '').
        ToLowerInvariant()
}

function Compute-HmacSha256Hex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Secret,

        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $secretBytes = [System.Text.Encoding]::UTF8.GetBytes($Secret)
    $valueBytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $hmac = New-Object System.Security.Cryptography.HMACSHA256 -ArgumentList (, $secretBytes)

    try {
        return [System.BitConverter]::ToString($hmac.ComputeHash($valueBytes)).
            Replace('-', '').
            ToLowerInvariant()
    }
    finally {
        $hmac.Dispose()
    }
}

function Get-UriQueryParameters {
    param(
        [Parameter(Mandatory = $true)]
        [uri]$RequestUri
    )

    if ([string]::IsNullOrWhiteSpace($RequestUri.Query) -or $RequestUri.Query -eq '?') {
        return @()
    }

    $pairs = New-Object System.Collections.Generic.List[object]
    foreach ($segment in $RequestUri.Query.TrimStart('?').Split('&')) {
        if ([string]::IsNullOrWhiteSpace($segment)) {
            continue
        }

        $parts = $segment.Split('=', 2)
        $rawKey = if ($parts.Length -ge 1) { $parts[0] } else { '' }
        $rawValue = if ($parts.Length -eq 2) { $parts[1] } else { '' }

        [void]$pairs.Add([pscustomobject]@{
                Key   = [System.Net.WebUtility]::UrlDecode($rawKey)
                Value = [System.Net.WebUtility]::UrlDecode($rawValue)
            })
    }

    return $pairs.ToArray()
}

function Get-NormalizedPathAndQuery {
    param(
        [Parameter(Mandatory = $true)]
        [uri]$RequestUri
    )

    $normalizedPath = if ([string]::IsNullOrWhiteSpace($RequestUri.AbsolutePath)) {
        '/'
    }
    else {
        $RequestUri.AbsolutePath
    }

    $sortedPairs = @(Get-UriQueryParameters -RequestUri $RequestUri |
        Sort-Object -Property `
            @{ Expression = { [string]$_.Key } ; Descending = $false }, `
            @{ Expression = { [string]$_.Value } ; Descending = $false })

    $normalizedPairs = @($sortedPairs | ForEach-Object {
            '{0}={1}' -f `
                [uri]::EscapeDataString([string]$_.Key),
                [uri]::EscapeDataString([string]$_.Value)
        })

    if ($normalizedPairs.Count -eq 0) {
        return $normalizedPath
    }

    return '{0}?{1}' -f $normalizedPath, ($normalizedPairs -join '&')
}

function Build-InternalCanonicalRequest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceId,

        [Parameter(Mandatory = $true)]
        [string]$Method,

        [Parameter(Mandatory = $true)]
        [uri]$RequestUri,

        [Parameter(Mandatory = $true)]
        [string]$Timestamp,

        [Parameter(Mandatory = $true)]
        [string]$Nonce,

        [string]$IdempotencyKey,

        [Parameter(Mandatory = $true)]
        [string]$BodyHashHex
    )

    return [string]::Join(
        "`n",
        @(
            'ghseeli-hmac-sha256-v1',
            $ServiceId,
            $Method.ToUpperInvariant(),
            (Get-NormalizedPathAndQuery -RequestUri $RequestUri),
            $Timestamp,
            $Nonce,
            $(if ($null -eq $IdempotencyKey) { '' } else { $IdempotencyKey }),
            $BodyHashHex
        ))
}

function Apply-InternalAuthHeaders {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Scenario,

        [object]$InternalAuth,

        [Parameter(Mandatory = $true)]
        [hashtable]$ResolvedHeaders,

        [Parameter(Mandatory = $true)]
        [uri]$RequestUri,

        [Parameter(Mandatory = $true)]
        [string]$Method,

        [string]$BodyContent,

        [Parameter(Mandatory = $true)]
        [hashtable]$Variables
    )

    if ($null -eq $InternalAuth) {
        return
    }

    $scenarioId = [string](Get-ObjectPropertyValue -Object $Scenario -Name 'id' -Required)
    $resolvedInternalAuth = Resolve-TemplatedValue -Value $InternalAuth -Variables $Variables
    if (-not ($resolvedInternalAuth -is [System.Collections.IDictionary])) {
        throw "Scenario '$scenarioId' must define internalAuth as an object."
    }

    $serviceIdHeaderName = [string](Get-ObjectPropertyValue -Object $resolvedInternalAuth -Name 'serviceIdHeaderName')
    if ([string]::IsNullOrWhiteSpace($serviceIdHeaderName)) {
        $serviceIdHeaderName = 'X-Ghseeli-Service-Id'
    }

    $timestampHeaderName = [string](Get-ObjectPropertyValue -Object $resolvedInternalAuth -Name 'timestampHeaderName')
    if ([string]::IsNullOrWhiteSpace($timestampHeaderName)) {
        $timestampHeaderName = 'X-Ghseeli-Timestamp'
    }

    $nonceHeaderName = [string](Get-ObjectPropertyValue -Object $resolvedInternalAuth -Name 'nonceHeaderName')
    if ([string]::IsNullOrWhiteSpace($nonceHeaderName)) {
        $nonceHeaderName = 'X-Ghseeli-Nonce'
    }

    $signatureHeaderName = [string](Get-ObjectPropertyValue -Object $resolvedInternalAuth -Name 'signatureHeaderName')
    if ([string]::IsNullOrWhiteSpace($signatureHeaderName)) {
        $signatureHeaderName = 'X-Ghseeli-Signature'
    }

    $idempotencyHeaderName = [string](Get-ObjectPropertyValue -Object $resolvedInternalAuth -Name 'idempotencyHeaderName')
    if ([string]::IsNullOrWhiteSpace($idempotencyHeaderName)) {
        $idempotencyHeaderName = 'Idempotency-Key'
    }

    $secretEnvName = [string](Get-ObjectPropertyValue -Object $resolvedInternalAuth -Name 'secretEnv' -Required)
    $secret = [System.Environment]::GetEnvironmentVariable($secretEnvName)
    if ([string]::IsNullOrWhiteSpace($secret)) {
        throw "Environment variable '$secretEnvName' is not set."
    }

    $existingHeaderValue = $null
    if (-not (Try-GetNamedValue -Object $ResolvedHeaders -Name $serviceIdHeaderName -Value ([ref]$existingHeaderValue))) {
        $ResolvedHeaders[$serviceIdHeaderName] = [string](Get-ObjectPropertyValue -Object $resolvedInternalAuth -Name 'serviceId' -Required)
    }

    $existingHeaderValue = $null
    if (-not (Try-GetNamedValue -Object $ResolvedHeaders -Name $timestampHeaderName -Value ([ref]$existingHeaderValue))) {
        $timestampValue = [string](Get-ObjectPropertyValue -Object $resolvedInternalAuth -Name 'timestamp')
        if ([string]::IsNullOrWhiteSpace($timestampValue)) {
            $timestampValue = (Get-Date).ToUniversalTime().ToString('o')
        }

        $ResolvedHeaders[$timestampHeaderName] = $timestampValue
    }

    $existingHeaderValue = $null
    if (-not (Try-GetNamedValue -Object $ResolvedHeaders -Name $nonceHeaderName -Value ([ref]$existingHeaderValue))) {
        $nonceValue = [string](Get-ObjectPropertyValue -Object $resolvedInternalAuth -Name 'nonce')
        if ([string]::IsNullOrWhiteSpace($nonceValue)) {
            $nonceValue = New-GeneratedNonce
        }

        $ResolvedHeaders[$nonceHeaderName] = $nonceValue
    }

    $existingHeaderValue = $null
    if (-not (Try-GetNamedValue -Object $ResolvedHeaders -Name $signatureHeaderName -Value ([ref]$existingHeaderValue))) {
        $bodyBytes = if ($null -eq $BodyContent) {
            New-Object byte[] 0
        }
        else {
            [System.Text.Encoding]::UTF8.GetBytes($BodyContent)
        }

        $idempotencyKeyValue = $null
        if (Try-GetNamedValue -Object $ResolvedHeaders -Name $idempotencyHeaderName -Value ([ref]$idempotencyKeyValue)) {
            $idempotencyKeyValue = [string]$idempotencyKeyValue
        }

        $canonicalRequest = Build-InternalCanonicalRequest `
            -ServiceId ([string]$ResolvedHeaders[$serviceIdHeaderName]) `
            -Method $Method `
            -RequestUri $RequestUri `
            -Timestamp ([string]$ResolvedHeaders[$timestampHeaderName]) `
            -Nonce ([string]$ResolvedHeaders[$nonceHeaderName]) `
            -IdempotencyKey $idempotencyKeyValue `
            -BodyHashHex (Compute-Sha256Hex -Bytes $bodyBytes)

        $ResolvedHeaders[$signatureHeaderName] = Compute-HmacSha256Hex `
            -Secret $secret `
            -Value $canonicalRequest
    }
}

function Test-IsLocalBaseUrl {
    param(
        [Parameter(Mandatory = $true)]
        [uri]$Uri
    )

    if (-not $Uri.IsAbsoluteUri) {
        return $false
    }

    if ([string]::Equals($Uri.Host, 'localhost', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $parsedAddress = $null
    if ([System.Net.IPAddress]::TryParse($Uri.Host, [ref]$parsedAddress)) {
        return [System.Net.IPAddress]::IsLoopback($parsedAddress)
    }

    return $false
}

function Assert-LocalBaseUrl {
    param(
        [Parameter(Mandatory = $true)]
        [uri]$BaseUri,

        [switch]$AllowNonLocal
    )

    if ($AllowNonLocal) {
        return
    }

    if (-not (Test-IsLocalBaseUrl -Uri $BaseUri)) {
        throw "Refusing to call non-local base URL '$($BaseUri.AbsoluteUri)'. Re-run with -AllowNonLocal to override the local/test-only safety guard."
    }
}

function Get-JsonPathTokens {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'JSON path must not be empty.'
    }

    $tokens = New-Object System.Collections.Generic.List[object]
    $index = 0

    if ($Path[0] -eq '$') {
        $index = 1
    }

    while ($index -lt $Path.Length) {
        $currentCharacter = $Path[$index]

        if ($currentCharacter -eq '.') {
            $index++
            if ($index -ge $Path.Length) {
                throw "Invalid JSON path '$Path'."
            }

            $startIndex = $index
            while ($index -lt $Path.Length -and $Path[$index] -ne '.' -and $Path[$index] -ne '[') {
                $index++
            }

            $name = $Path.Substring($startIndex, $index - $startIndex)
            if ([string]::IsNullOrWhiteSpace($name)) {
                throw "Invalid JSON path '$Path'."
            }

            [void]$tokens.Add([pscustomobject]@{
                    Kind  = 'Property'
                    Value = $name
                })

            continue
        }

        if ($currentCharacter -eq '[') {
            $index++
            if ($index -ge $Path.Length) {
                throw "Invalid JSON path '$Path'."
            }

            $tokenCharacter = $Path[$index]
            if ($tokenCharacter -eq "'" -or $tokenCharacter -eq '"') {
                $quoteCharacter = $tokenCharacter
                $index++
                $builder = New-Object System.Text.StringBuilder

                while ($index -lt $Path.Length) {
                    $character = $Path[$index]
                    if ($character -eq '\') {
                        if ($index + 1 -ge $Path.Length) {
                            throw "Invalid escape sequence in JSON path '$Path'."
                        }

                        $escapedCharacter = $Path[$index + 1]
                        [void]$builder.Append($escapedCharacter)
                        $index += 2
                        continue
                    }

                    if ($character -eq $quoteCharacter) {
                        break
                    }

                    [void]$builder.Append($character)
                    $index++
                }

                if ($index -ge $Path.Length -or $Path[$index] -ne $quoteCharacter) {
                    throw "Unterminated quoted property in JSON path '$Path'."
                }

                $index++
                if ($index -ge $Path.Length -or $Path[$index] -ne ']') {
                    throw "Invalid JSON path '$Path'."
                }

                $index++
                [void]$tokens.Add([pscustomobject]@{
                        Kind  = 'Property'
                        Value = $builder.ToString()
                    })

                continue
            }

            $startIndex = $index
            while ($index -lt $Path.Length -and $Path[$index] -ne ']') {
                $index++
            }

            if ($index -ge $Path.Length) {
                throw "Invalid JSON path '$Path'."
            }

            $indexText = $Path.Substring($startIndex, $index - $startIndex).Trim()
            $index++

            if ($indexText -notmatch '^\d+$') {
                throw "Only numeric array indexes are supported in JSON path '$Path'."
            }

            [void]$tokens.Add([pscustomobject]@{
                    Kind  = 'Index'
                    Value = [int]$indexText
                })

            continue
        }

        $startIndex = $index
        while ($index -lt $Path.Length -and $Path[$index] -ne '.' -and $Path[$index] -ne '[') {
            $index++
        }

        $name = $Path.Substring($startIndex, $index - $startIndex)
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw "Invalid JSON path '$Path'."
        }

        [void]$tokens.Add([pscustomobject]@{
                Kind  = 'Property'
                Value = $name
            })
    }

    return $tokens.ToArray()
}

function Get-JsonPathResult {
    param(
        [object]$InputObject,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $currentValue = $InputObject
    $tokens = Get-JsonPathTokens -Path $Path

    foreach ($token in $tokens) {
        if ($token.Kind -eq 'Property') {
            $nextValue = $null
            if (-not (Try-GetNamedValue -Object $currentValue -Name $token.Value -Value ([ref]$nextValue))) {
                return [pscustomobject]@{
                    Found = $false
                    Value = $null
                }
            }

            $currentValue = $nextValue
            continue
        }

        if ($currentValue -is [array]) {
            if ($token.Value -lt 0 -or $token.Value -ge $currentValue.Length) {
                return [pscustomobject]@{
                    Found = $false
                    Value = $null
                }
            }

            $currentValue = $currentValue[$token.Value]
            continue
        }

        if ($currentValue -is [System.Collections.IList]) {
            if ($token.Value -lt 0 -or $token.Value -ge $currentValue.Count) {
                return [pscustomobject]@{
                    Found = $false
                    Value = $null
                }
            }

            $currentValue = $currentValue[$token.Value]
            continue
        }

        return [pscustomobject]@{
            Found = $false
            Value = $null
        }
    }

    return [pscustomobject]@{
        Found = $true
        Value = $currentValue
    }
}

function New-HttpClient {
    Add-Type -AssemblyName System.Net.Http

    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $false

    $client = New-Object System.Net.Http.HttpClient -ArgumentList $handler
    $client.Timeout = [System.TimeSpan]::FromSeconds(100)

    return $client
}

function Add-HeadersToMap {
    param(
        [Parameter(Mandatory = $true)]
        [System.Net.Http.Headers.HttpHeaders]$Headers,

        [Parameter(Mandatory = $true)]
        [hashtable]$Target
    )

    foreach ($header in $Headers) {
        $Target[$header.Key] = @($header.Value)
    }
}

function Get-ResponseHeaderMap {
    param(
        [Parameter(Mandatory = $true)]
        [System.Net.Http.HttpResponseMessage]$Response
    )

    $headers = @{}
    Add-HeadersToMap -Headers $Response.Headers -Target $headers

    if ($null -ne $Response.Content) {
        Add-HeadersToMap -Headers $Response.Content.Headers -Target $headers
    }

    return $headers
}

function Try-GetHeaderValues {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$HeaderMap,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [ref]$Values
    )

    foreach ($key in $HeaderMap.Keys) {
        if ([string]::Equals([string]$key, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
            $Values.Value = @($HeaderMap[$key])
            return $true
        }
    }

    return $false
}

function Test-IsJsonContent {
    param(
        [string]$Body,
        [string]$ContentType
    )

    if (-not [string]::IsNullOrWhiteSpace($ContentType) -and $ContentType -match 'json') {
        return $true
    }

    if ([string]::IsNullOrWhiteSpace($Body)) {
        return $false
    }

    $trimmed = $Body.TrimStart()
    return $trimmed.StartsWith('{') -or $trimmed.StartsWith('[')
}

function Get-DefaultSensitiveFields {
    return @(
        'password',
        'confirmPassword',
        'token',
        'accessToken',
        'refreshToken',
        'secret',
        'signature',
        'apiKey',
        'authorization',
        'clientSecret'
    )
}

function Test-IsSensitiveFieldName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [string[]]$SensitiveFields = @()
    )

    foreach ($fieldName in $SensitiveFields) {
        if ([string]::Equals($Name, $fieldName, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return ($Name -match '(?i)(password|token|secret|signature|api[-_]?key|authorization|refresh)')
}

function Test-IsSensitiveHeaderName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    return ($Name -match '(?i)(authorization|signature|secret|api[-_]?key|token|cookie)')
}

function Protect-SensitiveData {
    param(
        [object]$Value,

        [string[]]$SensitiveFields = @()
    )

    if ($null -eq $Value) {
        return $null
    }

    if (Test-IsSimpleValue -Value $Value) {
        return $Value
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($entry in Get-ObjectEntries -Object $Value) {
            if (Test-IsSensitiveFieldName -Name $entry.Name -SensitiveFields $SensitiveFields) {
                $result[$entry.Name] = '[REDACTED]'
            }
            else {
                $result[$entry.Name] = Protect-SensitiveData -Value $entry.Value -SensitiveFields $SensitiveFields
            }
        }

        return $result
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        $items = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Value) {
            [void]$items.Add((Protect-SensitiveData -Value $item -SensitiveFields $SensitiveFields))
        }

        return $items.ToArray()
    }

    return $Value
}

function Protect-Headers {
    param(
        [hashtable]$Headers,

        [string[]]$SensitiveFields = @()
    )

    if ($null -eq $Headers) {
        return $null
    }

    $result = [ordered]@{}
    foreach ($key in $Headers.Keys) {
        if (Test-IsSensitiveHeaderName -Name ([string]$key)) {
            $result[[string]$key] = '[REDACTED]'
            continue
        }

        $values = @($Headers[$key])
        if ($values.Count -le 1) {
            $result[[string]$key] = if ($values.Count -eq 0) { $null } else { $values[0] }
            continue
        }

        $result[[string]$key] = $values
    }

    return $result
}

function Convert-BodyPreview {
    param(
        [string]$Body,
        [string]$ContentType,
        [string[]]$SensitiveFields = @()
    )

    if ([string]::IsNullOrWhiteSpace($Body)) {
        return $null
    }

    if ($Body.Length -gt 4000) {
        return '[body omitted; {0} chars]' -f $Body.Length
    }

    if (Test-IsJsonContent -Body $Body -ContentType $ContentType) {
        try {
            $parsed = $Body | ConvertFrom-Json -ErrorAction Stop
            $plainValue = ConvertTo-PlainValue -Value $parsed
            return Protect-SensitiveData -Value $plainValue -SensitiveFields $SensitiveFields
        }
        catch {
            return '[json preview unavailable; {0} chars]' -f $Body.Length
        }
    }

    return '[non-json body omitted; {0} chars]' -f $Body.Length
}

function ConvertTo-ComparisonText {
    param(
        [object]$Value
    )

    if ($null -eq $Value) {
        return '<null>'
    }

    if (Test-IsSimpleValue -Value $Value) {
        return [string]$Value
    }

    return ($Value | ConvertTo-Json -Depth 50 -Compress)
}

function Test-ValueEquals {
    param(
        [object]$Actual,
        [object]$Expected
    )

    if ($null -eq $Actual -and $null -eq $Expected) {
        return $true
    }

    if ($null -eq $Actual -or $null -eq $Expected) {
        return $false
    }

    if ((-not (Test-IsSimpleValue -Value $Actual)) -or (-not (Test-IsSimpleValue -Value $Expected))) {
        return ((ConvertTo-ComparisonText -Value $Actual) -eq (ConvertTo-ComparisonText -Value $Expected))
    }

    return ($Actual -eq $Expected)
}

function Add-Failure {
    param(
        [System.Management.Automation.AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Failures,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    [void]$Failures.Add($Message)
}

function Build-RequestBody {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Scenario,

        [Parameter(Mandatory = $true)]
        [hashtable]$Variables,

        [Parameter(Mandatory = $true)]
        [string]$ManifestDirectory
    )

    $jsonBody = Get-ObjectPropertyValue -Object $Scenario -Name 'jsonBody'
    $bodyFile = Get-ObjectPropertyValue -Object $Scenario -Name 'bodyFile'
    $repeatBody = Get-ObjectPropertyValue -Object $Scenario -Name 'repeatBody'

    $bodySourceCount = @($jsonBody, $bodyFile, $repeatBody | Where-Object { $null -ne $_ }).Count
    if ($bodySourceCount -gt 1) {
        throw "Scenario '$((Get-ObjectPropertyValue -Object $Scenario -Name 'id' -Required))' can define only one of jsonBody, bodyFile, or repeatBody."
    }

    if ($null -ne $jsonBody) {
        $resolvedBody = Resolve-TemplatedValue -Value $jsonBody -Variables $Variables
        return [pscustomobject]@{
            Content     = ($resolvedBody | ConvertTo-Json -Depth 100 -Compress)
            ContentType = 'application/json'
            ForceChunked = $false
        }
    }

    if ($null -ne $bodyFile) {
        $resolvedBodyFile = Resolve-ExistingPath -Path ([string]$bodyFile) -BaseDirectories @($ManifestDirectory)
        try {
            $rawBody = Get-Content -LiteralPath $resolvedBodyFile -Raw -ErrorAction Stop
        }
        catch {
            throw "Failed to read body file '$resolvedBodyFile': $($_.Exception.Message)"
        }

        $resolvedBody = Resolve-TemplateString -Template $rawBody -Variables $Variables
        $defaultContentType = $null
        if ([System.IO.Path]::GetExtension($resolvedBodyFile) -ieq '.json') {
            $defaultContentType = 'application/json'
        }

        return [pscustomobject]@{
            Content     = $resolvedBody
            ContentType = $defaultContentType
            ForceChunked = $false
        }
    }

    if ($null -ne $repeatBody) {
        $text = [string](Resolve-TemplatedValue -Value (Get-ObjectPropertyValue -Object $repeatBody -Name 'text' -Required) -Variables $Variables)
        $count = [int](Get-ObjectPropertyValue -Object $repeatBody -Name 'count' -Required)
        if ($count -lt 1 -or $count -gt 1000000) {
            throw "Scenario '$((Get-ObjectPropertyValue -Object $Scenario -Name 'id' -Required))' repeatBody count must be between 1 and 1000000."
        }

        return [pscustomobject]@{
            Content     = ($text * $count)
            ContentType = [string](Get-ObjectPropertyValue -Object $repeatBody -Name 'contentType')
            ForceChunked = $true
        }
    }

    return [pscustomobject]@{
        Content     = $null
        ContentType = $null
        ForceChunked = $false
    }
}

function Test-ScenarioExpectations {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Scenario,

        [Parameter(Mandatory = $true)]
        [hashtable]$Variables,

        [Parameter(Mandatory = $true)]
        [int]$StatusCode,

        [string]$ResponseContentType,

        [string]$ResponseBody,

        [hashtable]$ResponseHeaders,

        [object]$ResponseJson
    )

    $failures = New-Object System.Collections.Generic.List[string]
    $expect = Get-ObjectPropertyValue -Object $Scenario -Name 'expect' -Required
    $expectedStatus = [int](Resolve-TemplatedValue -Value (Get-ObjectPropertyValue -Object $expect -Name 'status' -Required) -Variables $Variables)

    if ($StatusCode -ne $expectedStatus) {
        Add-Failure -Failures $failures -Message ("Expected status {0} but received {1}." -f $expectedStatus, $StatusCode)
    }

    $expectedContentType = Get-ObjectPropertyValue -Object $expect -Name 'contentType'
    if ($null -ne $expectedContentType) {
        $resolvedContentType = [string](Resolve-TemplatedValue -Value $expectedContentType -Variables $Variables)
        if ([string]::IsNullOrWhiteSpace($ResponseContentType) -or -not $ResponseContentType.StartsWith($resolvedContentType, [System.StringComparison]::OrdinalIgnoreCase)) {
            Add-Failure -Failures $failures -Message ("Expected content type '{0}' but received '{1}'." -f $resolvedContentType, $ResponseContentType)
        }
    }

    $expectedHeaders = Get-ObjectPropertyValue -Object $expect -Name 'headers'
    if ($null -ne $expectedHeaders) {
        foreach ($headerExpectation in Get-ObjectEntries -Object $expectedHeaders) {
            $actualValues = $null
            $hasHeader = Try-GetHeaderValues -HeaderMap $ResponseHeaders -Name $headerExpectation.Name -Values ([ref]$actualValues)

            if (Test-IsSimpleValue -Value $headerExpectation.Value) {
                $expectedHeaderValue = [string](Resolve-TemplatedValue -Value $headerExpectation.Value -Variables $Variables)
                if (-not $hasHeader) {
                    Add-Failure -Failures $failures -Message ("Expected header '{0}' with value '{1}', but it was missing." -f $headerExpectation.Name, $expectedHeaderValue)
                    continue
                }

                $matches = $false
                foreach ($actualHeaderValue in @($actualValues)) {
                    if ([string]::Equals([string]$actualHeaderValue, $expectedHeaderValue, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $matches = $true
                        break
                    }
                }

                if (-not $matches) {
                    Add-Failure -Failures $failures -Message ("Expected header '{0}' to contain '{1}', but received '{2}'." -f $headerExpectation.Name, $expectedHeaderValue, (@($actualValues) -join ', '))
                }

                continue
            }

            $headerSpecification = $headerExpectation.Value
            $expectsExistence = Test-ObjectProperty -Object $headerSpecification -Name 'exists'
            if ($expectsExistence) {
                $requiredPresence = [bool](Resolve-TemplatedValue -Value (Get-ObjectPropertyValue -Object $headerSpecification -Name 'exists' -Required) -Variables $Variables)
                if ($requiredPresence -ne $hasHeader) {
                    Add-Failure -Failures $failures -Message ("Expected header '{0}' presence to be '{1}', but actual presence was '{2}'." -f $headerExpectation.Name, $requiredPresence, $hasHeader)
                }
            }

            if (Test-ObjectProperty -Object $headerSpecification -Name 'equals') {
                $expectedHeaderValue = [string](Resolve-TemplatedValue -Value (Get-ObjectPropertyValue -Object $headerSpecification -Name 'equals' -Required) -Variables $Variables)
                if (-not $hasHeader) {
                    Add-Failure -Failures $failures -Message ("Expected header '{0}' with value '{1}', but it was missing." -f $headerExpectation.Name, $expectedHeaderValue)
                    continue
                }

                $matches = $false
                foreach ($actualHeaderValue in @($actualValues)) {
                    if ([string]::Equals([string]$actualHeaderValue, $expectedHeaderValue, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $matches = $true
                        break
                    }
                }

                if (-not $matches) {
                    Add-Failure -Failures $failures -Message ("Expected header '{0}' to equal '{1}', but received '{2}'." -f $headerExpectation.Name, $expectedHeaderValue, (@($actualValues) -join ', '))
                }
            }

            if (Test-ObjectProperty -Object $headerSpecification -Name 'notEquals') {
                $unexpectedHeaderValue = [string](Resolve-TemplatedValue -Value (Get-ObjectPropertyValue -Object $headerSpecification -Name 'notEquals' -Required) -Variables $Variables)
                if (-not $hasHeader) {
                    Add-Failure -Failures $failures -Message ("Expected header '{0}' to be present so it could differ from '{1}', but it was missing." -f $headerExpectation.Name, $unexpectedHeaderValue)
                    continue
                }

                $matches = $false
                foreach ($actualHeaderValue in @($actualValues)) {
                    if ([string]::Equals([string]$actualHeaderValue, $unexpectedHeaderValue, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $matches = $true
                        break
                    }
                }

                if ($matches) {
                    Add-Failure -Failures $failures -Message ("Expected header '{0}' to differ from '{1}', but it matched." -f $headerExpectation.Name, $unexpectedHeaderValue)
                }
            }

            if (Test-ObjectProperty -Object $headerSpecification -Name 'matches') {
                $pattern = [string](Resolve-TemplatedValue -Value (Get-ObjectPropertyValue -Object $headerSpecification -Name 'matches' -Required) -Variables $Variables)
                if (-not $hasHeader) {
                    Add-Failure -Failures $failures -Message ("Expected header '{0}' to match regex '{1}', but it was missing." -f $headerExpectation.Name, $pattern)
                    continue
                }

                $matched = $false
                foreach ($actualHeaderValue in @($actualValues)) {
                    if ([System.Text.RegularExpressions.Regex]::IsMatch([string]$actualHeaderValue, $pattern)) {
                        $matched = $true
                        break
                    }
                }

                if (-not $matched) {
                    Add-Failure -Failures $failures -Message ("Expected header '{0}' to match regex '{1}', but received '{2}'." -f $headerExpectation.Name, $pattern, (@($actualValues) -join ', '))
                }
            }
        }
    }

    $jsonAssertions = @(Get-NormalizedArray -Value (Get-ObjectPropertyValue -Object $expect -Name 'json'))
    if ($jsonAssertions.Count -gt 0) {
        if ($null -eq $ResponseJson) {
            Add-Failure -Failures $failures -Message 'Expected a JSON response body, but none could be parsed.'
        }
        else {
            foreach ($jsonAssertion in $jsonAssertions) {
                $path = [string](Get-ObjectPropertyValue -Object $jsonAssertion -Name 'path' -Required)
                $pathResult = Get-JsonPathResult -InputObject $ResponseJson -Path $path

                if (Test-ObjectProperty -Object $jsonAssertion -Name 'exists') {
                    $expectedPresence = [bool](Resolve-TemplatedValue -Value (Get-ObjectPropertyValue -Object $jsonAssertion -Name 'exists' -Required) -Variables $Variables)
                    if ($pathResult.Found -ne $expectedPresence) {
                        Add-Failure -Failures $failures -Message ("Expected JSON path '{0}' presence to be '{1}', but actual presence was '{2}'." -f $path, $expectedPresence, $pathResult.Found)
                    }

                    if (-not $expectedPresence) {
                        continue
                    }
                }
                elseif (-not $pathResult.Found) {
                    Add-Failure -Failures $failures -Message ("Expected JSON path '{0}' to exist." -f $path)
                    continue
                }

                if (Test-ObjectProperty -Object $jsonAssertion -Name 'equals') {
                    $expectedValue = Resolve-TemplatedValue -Value (Get-ObjectPropertyValue -Object $jsonAssertion -Name 'equals' -Required) -Variables $Variables
                    if (-not (Test-ValueEquals -Actual $pathResult.Value -Expected $expectedValue)) {
                        Add-Failure -Failures $failures -Message ("Expected JSON path '{0}' to equal '{1}', but received '{2}'." -f $path, (ConvertTo-ComparisonText -Value $expectedValue), (ConvertTo-ComparisonText -Value $pathResult.Value))
                    }
                }

                if (Test-ObjectProperty -Object $jsonAssertion -Name 'notEquals') {
                    $unexpectedValue = Resolve-TemplatedValue -Value (Get-ObjectPropertyValue -Object $jsonAssertion -Name 'notEquals' -Required) -Variables $Variables
                    if (Test-ValueEquals -Actual $pathResult.Value -Expected $unexpectedValue) {
                        Add-Failure -Failures $failures -Message ("Expected JSON path '{0}' to differ from '{1}', but it matched." -f $path, (ConvertTo-ComparisonText -Value $unexpectedValue))
                    }
                }

                if (Test-ObjectProperty -Object $jsonAssertion -Name 'matches') {
                    $pattern = [string](Resolve-TemplatedValue -Value (Get-ObjectPropertyValue -Object $jsonAssertion -Name 'matches' -Required) -Variables $Variables)
                    $actualText = ConvertTo-ComparisonText -Value $pathResult.Value
                    if (-not [System.Text.RegularExpressions.Regex]::IsMatch($actualText, $pattern)) {
                        Add-Failure -Failures $failures -Message ("Expected JSON path '{0}' to match regex '{1}', but received '{2}'." -f $path, $pattern, $actualText)
                    }
                }
            }
        }
    }

    $errorCodeExpectation = Get-ObjectPropertyValue -Object $expect -Name 'errorCode'
    if ($null -ne $errorCodeExpectation) {
        if ($null -eq $ResponseJson) {
            Add-Failure -Failures $failures -Message 'Expected a JSON error body, but none could be parsed.'
        }
        else {
            $errorCodePath = '$.code'
            $expectedErrorCode = $null

            if (Test-IsSimpleValue -Value $errorCodeExpectation) {
                $expectedErrorCode = Resolve-TemplatedValue -Value $errorCodeExpectation -Variables $Variables
            }
            else {
                $pathOverride = Get-ObjectPropertyValue -Object $errorCodeExpectation -Name 'path'
                if (-not [string]::IsNullOrWhiteSpace([string]$pathOverride)) {
                    $errorCodePath = [string]$pathOverride
                }

                $expectedErrorCode = Resolve-TemplatedValue -Value (Get-ObjectPropertyValue -Object $errorCodeExpectation -Name 'equals' -Required) -Variables $Variables
            }

            $pathResult = Get-JsonPathResult -InputObject $ResponseJson -Path $errorCodePath
            if (-not $pathResult.Found) {
                Add-Failure -Failures $failures -Message ("Expected error code path '{0}' to exist." -f $errorCodePath)
            }
            elseif (-not (Test-ValueEquals -Actual $pathResult.Value -Expected $expectedErrorCode)) {
                Add-Failure -Failures $failures -Message ("Expected error code '{0}', but received '{1}'." -f (ConvertTo-ComparisonText -Value $expectedErrorCode), (ConvertTo-ComparisonText -Value $pathResult.Value))
            }
        }
    }

    if ($expectedStatus -eq 204 -and -not [string]::IsNullOrWhiteSpace($ResponseBody)) {
        Add-Failure -Failures $failures -Message 'Expected an empty response body for status 204.'
    }

    if ($expectedStatus -eq 201) {
        $locationValues = $null
        if (-not (Try-GetHeaderValues -HeaderMap $ResponseHeaders -Name 'Location' -Values ([ref]$locationValues)) -or [string]::IsNullOrWhiteSpace([string]$locationValues[0])) {
            Add-Failure -Failures $failures -Message 'Expected a Location header for status 201.'
        }
    }

    return $failures.ToArray()
}

function Update-ExtractedVariables {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Scenario,

        [Parameter(Mandatory = $true)]
        [hashtable]$Variables,

        [object]$ResponseJson,

        [hashtable]$ResponseHeaders
    )

    $extractDefinition = Get-ObjectPropertyValue -Object $Scenario -Name 'extract'
    if ($null -eq $extractDefinition) {
        return
    }

    foreach ($extraction in Get-ObjectEntries -Object $extractDefinition) {
        $variableName = $extraction.Name
        $required = $true

        if ($extraction.Value -is [string]) {
            if ($null -eq $ResponseJson) {
                throw "Cannot extract variable '$variableName' because the response body was not JSON."
            }

            $pathResult = Get-JsonPathResult -InputObject $ResponseJson -Path $extraction.Value
            if (-not $pathResult.Found) {
                throw "Failed to extract variable '$variableName' from JSON path '$($extraction.Value)'."
            }

            $Variables[$variableName] = $pathResult.Value
            continue
        }

        $specification = $extraction.Value
        if (Test-ObjectProperty -Object $specification -Name 'required') {
            $required = [bool](Get-ObjectPropertyValue -Object $specification -Name 'required' -Required)
        }

        $source = [string](Get-ObjectPropertyValue -Object $specification -Name 'from')
        if ([string]::IsNullOrWhiteSpace($source) -or [string]::Equals($source, 'json', [System.StringComparison]::OrdinalIgnoreCase)) {
            if ($null -eq $ResponseJson) {
                if ($required) {
                    throw "Cannot extract variable '$variableName' because the response body was not JSON."
                }

                continue
            }

            $path = [string](Get-ObjectPropertyValue -Object $specification -Name 'path' -Required)
            $pathResult = Get-JsonPathResult -InputObject $ResponseJson -Path $path
            if (-not $pathResult.Found) {
                if ($required) {
                    throw "Failed to extract variable '$variableName' from JSON path '$path'."
                }

                continue
            }

            $Variables[$variableName] = $pathResult.Value
            continue
        }

        if ([string]::Equals($source, 'header', [System.StringComparison]::OrdinalIgnoreCase)) {
            $headerName = [string](Get-ObjectPropertyValue -Object $specification -Name 'name' -Required)
            $headerValues = $null
            if (-not (Try-GetHeaderValues -HeaderMap $ResponseHeaders -Name $headerName -Values ([ref]$headerValues))) {
                if ($required) {
                    throw "Failed to extract variable '$variableName' from header '$headerName'."
                }

                continue
            }

            $Variables[$variableName] = @($headerValues)[0]
            continue
        }

        throw "Unsupported extract source '$source' for variable '$variableName'."
    }
}

function Test-ScenarioSelected {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Scenario,

        [string[]]$Tags = @(),

        [string[]]$Feature = @()
    )

    $normalizedTags = @(Get-StringArray -Value $Tags)
    $normalizedFeatures = @(Get-StringArray -Value $Feature)

    if ($normalizedTags.Count -gt 0) {
        $scenarioTags = Get-StringArray -Value (Get-ObjectPropertyValue -Object $Scenario -Name 'tags')
        $tagMatches = $false

        foreach ($requiredTag in $normalizedTags) {
            foreach ($scenarioTag in $scenarioTags) {
                if ([string]::Equals($scenarioTag, $requiredTag, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $tagMatches = $true
                    break
                }
            }

            if ($tagMatches) {
                break
            }
        }

        if (-not $tagMatches) {
            return $false
        }
    }

    if ($normalizedFeatures.Count -gt 0) {
        $scenarioFeature = [string](Get-ObjectPropertyValue -Object $Scenario -Name 'feature')
        if ([string]::IsNullOrWhiteSpace($scenarioFeature)) {
            return $false
        }

        $featureMatches = $false
        foreach ($requiredFeature in $normalizedFeatures) {
            if ([string]::Equals($scenarioFeature, $requiredFeature, [System.StringComparison]::OrdinalIgnoreCase)) {
                $featureMatches = $true
                break
            }
        }

        if (-not $featureMatches) {
            return $false
        }
    }

    return $true
}

function Assert-Manifest {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Manifest
    )

    if (-not ($Manifest -is [System.Collections.IDictionary])) {
        throw 'Manifest root must be a JSON object.'
    }

    $scenarios = @(Get-NormalizedArray -Value (Get-ObjectPropertyValue -Object $Manifest -Name 'scenarios' -Required))
    if ($scenarios.Count -eq 0) {
        throw "Manifest must contain at least one scenario in 'scenarios'."
    }

    $seenScenarioIds = @{}
    foreach ($scenario in $scenarios) {
        if (-not ($scenario -is [System.Collections.IDictionary])) {
            throw 'Each scenario must be a JSON object.'
        }

        $scenarioId = [string](Get-ObjectPropertyValue -Object $scenario -Name 'id' -Required)
        if ([string]::IsNullOrWhiteSpace($scenarioId)) {
            throw 'Scenario id must not be empty.'
        }

        if ($seenScenarioIds.ContainsKey($scenarioId)) {
            throw "Scenario id '$scenarioId' is duplicated."
        }

        $seenScenarioIds[$scenarioId] = $true

        [void](Get-ObjectPropertyValue -Object $scenario -Name 'method' -Required)
        [void](Get-ObjectPropertyValue -Object $scenario -Name 'url' -Required)

        $expect = Get-ObjectPropertyValue -Object $scenario -Name 'expect' -Required
        if (-not ($expect -is [System.Collections.IDictionary])) {
            throw "Scenario '$scenarioId' must define 'expect' as an object."
        }

        [void](Get-ObjectPropertyValue -Object $expect -Name 'status' -Required)

        $hasJsonBody = Test-ObjectProperty -Object $scenario -Name 'jsonBody'
        $hasBodyFile = Test-ObjectProperty -Object $scenario -Name 'bodyFile'
        $hasRepeatBody = Test-ObjectProperty -Object $scenario -Name 'repeatBody'
        if (@($hasJsonBody, $hasBodyFile, $hasRepeatBody | Where-Object { $_ }).Count -gt 1) {
            throw "Scenario '$scenarioId' can define only one of jsonBody, bodyFile, or repeatBody."
        }

        $internalAuth = Get-ObjectPropertyValue -Object $scenario -Name 'internalAuth'
        if ($null -ne $internalAuth) {
            if (-not ($internalAuth -is [System.Collections.IDictionary])) {
                throw "Scenario '$scenarioId' must define 'internalAuth' as an object."
            }

            [void](Get-ObjectPropertyValue -Object $internalAuth -Name 'secretEnv' -Required)
        }
    }
}

function Get-SensitiveFieldList {
    param(
        [object]$Manifest,
        [object]$Config
    )

    $values = New-Object System.Collections.Generic.List[string]
    foreach ($defaultField in Get-DefaultSensitiveFields) {
        [void]$values.Add($defaultField)
    }

    foreach ($fieldName in Get-StringArray -Value (Get-ObjectPropertyValue -Object $Manifest -Name 'sensitiveFields')) {
        if (-not $values.Contains($fieldName)) {
            [void]$values.Add($fieldName)
        }
    }

    foreach ($fieldName in Get-StringArray -Value (Get-ObjectPropertyValue -Object $Config -Name 'sensitiveFields')) {
        if (-not $values.Contains($fieldName)) {
            [void]$values.Add($fieldName)
        }
    }

    return $values.ToArray()
}

function Invoke-HttpScenario {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Scenario,

        [Parameter(Mandatory = $true)]
        [uri]$BaseUri,

        [Parameter(Mandatory = $true)]
        [hashtable]$Variables,

        [Parameter(Mandatory = $true)]
        [System.Net.Http.HttpClient]$HttpClient,

        [Parameter(Mandatory = $true)]
        [string]$ManifestDirectory,

        [string[]]$SensitiveFields = @()
    )

    $scenarioId = [string](Get-ObjectPropertyValue -Object $Scenario -Name 'id' -Required)
    $method = ([string](Get-ObjectPropertyValue -Object $Scenario -Name 'method' -Required)).ToUpperInvariant()
    $feature = [string](Get-ObjectPropertyValue -Object $Scenario -Name 'feature')
    $tags = @(Get-StringArray -Value (Get-ObjectPropertyValue -Object $Scenario -Name 'tags'))

    $result = [ordered]@{
        id                = $scenarioId
        feature           = if ([string]::IsNullOrWhiteSpace($feature)) { $null } else { $feature }
        tags              = $tags
        method            = $method
        requestUrl        = $null
        passed            = $false
        statusCode        = $null
        durationMs        = 0
        assertionFailures = @()
        request           = $null
        response          = $null
    }

    $request = $null
    $response = $null

    try {
        Resolve-VariableAssignments -Assignments (Get-ObjectPropertyValue -Object $Scenario -Name 'setVariables') -Variables $Variables

        $resolvedRelativeUrl = [string](Resolve-TemplatedValue -Value (Get-ObjectPropertyValue -Object $Scenario -Name 'url' -Required) -Variables $Variables)
        $requestUri = [System.Uri]::new($BaseUri, $resolvedRelativeUrl)
        $result.requestUrl = $requestUri.AbsoluteUri

        $resolvedHeaders = @{}
        foreach ($headerEntry in Get-ObjectEntries -Object (Get-ObjectPropertyValue -Object $Scenario -Name 'headers')) {
            $resolvedHeaders[$headerEntry.Name] = [string](Resolve-TemplatedValue -Value $headerEntry.Value -Variables $Variables)
        }

        $body = Build-RequestBody -Scenario $Scenario -Variables $Variables -ManifestDirectory $ManifestDirectory
        Apply-InternalAuthHeaders `
            -Scenario $Scenario `
            -InternalAuth (Get-ObjectPropertyValue -Object $Scenario -Name 'internalAuth') `
            -ResolvedHeaders $resolvedHeaders `
            -RequestUri $requestUri `
            -Method $method `
            -BodyContent $body.Content `
            -Variables $Variables

        $httpMethod = New-Object System.Net.Http.HttpMethod -ArgumentList $method
        $request = New-Object System.Net.Http.HttpRequestMessage -ArgumentList $httpMethod, $requestUri

        $contentTypeHeader = $null
        foreach ($headerName in @($resolvedHeaders.Keys)) {
            if ([string]::Equals([string]$headerName, 'Content-Type', [System.StringComparison]::OrdinalIgnoreCase)) {
                $contentTypeHeader = [string]$resolvedHeaders[$headerName]
                continue
            }

            if (-not $request.Headers.TryAddWithoutValidation([string]$headerName, [string]$resolvedHeaders[$headerName])) {
                throw "Header '$headerName' could not be added to scenario '$scenarioId'."
            }
        }

        if ($null -ne $body.Content) {
            $finalContentType = if ([string]::IsNullOrWhiteSpace($contentTypeHeader)) { $body.ContentType } else { $contentTypeHeader }
            if ([string]::IsNullOrWhiteSpace($finalContentType)) {
                $finalContentType = 'application/json'
            }

            $request.Content = New-Object System.Net.Http.StringContent -ArgumentList $body.Content
            $request.Content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($finalContentType)
            $request.Content.Headers.ContentType.CharSet = 'utf-8'
            if ($body.ForceChunked) {
                $request.Headers.TransferEncodingChunked = $true
                $request.Content.Headers.ContentLength = $null
            }
            $resolvedHeaders['Content-Type'] = $finalContentType
        }
        elseif (-not [string]::IsNullOrWhiteSpace($contentTypeHeader)) {
            throw "Scenario '$scenarioId' sets Content-Type but does not define jsonBody or bodyFile."
        }

        $requestPreview = [ordered]@{
            headers    = Protect-Headers -Headers $resolvedHeaders -SensitiveFields $SensitiveFields
            bodyLength = if ($null -eq $body.Content) { 0 } else { $body.Content.Length }
            body       = if ($null -eq $body.Content) { $null } else { Convert-BodyPreview -Body $body.Content -ContentType $resolvedHeaders['Content-Type'] -SensitiveFields $SensitiveFields }
        }

        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $response = $HttpClient.SendAsync($request).GetAwaiter().GetResult()
        $stopwatch.Stop()

        $responseBody = if ($null -ne $response.Content) { $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() } else { '' }
        $responseContentType = if ($null -ne $response.Content -and $null -ne $response.Content.Headers.ContentType) { $response.Content.Headers.ContentType.ToString() } else { $null }
        $responseHeaders = Get-ResponseHeaderMap -Response $response
        $responseJson = $null

        if (Test-IsJsonContent -Body $responseBody -ContentType $responseContentType) {
            try {
                $responseJson = ConvertTo-PlainValue -Value ($responseBody | ConvertFrom-Json -ErrorAction Stop)
            }
            catch {
                $responseJson = $null
            }
        }

        $result.statusCode = [int]$response.StatusCode
        $result.durationMs = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 2)
        $result.request = $requestPreview
        $result.response = [ordered]@{
            contentType = $responseContentType
            headers     = Protect-Headers -Headers $responseHeaders -SensitiveFields $SensitiveFields
            bodyLength  = if ([string]::IsNullOrEmpty($responseBody)) { 0 } else { $responseBody.Length }
            body        = Convert-BodyPreview -Body $responseBody -ContentType $responseContentType -SensitiveFields $SensitiveFields
        }

        $assertionFailures = @(Test-ScenarioExpectations `
            -Scenario $Scenario `
            -Variables $Variables `
            -StatusCode ([int]$response.StatusCode) `
            -ResponseContentType $responseContentType `
            -ResponseBody $responseBody `
            -ResponseHeaders $responseHeaders `
            -ResponseJson $responseJson)

        $result.assertionFailures = $assertionFailures
        $result.passed = ($assertionFailures.Count -eq 0)

        if ($result.passed) {
            Update-ExtractedVariables -Scenario $Scenario -Variables $Variables -ResponseJson $responseJson -ResponseHeaders $responseHeaders
        }
    }
    catch {
        $result.passed = $false
        $result.assertionFailures = @("Scenario error: $($_.Exception.Message)")

        if ($null -eq $result.request) {
            $result.request = $null
        }

        if ($null -eq $result.response) {
            $result.response = $null
        }
    }
    finally {
        if ($null -ne $request) {
            $request.Dispose()
        }

        if ($null -ne $response) {
            $response.Dispose()
        }
    }

    return [pscustomobject]$result
}

function Write-ConsoleSummary {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$ScenarioResults,

        [Parameter(Mandatory = $true)]
        [string]$ResultsPath
    )

    foreach ($scenarioResult in $ScenarioResults) {
        $statusLabel = if ($scenarioResult.passed) { 'PASS' } else { 'FAIL' }
        $statusColor = if ($scenarioResult.passed) { 'Green' } else { 'Red' }
        $statusCodeText = if ($null -eq $scenarioResult.statusCode) { 'n/a' } else { [string]$scenarioResult.statusCode }
        Write-Host ("[{0}] {1} {2} ({3} ms)" -f $statusLabel, $scenarioResult.id, $statusCodeText, $scenarioResult.durationMs) -ForegroundColor $statusColor

        if (-not $scenarioResult.passed) {
            foreach ($failure in $scenarioResult.assertionFailures) {
                Write-Host ("  - {0}" -f $failure) -ForegroundColor Yellow
            }
        }
    }

    $total = $ScenarioResults.Count
    $passed = @($ScenarioResults | Where-Object { $_.passed }).Count
    $failed = $total - $passed

    Write-Host ''
    Write-Host ("Summary: {0} total, {1} passed, {2} failed" -f $total, $passed, $failed) -ForegroundColor Cyan
    Write-Host ("Results: {0}" -f $ResultsPath) -ForegroundColor Cyan
}

function Invoke-HttpTestHarness {
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

        [switch]$AllowNonLocal
    )

    $resolvedManifestPath = Resolve-ExistingPath -Path $ManifestPath
    $manifestDirectory = Split-Path -Path $resolvedManifestPath -Parent
    $manifest = Read-JsonDocument -Path $resolvedManifestPath
    Assert-Manifest -Manifest $manifest

    $config = [ordered]@{}
    if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
        $resolvedConfigPath = Resolve-ExistingPath -Path $ConfigPath -BaseDirectories @($manifestDirectory)
        $config = Read-JsonDocument -Path $resolvedConfigPath
    }

    $variablesFromFile = [ordered]@{}
    if (-not [string]::IsNullOrWhiteSpace($VariablesPath)) {
        $resolvedVariablesPath = Resolve-ExistingPath -Path $VariablesPath -BaseDirectories @($manifestDirectory)
        $variablesFromFile = Read-JsonDocument -Path $resolvedVariablesPath
        if (-not ($variablesFromFile -is [System.Collections.IDictionary])) {
            throw "Variables file '$resolvedVariablesPath' must contain a JSON object."
        }
    }

    $effectiveBaseUrl = $BaseUrl
    if ([string]::IsNullOrWhiteSpace($effectiveBaseUrl)) {
        $effectiveBaseUrl = [string](Get-ObjectPropertyValue -Object $config -Name 'baseUrl')
    }

    if ([string]::IsNullOrWhiteSpace($effectiveBaseUrl)) {
        $effectiveBaseUrl = [System.Environment]::GetEnvironmentVariable('HTTP_TEST_BASE_URL')
    }

    if ([string]::IsNullOrWhiteSpace($effectiveBaseUrl)) {
        throw 'Base URL was not supplied. Provide -BaseUrl, a config file baseUrl, or the HTTP_TEST_BASE_URL environment variable.'
    }

    $baseUri = $null
    if (-not [System.Uri]::TryCreate($effectiveBaseUrl, [System.UriKind]::Absolute, [ref]$baseUri)) {
        throw "Base URL '$effectiveBaseUrl' is not a valid absolute URL."
    }

    Assert-LocalBaseUrl -BaseUri $baseUri -AllowNonLocal:$AllowNonLocal

    $variableBag = @{}
    $variableBag['baseUrl'] = $baseUri.AbsoluteUri.TrimEnd('/')

    Resolve-VariableAssignments -Assignments (Get-ObjectPropertyValue -Object $config -Name 'variables') -Variables $variableBag
    Resolve-VariableAssignments -Assignments $variablesFromFile -Variables $variableBag

    if ($null -ne $Variables) {
        Resolve-VariableAssignments -Assignments (ConvertTo-PlainValue -Value $Variables) -Variables $variableBag
    }

    Resolve-VariableAssignments -Assignments (Get-ObjectPropertyValue -Object $manifest -Name 'setupVariables') -Variables $variableBag

    $selectedScenarios = New-Object System.Collections.Generic.List[object]
    foreach ($scenario in @(Get-NormalizedArray -Value (Get-ObjectPropertyValue -Object $manifest -Name 'scenarios' -Required))) {
        if (Test-ScenarioSelected -Scenario $scenario -Tags (Get-StringArray -Value $Tags) -Feature (Get-StringArray -Value $Feature)) {
            [void]$selectedScenarios.Add($scenario)
        }
    }

    if ($selectedScenarios.Count -eq 0) {
        throw 'No scenarios matched the supplied filters.'
    }

    $sensitiveFields = Get-SensitiveFieldList -Manifest $manifest -Config $config
    $httpClient = New-HttpClient
    $startTime = Get-Date
    $scenarioResults = New-Object System.Collections.Generic.List[object]

    try {
        foreach ($scenario in $selectedScenarios) {
            $scenarioResult = Invoke-HttpScenario `
                -Scenario $scenario `
                -BaseUri $baseUri `
                -Variables $variableBag `
                -HttpClient $httpClient `
                -ManifestDirectory $manifestDirectory `
                -SensitiveFields $sensitiveFields

            [void]$scenarioResults.Add($scenarioResult)
        }
    }
    finally {
        $httpClient.Dispose()
    }

    $endTime = Get-Date
    $scenarioResultArray = $scenarioResults.ToArray()
    $passedCount = @($scenarioResultArray | Where-Object { $_.passed }).Count
    $failedCount = $scenarioResultArray.Count - $passedCount

    $resultsDirectory = Join-Path -Path $PSScriptRoot -ChildPath 'artifacts'
    if (-not (Test-Path -LiteralPath $resultsDirectory)) {
        [void](New-Item -ItemType Directory -Path $resultsDirectory -Force)
    }

    $resolvedResultsPath = Resolve-OutputPath -Path $ResultsPath -DefaultDirectory $resultsDirectory
    $resultsParentDirectory = Split-Path -Path $resolvedResultsPath -Parent
    if (-not (Test-Path -LiteralPath $resultsParentDirectory)) {
        [void](New-Item -ItemType Directory -Path $resultsParentDirectory -Force)
    }

    $resultsDocument = [ordered]@{
        manifestPath = $resolvedManifestPath
        startedAt    = $startTime.ToUniversalTime().ToString('o')
        completedAt  = $endTime.ToUniversalTime().ToString('o')
        baseUrl      = $baseUri.AbsoluteUri
        filters      = [ordered]@{
            tags    = @(Get-StringArray -Value $Tags)
            feature = @(Get-StringArray -Value $Feature)
        }
        summary      = [ordered]@{
            total      = $scenarioResultArray.Count
            passed     = $passedCount
            failed     = $failedCount
            durationMs = [math]::Round(($endTime - $startTime).TotalMilliseconds, 2)
            passedAll  = ($failedCount -eq 0)
        }
        scenarios    = $scenarioResultArray
    }

    $resultsDocument | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $resolvedResultsPath -Encoding UTF8

    Write-ConsoleSummary -ScenarioResults $scenarioResultArray -ResultsPath $resolvedResultsPath

    return [pscustomobject]@{
        ManifestPath = $resolvedManifestPath
        ResultsPath  = $resolvedResultsPath
        Summary      = [pscustomobject]$resultsDocument.summary
        Scenarios    = $scenarioResultArray
    }
}

function Invoke-HttpTestHarnessSelfTest {
    [CmdletBinding()]
    param()

    $results = New-Object System.Collections.Generic.List[object]

    function Add-SelfTestResult {
        param(
            [string]$Name,
            [scriptblock]$Action
        )

        try {
            & $Action
            [void]$results.Add([pscustomobject]@{
                    Name    = $Name
                    Passed  = $true
                    Details = $null
                })
            Write-Host ("[PASS] {0}" -f $Name) -ForegroundColor Green
        }
        catch {
            [void]$results.Add([pscustomobject]@{
                    Name    = $Name
                    Passed  = $false
                    Details = $_.Exception.Message
                })
            Write-Host ("[FAIL] {0}" -f $Name) -ForegroundColor Red
            Write-Host ("  - {0}" -f $_.Exception.Message) -ForegroundColor Yellow
        }
    }

    Add-SelfTestResult -Name 'Local base URL guard allows loopback' -Action {
        if (-not (Test-IsLocalBaseUrl -Uri ([uri]'https://localhost:5001'))) {
            throw 'Expected localhost to be allowed.'
        }
    }

    Add-SelfTestResult -Name 'Local base URL guard rejects remote host' -Action {
        if (Test-IsLocalBaseUrl -Uri ([uri]'https://api.example.com')) {
            throw 'Expected remote host to be rejected.'
        }
    }

    Add-SelfTestResult -Name 'Template interpolation resolves variables and environment values' -Action {
        [System.Environment]::SetEnvironmentVariable('HTTP_TEST_HARNESS_SELFTEST_VALUE', 'env-ok', 'Process')
        try {
            $variables = @{ name = 'value-ok' }
            $resolved = Resolve-TemplateString -Template 'prefix-{{var:name}}-{{env:HTTP_TEST_HARNESS_SELFTEST_VALUE}}' -Variables $variables
            if ($resolved -ne 'prefix-value-ok-env-ok') {
                throw "Unexpected interpolation result '$resolved'."
            }
        }
        finally {
            [System.Environment]::SetEnvironmentVariable('HTTP_TEST_HARNESS_SELFTEST_VALUE', $null, 'Process')
        }
    }

    Add-SelfTestResult -Name 'Generated variables can chain in order' -Action {
        $variables = @{}
        Resolve-VariableAssignments -Assignments ([ordered]@{
                runId = '{{gen:guid}}'
                email = 'http-harness+{{var:runId}}@example.test'
            }) -Variables $variables

        if ([string]::IsNullOrWhiteSpace([string]$variables.runId)) {
            throw 'runId was not generated.'
        }

        if ([string]$variables.email -notlike "http-harness+$($variables.runId)@example.test") {
            throw 'email did not use the generated runId.'
        }
    }

    Add-SelfTestResult -Name 'Generated timestamps and nonce support offsets' -Action {
        $variables = @{}
        $nonce = Resolve-TemplateTokenValue -Token 'gen:nonce' -Variables $variables
        if ($nonce -notmatch '^[0-9a-f]{32}$') {
            throw "Generated nonce '$nonce' was not a 32-character lowercase hex value."
        }

        $offsetValue = [datetimeoffset](Resolve-TemplateTokenValue -Token 'gen:iso8601:+10m' -Variables $variables)
        $deltaMinutes = [math]::Round(($offsetValue - [datetimeoffset]::UtcNow).TotalMinutes)
        if ($deltaMinutes -lt 9 -or $deltaMinutes -gt 11) {
            throw "Generated ISO-8601 offset was not close to +10 minutes. Actual delta: $deltaMinutes."
        }

        $dateOnly = [string](Resolve-TemplateTokenValue -Token 'gen:timestamp:yyyy-MM-dd:+1d' -Variables $variables)
        if ($dateOnly -notmatch '^\d{4}-\d{2}-\d{2}$') {
            throw "Generated timestamp with format/offset produced '$dateOnly'."
        }
    }

    Add-SelfTestResult -Name 'Internal auth helper signs canonical requests with empty idempotency line' -Action {
        [System.Environment]::SetEnvironmentVariable('HTTP_TEST_HARNESS_SELFTEST_SECRET', '0123456789abcdef0123456789abcdef', 'Process')
        try {
            $scenario = [ordered]@{
                id = 'signed-internal-request'
            }
            $headers = @{}
            $internalAuth = [ordered]@{
                serviceId = 'step6-selftest'
                secretEnv = 'HTTP_TEST_HARNESS_SELFTEST_SECRET'
                timestamp = '2026-08-20T20:15:00.0000000+00:00'
                nonce     = '0123456789abcdef0123456789abcdef'
            }
            $requestUri = [uri]'https://localhost/api/v1/internal/catalog/snapshot?z=1&companyId=22222222-2222-2222-2222-222222222222&a=2'

            Apply-InternalAuthHeaders `
                -Scenario $scenario `
                -InternalAuth $internalAuth `
                -ResolvedHeaders $headers `
                -RequestUri $requestUri `
                -Method 'GET' `
                -BodyContent $null `
                -Variables @{}

            $expectedCanonicalRequest = [string]::Join(
                "`n",
                @(
                    'ghseeli-hmac-sha256-v1',
                    'step6-selftest',
                    'GET',
                    '/api/v1/internal/catalog/snapshot?a=2&companyId=22222222-2222-2222-2222-222222222222&z=1',
                    '2026-08-20T20:15:00.0000000+00:00',
                    '0123456789abcdef0123456789abcdef',
                    '',
                    'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
                ))
            $expectedSignature = Compute-HmacSha256Hex -Secret '0123456789abcdef0123456789abcdef' -Value $expectedCanonicalRequest

            if ($headers['X-Ghseeli-Service-Id'] -ne 'step6-selftest') {
                throw 'Internal auth helper did not add the service-id header.'
            }

            if ($headers['X-Ghseeli-Signature'] -ne $expectedSignature) {
                throw 'Internal auth helper did not compute the expected signature.'
            }
        }
        finally {
            [System.Environment]::SetEnvironmentVariable('HTTP_TEST_HARNESS_SELFTEST_SECRET', $null, 'Process')
        }
    }

    Add-SelfTestResult -Name 'Internal auth helper signs Idempotency-Key when present' -Action {
        [System.Environment]::SetEnvironmentVariable('HTTP_TEST_HARNESS_SELFTEST_SECRET', '0123456789abcdef0123456789abcdef', 'Process')
        try {
            $scenario = [ordered]@{
                id = 'signed-idempotent-internal-request'
            }
            $headers = @{
                'Idempotency-Key' = 'idem-fixed-vector'
                'Content-Type'    = 'application/json'
            }
            $internalAuth = [ordered]@{
                serviceId = 'step6-selftest'
                secretEnv = 'HTTP_TEST_HARNESS_SELFTEST_SECRET'
                timestamp = '2026-08-20T20:15:00.0000000+00:00'
                nonce     = '0123456789abcdef0123456789abcdef'
            }
            $requestUri = [uri]'https://localhost/api/v1/internal/appointments/validate?companyId=22222222-2222-2222-2222-222222222222'
            $bodyContent = '{"currency":"ILS"}'

            Apply-InternalAuthHeaders `
                -Scenario $scenario `
                -InternalAuth $internalAuth `
                -ResolvedHeaders $headers `
                -RequestUri $requestUri `
                -Method 'POST' `
                -BodyContent $bodyContent `
                -Variables @{}

            $expectedCanonicalRequest = [string]::Join(
                "`n",
                @(
                    'ghseeli-hmac-sha256-v1',
                    'step6-selftest',
                    'POST',
                    '/api/v1/internal/appointments/validate?companyId=22222222-2222-2222-2222-222222222222',
                    '2026-08-20T20:15:00.0000000+00:00',
                    '0123456789abcdef0123456789abcdef',
                    'idem-fixed-vector',
                    (Compute-Sha256Hex -Bytes ([System.Text.Encoding]::UTF8.GetBytes($bodyContent)))
                ))
            $expectedSignature = Compute-HmacSha256Hex -Secret '0123456789abcdef0123456789abcdef' -Value $expectedCanonicalRequest

            if ($headers['X-Ghseeli-Signature'] -ne $expectedSignature) {
                throw 'Internal auth helper did not include Idempotency-Key in the canonical request.'
            }
        }
        finally {
            [System.Environment]::SetEnvironmentVariable('HTTP_TEST_HARNESS_SELFTEST_SECRET', $null, 'Process')
        }
    }

    Add-SelfTestResult -Name 'Changing Idempotency-Key changes the internal auth signature' -Action {
        [System.Environment]::SetEnvironmentVariable('HTTP_TEST_HARNESS_SELFTEST_SECRET', '0123456789abcdef0123456789abcdef', 'Process')
        try {
            $scenario = [ordered]@{
                id = 'tampered-idempotency-key'
            }
            $requestUri = [uri]'https://localhost/api/v1/internal/appointments/validate'
            $internalAuth = [ordered]@{
                serviceId = 'step6-selftest'
                secretEnv = 'HTTP_TEST_HARNESS_SELFTEST_SECRET'
                timestamp = '2026-08-20T20:15:00.0000000+00:00'
                nonce     = '0123456789abcdef0123456789abcdef'
            }
            $bodyContent = '{"currency":"ILS"}'

            $headersBefore = @{
                'Idempotency-Key' = 'idem-before'
            }
            Apply-InternalAuthHeaders `
                -Scenario $scenario `
                -InternalAuth $internalAuth `
                -ResolvedHeaders $headersBefore `
                -RequestUri $requestUri `
                -Method 'POST' `
                -BodyContent $bodyContent `
                -Variables @{}

            $headersAfter = @{
                'Idempotency-Key' = 'idem-after'
            }
            Apply-InternalAuthHeaders `
                -Scenario $scenario `
                -InternalAuth $internalAuth `
                -ResolvedHeaders $headersAfter `
                -RequestUri $requestUri `
                -Method 'POST' `
                -BodyContent $bodyContent `
                -Variables @{}

            if ($headersBefore['X-Ghseeli-Signature'] -eq $headersAfter['X-Ghseeli-Signature']) {
                throw 'Changing Idempotency-Key did not change the computed signature.'
            }
        }
        finally {
            [System.Environment]::SetEnvironmentVariable('HTTP_TEST_HARNESS_SELFTEST_SECRET', $null, 'Process')
        }
    }

    Add-SelfTestResult -Name 'JSON path handles quoted keys and arrays' -Action {
        $document = [ordered]@{
            info  = [ordered]@{ title = 'Ghseeli APIs' }
            paths = [ordered]@{
                '/api/health' = [ordered]@{
                    get = [ordered]@{
                        operationId = 'CheckApiHealth'
                    }
                }
            }
            items = @(
                [ordered]@{ id = 1 },
                [ordered]@{ id = 2 }
            )
        }

        $pathResult = Get-JsonPathResult -InputObject $document -Path "$.paths['/api/health'].get.operationId"
        if (-not $pathResult.Found -or $pathResult.Value -ne 'CheckApiHealth') {
            throw 'Quoted key lookup failed.'
        }

        $arrayResult = Get-JsonPathResult -InputObject $document -Path '$.items[1].id'
        if (-not $arrayResult.Found -or $arrayResult.Value -ne 2) {
            throw 'Array index lookup failed.'
        }
    }

    Add-SelfTestResult -Name 'Expectation helper enforces 204 empty body' -Action {
        $scenario = [ordered]@{
            id     = 'no-content'
            method = 'DELETE'
            url    = '/api/items/1'
            expect = [ordered]@{
                status = 204
            }
        }

        $failures = @(Test-ScenarioExpectations `
            -Scenario $scenario `
            -Variables @{} `
            -StatusCode 204 `
            -ResponseContentType $null `
            -ResponseBody 'unexpected' `
            -ResponseHeaders @{} `
            -ResponseJson $null)

        if ($failures.Count -eq 0) {
            throw 'Expected the 204 body assertion to fail.'
        }
    }

    Add-SelfTestResult -Name 'Expectation helper enforces 201 location and stable error code' -Action {
        $createdScenario = [ordered]@{
            id     = 'create-item'
            method = 'POST'
            url    = '/api/items'
            expect = [ordered]@{
                status = 201
            }
        }

        $createFailures = @(Test-ScenarioExpectations `
            -Scenario $createdScenario `
            -Variables @{} `
            -StatusCode 201 `
            -ResponseContentType 'application/json' `
            -ResponseBody '{}' `
            -ResponseHeaders @{} `
            -ResponseJson ([ordered]@{}))

        if ($createFailures.Count -eq 0) {
            throw 'Expected the 201 Location assertion to fail.'
        }

        $errorScenario = [ordered]@{
            id     = 'error-code'
            method = 'POST'
            url    = '/api/items'
            expect = [ordered]@{
                status    = 400
                errorCode = [ordered]@{
                    path   = '$.error.code'
                    equals = 'INVALID_INPUT'
                }
            }
        }

        $errorFailures = @(Test-ScenarioExpectations `
            -Scenario $errorScenario `
            -Variables @{} `
            -StatusCode 400 `
            -ResponseContentType 'application/json' `
            -ResponseBody '{"error":{"code":"INVALID_INPUT"}}' `
            -ResponseHeaders @{} `
            -ResponseJson ([ordered]@{
                    error = [ordered]@{
                        code = 'INVALID_INPUT'
                    }
                }))

        if ($errorFailures.Count -ne 0) {
            throw ('Expected stable error code assertion to pass, but got: {0}' -f ($errorFailures -join '; '))
        }
    }

    Add-SelfTestResult -Name 'Redaction hides sensitive headers and fields' -Action {
        $headers = Protect-Headers -Headers @{
            Authorization = @('Bearer secret-value')
            Accept        = @('application/json')
        }

        if ($headers.Authorization -ne '[REDACTED]') {
            throw 'Authorization header was not redacted.'
        }

        $bodyPreview = Convert-BodyPreview -Body '{"token":"abc","profile":{"password":"secret","name":"ok"}}' -ContentType 'application/json' -SensitiveFields @()
        if ($bodyPreview['token'] -ne '[REDACTED]' -or $bodyPreview['profile']['password'] -ne '[REDACTED]') {
            throw 'Sensitive JSON fields were not redacted.'
        }
    }

    Add-SelfTestResult -Name 'Header and JSON assertions support regex and inequality' -Action {
        $scenario = [ordered]@{
            id     = 'regex-and-inequality'
            method = 'GET'
            url    = '/api/internal'
            expect = [ordered]@{
                status  = 200
                headers = [ordered]@{
                    'X-Correlation-Id' = [ordered]@{
                        exists    = $true
                        notEquals = 'this-value-must-not-roundtrip'
                        matches   = '^[0-9a-f]{32}$'
                    }
                }
                json    = @(
                    [ordered]@{
                        path      = '$.code'
                        notEquals = 'internal_auth_invalid_signature'
                        matches   = '^internal_auth_[a-z_]+$'
                    }
                )
            }
        }

        $failures = @(Test-ScenarioExpectations `
            -Scenario $scenario `
            -Variables @{} `
            -StatusCode 200 `
            -ResponseContentType 'application/json' `
            -ResponseBody '{"code":"internal_auth_replay_nonce"}' `
            -ResponseHeaders @{
                'X-Correlation-Id' = @('0123456789abcdef0123456789abcdef')
            } `
            -ResponseJson ([ordered]@{
                    code = 'internal_auth_replay_nonce'
                }))

        if ($failures.Count -ne 0) {
            throw ('Expected regex/inequality assertions to pass, but got: {0}' -f ($failures -join '; '))
        }
    }

    Add-SelfTestResult -Name 'Header assertions and extraction support chaining' -Action {
        $scenario = [ordered]@{
            id      = 'extract-and-chain'
            method  = 'GET'
            url     = '/api/auth/me'
            expect  = [ordered]@{
                status  = 200
                headers = [ordered]@{
                    'X-Correlation-Id' = [ordered]@{
                        equals = 'corr-123'
                    }
                    'Location'         = [ordered]@{
                        exists = $true
                    }
                }
                json    = @(
                    [ordered]@{
                        path   = '$.token'
                        equals = 'abc123'
                    }
                )
            }
            extract = [ordered]@{
                authToken       = '$.token'
                createdLocation = [ordered]@{
                    from = 'header'
                    name = 'Location'
                }
            }
        }

        $variables = @{}
        $responseHeaders = @{
            'X-Correlation-Id' = @('corr-123')
            'Location'         = @('/api/auth/me')
        }
        $responseJson = [ordered]@{
            token = 'abc123'
        }

        $failures = @(Test-ScenarioExpectations `
            -Scenario $scenario `
            -Variables $variables `
            -StatusCode 200 `
            -ResponseContentType 'application/json' `
            -ResponseBody '{"token":"abc123"}' `
            -ResponseHeaders $responseHeaders `
            -ResponseJson $responseJson)

        if ($failures.Count -ne 0) {
            throw ('Expected header assertions to pass, but got: {0}' -f ($failures -join '; '))
        }

        Update-ExtractedVariables -Scenario $scenario -Variables $variables -ResponseJson $responseJson -ResponseHeaders $responseHeaders

        if ($variables.authToken -ne 'abc123') {
            throw 'JSON extraction did not capture authToken.'
        }

        if ($variables.createdLocation -ne '/api/auth/me') {
            throw 'Header extraction did not capture Location.'
        }

        $authorizationHeader = Resolve-TemplateString -Template 'Bearer {{var:authToken}}' -Variables $variables
        if ($authorizationHeader -ne 'Bearer abc123') {
            throw 'Extracted token did not chain into the Authorization header.'
        }
    }

    Add-SelfTestResult -Name 'AllowNonLocal bypasses the local guard intentionally' -Action {
        Assert-LocalBaseUrl -BaseUri ([uri]'https://api.example.com') -AllowNonLocal
    }

    Add-SelfTestResult -Name 'Repeated request bodies support transport limits' -Action {
        $body = Build-RequestBody `
            -Scenario ([ordered]@{
                id = 'oversized-body'
                repeatBody = [ordered]@{
                    text = 'x'
                    count = 70000
                    contentType = 'application/json'
                }
            }) `
            -Variables @{} `
            -ManifestDirectory $PSScriptRoot

        if ($body.Content.Length -ne 70000 -or $body.ContentType -ne 'application/json' -or -not $body.ForceChunked) {
            throw 'Repeated request body generation failed.'
        }
    }

    Add-SelfTestResult -Name 'Sample manifest parses and validates' -Action {
        $sampleManifestPath = Join-Path -Path $PSScriptRoot -ChildPath 'sample.manifest.json'
        $manifest = Read-JsonDocument -Path $sampleManifestPath
        Assert-Manifest -Manifest $manifest
    }

    $resultArray = $results.ToArray()
    $passedCount = @($resultArray | Where-Object { $_.Passed }).Count
    $failedCount = $resultArray.Count - $passedCount

    Write-Host ''
    Write-Host ("Self-test summary: {0} total, {1} passed, {2} failed" -f $resultArray.Count, $passedCount, $failedCount) -ForegroundColor Cyan

    if ($failedCount -gt 0) {
        throw "Self-tests failed: $failedCount of $($resultArray.Count)."
    }

    return [pscustomobject]@{
        Total  = $resultArray.Count
        Passed = $passedCount
        Failed = $failedCount
        Tests  = $resultArray
    }
}

Export-ModuleMember -Function Invoke-HttpTestHarness, Invoke-HttpTestHarnessSelfTest
