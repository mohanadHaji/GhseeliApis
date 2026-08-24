#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$PlanPath = '.\STEP_16_HTTP_TEST_PLAN.md',
    [string]$ManifestPath =
        '.\scripts\http-tests\plans\step-16-clean-schema-separation.manifest.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$plan = Get-Content -LiteralPath $PlanPath -Encoding UTF8
$manifestText = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8
$manifest = $manifestText | ConvertFrom-Json

$rows = @($plan | Where-Object { $_.StartsWith('| ') -and $_.Contains('STEP16-') } |
    ForEach-Object {
        $parts = $_.Split('|')
        if ($parts.Count -ge 4) {
            $id = $parts[1].Trim().Trim([char]96)
            if ($id.StartsWith('STEP16-')) {
                [pscustomobject]@{ Id = $id; Level = $parts[2].Trim() }
            }
        }
    })
$allIds = @($rows.Id)
$liveIds = @($rows | Where-Object { $_.Level -match '(?i)live-local' } |
    ForEach-Object Id)
if ($allIds.Count -ne 168 -or @($allIds | Sort-Object -Unique).Count -ne 168) {
    throw "Expected 168 unique frozen Step 16 IDs; found $($allIds.Count)."
}
if ($liveIds.Count -ne 76) {
    throw "Expected 76 frozen live-local Step 16 IDs; found $($liveIds.Count)."
}

$scenarios = @($manifest.scenarios)
$lifecycleScenarios = @($manifest.lifecycleScenarios)
$scenarioIds = [Collections.Generic.HashSet[string]]::new(
    [string[]]$scenarios.id, [StringComparer]::Ordinal)
if ($scenarios.Count -ne 790) {
    throw "Expected 790 audited HTTP entries; found $($scenarios.Count)."
}
if (@($scenarios.id | Sort-Object -Unique).Count -ne $scenarios.Count) {
    throw 'Manifest entry IDs must be unique.'
}
$mapped = @($scenarios.planScenarioId + $lifecycleScenarios.planScenarioId |
    Sort-Object -Unique)
$missing = @($liveIds | Where-Object { $_ -notin $mapped })
$unexpected = @($mapped | Where-Object { $_ -notin $liveIds })
if ($missing.Count -or $unexpected.Count) {
    throw "Live mapping mismatch. Missing: $($missing -join ', '); unexpected: $($unexpected -join ', ')."
}

$requiredSensitive = @(
    'authorization', 'token', 'password', 'secret', 'connectionstring',
    'x-device-token', 'x-ghseeli-signature', 'x-ghseeli-nonce',
    'idempotency-key', 'stripe-signature', 'email')
foreach ($field in $requiredSensitive) {
    if ($field -notin @($manifest.sensitiveFields | ForEach-Object {
                ([string]$_).ToLowerInvariant()
            })) {
        throw "Sensitive field '$field' is not redacted."
    }
}
if ($manifestText -match '(?i)(sk_live_|whsec_[A-Za-z0-9]|password\s*=\s*[^<{"]|Data Source=.*prod)') {
    throw 'Manifest contains a committed credential or production marker.'
}

$allowedOrigins = @(
    '{{var:customerBaseUrl}}', '{{var:businessBaseUrl}}',
    '{{var:customerProductionBaseUrl}}', '{{var:businessProductionBaseUrl}}',
    '{{var:businessHttpBaseUrl}}', '{{var:customerSecondBaseUrl}}',
    '{{var:businessSecondBaseUrl}}')
$knownVariables = @(
    'customerBaseUrl', 'businessBaseUrl', 'customerProductionBaseUrl',
    'businessProductionBaseUrl', 'businessHttpBaseUrl', 'customerJwt',
    'businessJwt', 'customerDeviceToken', 'customerUserId', 'businessUserId',
    'customerAdminJwt', 'obsoleteCompanyJwt', 'initialDeviceToken',
    'rotatedDeviceToken',
    'customerDeviceId', 'deviceInstallationId', 'unknownReference',
    'customerSecondBaseUrl', 'businessSecondBaseUrl',
    'companyId', 'fixtureRunId', 'unknownId', 'correlationId')
$concreteValidated = $false
foreach ($scenario in $scenarios) {
    if ($scenario.feature -ne 'step16' -or $scenario.tags -notcontains 'live-local') {
        throw "Scenario '$($scenario.id)' must be Step 16 and tagged live-local."
    }

    if (-not $concreteValidated) {
    if ($lifecycleScenarios.Count -ne 4) {
        throw 'Expected cleanup, production schema, and two missing-connection lifecycle scenarios.'
    }
    $cleanupLifecycle = @($lifecycleScenarios | Where-Object {
            $_.planScenarioId -eq 'STEP16-CLEANUP-ISOLATION-163'
        })
    if ($cleanupLifecycle.Count -ne 1 -or
        $cleanupLifecycle[0].operation -ne
            'stop-owned-processes-clear-pools-drop-run-databases-remove-runtime-files' -or
        @($cleanupLifecycle[0].evidence).Count -ne 4) {
        throw 'Cleanup isolation must be lifecycle evidence, not a representative HTTP request.'
    }
    $missingConnectionLifecycle = @($lifecycleScenarios | Where-Object {
            $_.planScenarioId -in @(
                'STEP16-START-MISSING-CONNECTION-CUSTOMER-052',
                'STEP16-START-MISSING-CONNECTION-BUSINESS-053')
        })
    if ($missingConnectionLifecycle.Count -ne 2 -or
        @($missingConnectionLifecycle | Where-Object {
                @($_.evidence).Count -ne 3
            }).Count) {
        throw 'Both missing-connection startup failures require explicit lifecycle evidence.'
    }
    $productionLifecycle = @($lifecycleScenarios | Where-Object {
            $_.id -eq
                'STEP16-MIGRATION-NO-AUTO-PRODUCTION-024--incompatible-schema-unchanged'
        })
    if ($productionLifecycle.Count -ne 1 -or
        @($productionLifecycle[0].evidence).Count -ne 3) {
        throw 'Production incompatible-schema no-migration evidence is incomplete.'
    }

    $requiredCounts = [ordered]@{
        'STEP16-HEALTH-CUSTOMER-060' = 6
        'STEP16-HEALTH-BUSINESS-061' = 4
        'STEP16-SWAGGER-ENVIRONMENTS-063' = 8
        'STEP16-ROOT-INDEPENDENT-064' = 4
        'STEP16-AUTH-CUSTOMER-TOKEN-070' = 2
        'STEP16-AUTH-BUSINESS-TOKEN-071' = 2
        'STEP16-AUTH-HMAC-ONLY-074' = 3
        'STEP16-SEED-IDEMPOTENT-038' = 4
        'STEP16-START-CUSTOMER-ONLY-045' = 2
        'STEP16-START-BUSINESS-ONLY-046' = 2
        'STEP16-START-BOTH-047' = 4
        'STEP16-START-STOP-CUSTOMER-048' = 2
        'STEP16-START-STOP-BUSINESS-049' = 2
        'STEP16-DB-OUTAGE-CUSTOMER-056' = 3
        'STEP16-DB-OUTAGE-BUSINESS-057' = 2
        'STEP16-DB-RECOVERY-CUSTOMER-058' = 2
        'STEP16-DB-RECOVERY-BUSINESS-059' = 2
        'STEP16-CUSTOMER-AUTH-077' = 10
        'STEP16-CUSTOMER-ADDRESSES-079' = 6
        'STEP16-CUSTOMER-VEHICLES-080' = 5
        'STEP16-CUSTOMER-DEVICES-081' = 3
        'STEP16-BUSINESS-AUTH-097' = 2
        'STEP16-REMOVED-COMPANIES-READS-113' = 3
        'STEP16-REMOVED-COMPANIES-WRITES-114' = 15
        'STEP16-REMOVED-SERVICES-WRITES-116' = 12
        'STEP16-REMOVED-OPTIONS-WRITES-118' = 3
        'STEP16-REMOVED-BOOKINGS-CUSTOMER-119' = 30
        'STEP16-REMOVED-BOOKINGS-BUSINESS-120' = 30
        'STEP16-REMOVED-COMPANY-ROLE-122' = 3
        'STEP16-FOREIGN-CUSTOMER-AUTH-USERS-127' = 22
        'STEP16-FOREIGN-DEVICE-CONFIG-129' = 2
        'STEP16-FOREIGN-DRAFT-PRICING-131' = 5
        'STEP16-FOREIGN-PAYMENTS-STRIPE-133' = 10
        'STEP16-FOREIGN-HEALTH-NAMES-135' = 3
        'STEP16-INTEGRATION-HMAC-SUCCESS-139' = 2
        'STEP16-INTEGRATION-OPPOSITE-DOWN-141' = 2
        'STEP16-INTEGRATION-CUSTOMER-DOWN-142' = 2
        'STEP16-LEGACY-WRONG-METHOD-151' = 22
        'STEP16-LEGACY-HEAD-OPTIONS-152' = 54
        'STEP16-LEGACY-CACHE-SECURITY-154' = 9
        'STEP16-ADVERSE-CUSTOMER-TABLE-MISSING-155' = 2
        'STEP16-ADVERSE-BUSINESS-TABLE-MISSING-156' = 2
        'STEP16-ADVERSE-LOGIN-DENIED-159' = 2
        'STEP16-ADVERSE-CONCURRENT-HOSTS-160' = 4
        'STEP16-ADVERSE-RESTART-IDEMPOTENCY-161' = 4
        'STEP16-ADVERSE-RESET-SEQUENCE-162' = 8
        'STEP16-FINAL-REGRESSION-164' = 8
        'STEP16-REMOVED-WALLET-NOTIFICATION-ROUTES-167' = 420
        'STEP16-REMOVED-WALLET-NOTIFICATION-STARTUP-168' = 7
    }
    foreach ($requirement in $requiredCounts.GetEnumerator()) {
        $actual = @($scenarios | Where-Object planScenarioId -eq $requirement.Key).Count
        if ($actual -ne $requirement.Value) {
            throw "Concrete coverage for '$($requirement.Key)' requires $($requirement.Value) entries; found $actual."
        }
    }

    $auth077 = @($scenarios | Where-Object {
            $_.planScenarioId -eq 'STEP16-CUSTOMER-AUTH-077'
        })
    $authOperations = @($auth077 | ForEach-Object {
            "$($_.method.ToUpperInvariant()) $($_.url)"
        } | Sort-Object -Unique)
    $requiredAuthOperations = @(
        'POST {{var:customerBaseUrl}}/api/Auth/register',
        'POST {{var:customerBaseUrl}}/api/Auth/login',
        'POST {{var:customerBaseUrl}}/api/Auth/validate',
        'GET {{var:customerBaseUrl}}/api/Auth/external-login',
        'GET {{var:customerBaseUrl}}/api/Auth/external-login-callback',
        'GET {{var:customerBaseUrl}}/api/Auth/me',
        'POST {{var:customerBaseUrl}}/api/Auth/link-external-login',
        'GET {{var:customerBaseUrl}}/api/Auth/link-external-login-callback',
        'DELETE {{var:customerBaseUrl}}/api/Auth/external-login/Google',
        'GET {{var:customerBaseUrl}}/api/Auth/external-logins')
    if (@($requiredAuthOperations | Where-Object { $_ -notin $authOperations }).Count) {
        throw 'STEP16-CUSTOMER-AUTH-077 does not execute all ten retained Auth operations.'
    }
    $invalidValidate = @($auth077 | Where-Object {
            $_.id -eq 'STEP16-CUSTOMER-AUTH-077--03-validate-invalid-json-string'
        })
    if ($invalidValidate.Count -ne 1 -or
        $invalidValidate[0].jsonBody -isnot [string] -or
        $invalidValidate[0].jsonBody -cne 'invalid' -or
        $invalidValidate[0].expect.status -ne 401) {
        throw 'Auth validate invalid-token probe must send the JSON string "invalid" and expect 401.'
    }
    $customerSwagger = @($scenarios | Where-Object {
            $_.planScenarioId -eq 'STEP16-SWAGGER-CUSTOMER-EXACT-067'
        })
    $businessSwagger = @($scenarios | Where-Object {
            $_.planScenarioId -eq 'STEP16-SWAGGER-BUSINESS-EXACT-068'
        })
    if ($customerSwagger.Count -ne 1 -or
        @($customerSwagger[0].expect.bodyContains).Count -ne 44 -or
        $businessSwagger.Count -ne 1 -or
        @($businessSwagger[0].expect.bodyContains).Count -ne 26) {
        throw 'Exact Swagger probes must assert every retained Customer and Business path.'
    }

    $credentialNames = @('anonymous','customer','admin','obsolete-company','business')
    foreach ($matrix in @(
            @{ Id='STEP16-REMOVED-COMPANIES-WRITES-114';
                Operations=@('create','update','delete') },
            @{ Id='STEP16-REMOVED-BOOKINGS-CUSTOMER-119';
                Operations=@('mine','upcoming','history','id','cancel','availability') },
            @{ Id='STEP16-REMOVED-BOOKINGS-BUSINESS-120';
                Operations=@('company','create','update','confirm','start','complete') })) {
        foreach ($operation in $matrix.Operations) {
            foreach ($credential in $credentialNames) {
                $expectedId = "$($matrix.Id)--$operation--$credential"
                if (-not $scenarioIds.Contains($expectedId)) {
                    throw "Missing concrete credential-matrix entry '$expectedId'."
                }
            }
        }
    }

    $walletPaths = @('wallet','wallet-id','wallets','wallets-id','transactions',
        'transactions-id','notification','notification-id','notifications',
        'notifications-id')
    $walletMethods = @('get','post','put','patch','delete','head','options')
    $walletCredentials = @('anonymous','customer','admin','business','device','hmac')
    foreach ($path in $walletPaths) {
        foreach ($method in $walletMethods) {
            foreach ($credential in $walletCredentials) {
                $expectedId =
                    "STEP16-REMOVED-WALLET-NOTIFICATION-ROUTES-167--$path-$method-$credential"
                if (-not $scenarioIds.Contains($expectedId)) {
                    throw "Missing exhaustive wallet/notification entry '$expectedId'."
                }
            }
        }
    }
    $concreteValidated = $true
    }
    if ([string]::IsNullOrWhiteSpace($scenario.method) -or
        [string]::IsNullOrWhiteSpace($scenario.url) -or
        $null -eq $scenario.expect.status) {
        throw "Scenario '$($scenario.id)' is missing executable HTTP fields."
    }
    if (-not @($allowedOrigins | Where-Object { $scenario.url.StartsWith($_) }).Count) {
        throw "Scenario '$($scenario.id)' does not use an approved exact local host variable."
    }
    if (@('GET','HEAD','POST','PUT','PATCH','DELETE','OPTIONS') -notcontains
        ([string]$scenario.method).ToUpperInvariant()) {
        throw "Scenario '$($scenario.id)' has an unsupported HTTP method."
    }
    $assertions = @('contentType','headers','json','errorCode','bodyEquals',
        'bodyContains','bodyNotContains')
    if (-not @($assertions | Where-Object {
                $scenario.expect.psobject.Properties.Name -contains $_
            }).Count) {
        throw "Scenario '$($scenario.id)' is status-only."
    }
    if (@($scenario.expect.bodyNotContains).Count -eq 0) {
        throw "Scenario '$($scenario.id)' lacks leakage assertions."
    }
    $overbroadLeakTerms = @(
        'server','table','password','token','signature','email')
    if (@($overbroadLeakTerms | Where-Object {
                $_ -cin @($scenario.expect.bodyNotContains)
            }).Count -eq $overbroadLeakTerms.Count) {
        throw "Scenario '$($scenario.id)' uses generic contract words as leakage markers."
    }
}

$businessDown = @($scenarios | Where-Object {
        $_.id -ceq 'STEP16-INTEGRATION-OPPOSITE-DOWN-141--integration-fails-closed'
    })
if ($businessDown.Count -ne 1 -or
    $null -eq $businessDown[0].expect.psobject.Properties['errorCode'] -or
    $businessDown[0].expect.errorCode -cne 'idempotency_unavailable') {
    throw 'The Business-down dependency probe must require idempotency_unavailable.'
}

$catalogCredentialScenarios = @($scenarios | Where-Object {
        $_.id -clike 'STEP16-AUTH-HMAC-ONLY-074--*' -and
        $_.id -cne 'STEP16-AUTH-HMAC-ONLY-074--credential-substitution-rejected'
    })
if (@($catalogCredentialScenarios | Where-Object {
            $_.internalAuth.secretEnv -cne
                'STEP16_CUSTOMER_TO_BUSINESS_CATALOG_HMAC_SECRET'
        }).Count) {
    throw 'Catalog-only probes must use the restricted credential secret.'
}
$credentialSubstitution = @($scenarios | Where-Object {
        $_.id -ceq 'STEP16-AUTH-HMAC-ONLY-074--credential-substitution-rejected'
    })
if ($credentialSubstitution.Count -ne 1 -or
    $credentialSubstitution[0].expect.status -ne 401 -or
    $credentialSubstitution[0].internalAuth.serviceId -cne
        '{{env:STEP16_CUSTOMER_TO_BUSINESS_SERVICE_ID}}' -or
    $credentialSubstitution[0].internalAuth.secretEnv -cne
        'STEP16_CUSTOMER_TO_BUSINESS_CATALOG_HMAC_SECRET') {
    throw 'Scenario 074 must reject catalog-secret substitution under the broad service ID.'
}

$reconciliationScenarios = @($scenarios | Where-Object {
        $_.id -in @(
            'STEP16-INTEGRATION-HMAC-SUCCESS-139--business-to-customer',
            'STEP16-INTEGRATION-OPPOSITE-DOWN-141--integration-fails-closed')
    })
if ($reconciliationScenarios.Count -ne 2 -or
    @($reconciliationScenarios | Where-Object {
            $_.internalAuth.serviceId -cne
                '{{env:STEP16_BUSINESS_TO_CUSTOMER_RECONCILE_SERVICE_ID}}' -or
            $_.internalAuth.secretEnv -cne
                'STEP16_BUSINESS_TO_CUSTOMER_RECONCILE_HMAC_SECRET'
        }).Count) {
    throw 'Reconciliation probes must use a dedicated restricted credential.'
}

$referenced = @([regex]::Matches($manifestText,
        '\{\{var:([A-Za-z0-9_.-]+)\}\}') | ForEach-Object {
        $_.Groups[1].Value
    } | Sort-Object -Unique)
$unknown = @($referenced | Where-Object { $_ -notin $knownVariables -and
        $_ -notin @($manifest.setupVariables.psobject.Properties.Name) })
if ($unknown.Count) { throw "Unknown manifest variables: $($unknown -join ', ')." }
$unsupported = @([regex]::Matches($manifestText, '\{\{([^{}]+)\}\}') |
    ForEach-Object { $_.Groups[1].Value } | Where-Object {
        $_ -notmatch '^var:[A-Za-z0-9_.-]+$' -and
        $_ -notmatch '^env:[A-Za-z_][A-Za-z0-9_]*$' -and
        $_ -notmatch '^gen:(guid|nonce|iso8601(?::[^{}]+)?|timestamp(?::[^{}]+)?)$'
    })
if ($unsupported.Count) { throw "Unsupported template tokens: $($unsupported -join ', ')." }

$scriptRoot = Split-Path -Parent (Resolve-Path -LiteralPath $ManifestPath).Path
$parseFailures = [Collections.Generic.List[string]]::new()
foreach ($script in Get-ChildItem -LiteralPath $scriptRoot -Filter '*.ps1' -File) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile(
        $script.FullName, [ref]$tokens, [ref]$errors)
    foreach ($error in @($errors)) { $parseFailures.Add("$($script.Name): $($error.Message)") }
}
if ($parseFailures.Count) { throw ($parseFailures -join [Environment]::NewLine) }

Write-Host ("Step 16 assets passed: 168 frozen IDs, 76 live-local mappings, " +
    "$($scenarios.Count) HTTP entries and $($lifecycleScenarios.Count) lifecycle entries.")
