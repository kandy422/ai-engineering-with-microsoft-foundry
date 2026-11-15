#Requires -Version 7.2
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [guid] $Subscription,
    [Parameter(Mandatory)] [guid] $Principal,
    [Parameter(Mandatory)] [string] $FoundryAccountId,
    [Parameter(Mandatory)] [string] $SearchIndexId,
    [Parameter(Mandatory)] [string] $Receipt,
    [switch] $Remove,
    [switch] $SearchOnly
)
$ErrorActionPreference = 'Stop'
if ($FoundryAccountId -notmatch "^/subscriptions/$Subscription/resourceGroups/[^/]+/providers/Microsoft\.CognitiveServices/accounts/[^/]+$" -or
    $SearchIndexId -notmatch "^/subscriptions/$Subscription/resourceGroups/[^/]+/providers/Microsoft\.Search/searchServices/[^/]+/indexes/[a-z0-9-]+$") {
    throw 'Use the exact Foundry account and Search index scopes for the approved subscription.'
}
if ($Remove) {
    $previous = Get-Content -LiteralPath $Receipt -Raw | ConvertFrom-Json
    if ($previous.principal -ne "$Principal" -or $previous.subscription -ne "$Subscription") {
        throw 'Grant receipt belongs to a different identity or subscription.'
    }
}
$roles = [ordered]@{
    $FoundryAccountId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
    $SearchIndexId = '1407120a-92aa-4202-b7e9-c0e197c71c8f'
}
if ($SearchOnly) { $roles.Remove($FoundryAccountId) }
$created = @()
if (!$Remove -and (Test-Path -LiteralPath $Receipt)) {
    $existing = Get-Content -LiteralPath $Receipt -Raw | ConvertFrom-Json
    if ($existing.principal -ne "$Principal" -or $existing.subscription -ne "$Subscription") { throw 'Existing receipt is for a different deployment.' }
    $created = @($existing.grants)
}
foreach ($scope in $roles.Keys) {
    $role = $roles[$scope]
    if (!$PSCmdlet.ShouldProcess($scope, "$($Remove ? 'Remove' : 'Grant') role $role for $Principal")) { continue }
    if ($Remove) {
        $entry = $previous.grants | Where-Object { $_.scope -eq $scope -and $_.roleDefinitionId -like "*/$role" }
        if (!$entry) { continue }
        $expectedPrefix = "$scope/providers/Microsoft.Authorization/roleAssignments/"
        if ($entry.principalId -ne "$Principal" -or $entry.id -notmatch ('^' + [regex]::Escape($expectedPrefix) + '[a-f0-9-]{36}$')) {
            throw 'Grant receipt failed scope validation.'
        }
        az role assignment delete --ids $entry.id --subscription $Subscription --only-show-errors
        if ($LASTEXITCODE -ne 0) { throw "Failed to remove $scope grant." }
    } else {
        if ($created | Where-Object scope -eq $scope) { continue }
        $entry = az role assignment create --assignee-object-id $Principal --assignee-principal-type ServicePrincipal --role $role --scope $scope --subscription $Subscription --only-show-errors -o json
        if ($LASTEXITCODE -ne 0) { throw "Failed to create $scope grant." }
        $created += ($entry | ConvertFrom-Json)
        @{ principal = "$Principal"; subscription = "$Subscription"; grants = $created } |
            ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Receipt
    }
}
