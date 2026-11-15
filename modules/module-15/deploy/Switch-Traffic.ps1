#Requires -Version 7.2
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $App,
    [Parameter(Mandatory)] [string] $FromRevision,
    [Parameter(Mandatory)] [string] $ToRevision,
    [Parameter(Mandatory)] [securestring] $AccessToken,
    [ValidateRange(1, 100)] [int] $ToWeight = 100
)
$ErrorActionPreference = 'Stop'
function Invoke-AzJson([string[]] $Arguments) {
    $result = az @Arguments --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Azure CLI operation failed.' }
    $result | ConvertFrom-Json
}
if ($FromRevision -eq $ToRevision -or !$FromRevision.StartsWith("$App--") -or !$ToRevision.StartsWith("$App--")) {
    throw 'Provide two distinct full revision names for this app.'
}
$from = Invoke-AzJson @('containerapp','revision','show','-g',$ResourceGroup,'-n',$App,'--revision',$FromRevision)
$to = Invoke-AzJson @('containerapp','revision','show','-g',$ResourceGroup,'-n',$App,'--revision',$ToRevision)
if (!$from.properties.active -or !$to.properties.active -or !$to.properties.fqdn) {
    throw 'Both revisions must be active and the destination must have a revision URL.'
}
$origin = [uri] ('https://' + $to.properties.fqdn)
if (!$origin.DnsSafeHost.EndsWith('.azurecontainerapps.io') -or !$origin.IsDefaultPort -or $origin.UserInfo) {
    throw 'Destination revision did not return an Azure Container Apps host.'
}
$request = @{
    Uri = [uri]::new($origin, '/answer')
    Method = 'Post'
    Body = '{"text":""}'
    ContentType = 'application/json'
    MaximumRedirection = 0
    TimeoutSec = 60
}
$anonymous = Invoke-WebRequest @request -SkipHttpErrorCheck
if ($anonymous.StatusCode -ne 401) { throw 'The destination data endpoint did not reject an anonymous caller.' }
$authorized = Invoke-RestMethod @request -Authentication Bearer -Token $AccessToken
if ($authorized.status -ne 'clarify') { throw 'The destination did not confirm authenticated API access.' }
$health = Invoke-RestMethod -Uri ([uri]::new($origin, '/health')) -MaximumRedirection 0 -TimeoutSec 60
if ($health.status -ne 'ready' -or $health.mode -ne 'hosted') { throw 'Destination did not confirm hosted readiness.' }
if ($PSCmdlet.ShouldProcess($App, "Set $ToRevision to ${ToWeight}%")) {
    $current = (Invoke-AzJson @('containerapp','show','-g',$ResourceGroup,'-n',$App)).properties.configuration.ingress.traffic
    if ($current | Where-Object { $_.weight -gt 0 -and $_.revisionName -notin @($FromRevision,$ToRevision) }) {
        throw 'An unexpected revision is serving traffic. Reconcile active routes first.'
    }
    $null = Invoke-AzJson @('containerapp','ingress','traffic','set','-g',$ResourceGroup,'-n',$App,'--revision-weight',"$FromRevision=$(100 - $ToWeight)","$ToRevision=$ToWeight")
    $appState = Invoke-AzJson @('containerapp','show','-g',$ResourceGroup,'-n',$App)
    $traffic = @($appState.properties.configuration.ingress.traffic)
    if (($traffic | Where-Object revisionName -eq $ToRevision).weight -ne $ToWeight -or
        [int] (($traffic | Where-Object revisionName -eq $FromRevision).weight) -ne (100 - $ToWeight) -or
        ($traffic | Where-Object { $_.revisionName -notin @($FromRevision,$ToRevision) -and $_.weight -gt 0 })) {
        throw 'Recorded traffic did not match the requested routing.'
    }
    [pscustomobject]@{ destination = $ToRevision; health = $health; traffic = $traffic }
}
