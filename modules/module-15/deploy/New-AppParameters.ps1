#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $FoundationIds,
    [Parameter(Mandatory)] [string] $Output,
    [Parameter(Mandatory)] [ValidatePattern('^[a-z][a-z0-9-]{2,23}[a-z0-9]$')] [string] $App,
    [Parameter(Mandatory)] [ValidatePattern('^[a-z][a-z0-9-]{0,19}[a-z0-9]$')] [string] $Suffix,
    [Parameter(Mandatory)] [ValidatePattern('^sha256:[0-9a-f]{64}$')] [string] $Digest,
    [Parameter(Mandatory)] [guid] $Tenant,
    [Parameter(Mandatory)] [guid] $Audience,
    [Parameter(Mandatory)] [guid] $SpaClientId,
    [Parameter(Mandatory)] [hashtable] $Customers,
    [hashtable] $AutomationClients = @{},
    [Parameter(Mandatory)] [uri] $FoundryEndpoint,
    [Parameter(Mandatory)] [string] $Deployment,
    [Parameter(Mandatory)] [uri] $SearchEndpoint,
    [Parameter(Mandatory)] [string] $Index,
    [Parameter(Mandatory)] [string] $SemanticConfiguration,
    [string] $PreviousRevision = '',
    [switch] $EnableApplicationInsights
)
$ErrorActionPreference = 'Stop'
$ids = Get-Content -LiteralPath $FoundationIds -Raw | ConvertFrom-Json
foreach ($id in @('environment','identity','registryLogin','tableEndpoint','storageAccount','tableName')) {
    if (!$ids.$id) { throw "Foundation IDs are missing $id." }
}
if (!$Customers.Count -or $Customers.Count -gt 5) { throw 'Use one to five mapped test users.' }
if ($Tenant -eq [guid]::Empty -or $Audience -eq [guid]::Empty -or $SpaClientId -eq [guid]::Empty -or $Audience -eq $SpaClientId) {
    throw 'Use a tenant ID and separate nonempty API and SPA application IDs.'
}
if ($PreviousRevision -and !$PreviousRevision.StartsWith("$App--")) { throw 'Baseline revision must belong to this app.' }
if ($EnableApplicationInsights -and !$ids.insightsName) { throw 'Foundation IDs are missing insightsName.' }
$settings = @{
    Capstone__Mode = 'hosted'
    Identity__TenantId = "$Tenant"
    Identity__Audience = "$Audience"
    Identity__BrowserClientId = "$SpaClientId"
    Foundry__Endpoint = $FoundryEndpoint.AbsoluteUri
    Foundry__Deployment = $Deployment
    Search__Endpoint = $SearchEndpoint.AbsoluteUri
    Search__Index = $Index
    Search__SemanticConfiguration = $SemanticConfiguration
    Storage__Endpoint = $ids.tableEndpoint
    Storage__Table = $ids.tableName
}
$principalIds = [Collections.Generic.HashSet[guid]]::new()
$clientIds = [Collections.Generic.HashSet[guid]]::new()
$null = $clientIds.Add($SpaClientId)
foreach ($oid in $Customers.Keys) {
    $principal = [guid] $oid
    $customer = [string] $Customers[$oid]
    if ($principal -eq [guid]::Empty -or !$principalIds.Add($principal) -or $customer -notmatch '^[a-z0-9-]{1,100}$') {
        throw 'Use distinct user object IDs and synthetic customer labels.'
    }
    $settings["Identity__Customers__$principal"] = $customer
}
foreach ($oid in $AutomationClients.Keys) {
    $principal = [guid] $oid
    $client = [guid] $AutomationClients[$oid].ClientId
    $customer = [string] $AutomationClients[$oid].Customer
    if ($principal -eq [guid]::Empty -or !$principalIds.Add($principal) -or $client -eq [guid]::Empty -or
        $client -eq $Audience -or !$clientIds.Add($client) -or $customer -notmatch '^[a-z0-9-]{1,100}$') {
        throw 'Use distinct service principal object IDs, separate automation application IDs and synthetic customer labels.'
    }
    $settings["Identity__Customers__$principal"] = $customer
    $settings["Identity__Automation__$client"] = "$principal"
}
$values = @{
    name = $App
    environmentId = $ids.environment
    identityId = $ids.identity
    storageAccount = $ids.storageAccount
    applicationInsightsName = $EnableApplicationInsights ? $ids.insightsName : ''
    registryLogin = $ids.registryLogin
    imageDigest = $Digest
    suffix = $Suffix
    previousRevision = $PreviousRevision
    tenantId = "$Tenant"
    appClientId = "$Audience"
    allowedPrincipalIds = @($principalIds | Sort-Object | ForEach-Object ToString)
    allowedClientIds = @($clientIds | Sort-Object | ForEach-Object ToString)
    settings = $settings
}
$parameters = @{}
foreach ($entry in $values.GetEnumerator()) { $parameters[$entry.Key] = @{ value = $entry.Value } }
@{
    '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'
    contentVersion = '1.0.0.0'
    parameters = $parameters
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Output
