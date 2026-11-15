#Requires -Version 7.2
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [guid] $Subscription,
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $App,
    [Parameter(Mandatory)] [string] $Receipt
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Receipt) { throw 'Choose a new path for this release receipt.' }
$baseline = "$App--baseline"
$candidate = "$App--candidate"
function Read-Az([string[]] $Arguments) {
    $result = az @Arguments --subscription $Subscription --only-show-errors -o json
    if ($LASTEXITCODE) { throw "Azure CLI failed at $($Arguments[0..2] -join ' ')." }
    $result | ConvertFrom-Json
}
function Read-App {
    Read-Az @('containerapp','show','-g',$ResourceGroup,'-n',$App)
}
function Confirm-Traffic([string] $Revision) {
    $traffic = @((Read-App).properties.configuration.ingress.traffic)
    $serving = @($traffic | Where-Object { $_.weight -gt 0 })
    if ($serving.Count -ne 1 -or $serving[0].revisionName -ne $Revision -or $serving[0].weight -ne 100) {
        throw "Main traffic did not resolve exclusively to $Revision."
    }
    $traffic
}
function Set-Traffic([string] $Revision) {
    $other = if ($Revision -eq $baseline) { $candidate } else { $baseline }
    $null = Read-Az @('containerapp','ingress','traffic','set','-g',$ResourceGroup,'-n',$App,
        '--revision-weight',"$Revision=100","$other=0")
    Confirm-Traffic $Revision
}
function Origin([string] $HostName) {
    if ($HostName -notmatch '^[a-z0-9.-]+\.azurecontainerapps\.io$') { throw 'Expected an Azure Container Apps origin.' }
    [uri] "https://$HostName/"
}
function Read-Revision([string] $Name) {
    $r = Read-Az @('containerapp','revision','show','-g',$ResourceGroup,'-n',$App,'--revision',$Name)
    if (!$r.properties.active) { throw "$Name is inactive." }
    $r
}
function Observe-Health([uri] $Origin, [string] $Index, [int] $Attempts = 1) {
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $result = Invoke-RestMethod -Uri ([uri]::new($Origin,'/health')) -MaximumRedirection 0 -TimeoutSec 45
        if ($result.mode -eq 'hosted' -and $result.status -eq 'ready' -and $result.index -eq $Index) { return $result }
        if ($attempt -lt $Attempts) { Start-Sleep -Seconds 3 }
    }
    throw "Expected healthy hosted route for $Index."
}
function Run-Case([uri] $Origin, [hashtable] $Case, [string] $Phase) {
    $trace = [guid]::NewGuid().ToString('N')
    $span = [guid]::NewGuid().ToString('N').Substring(0,16)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $reply = Invoke-WebRequest -Uri ([uri]::new($Origin,'/answer')) -Method Post -Body (@{text=$Case.text}|ConvertTo-Json -Compress) `
        -ContentType application/json -Authentication Bearer -Token $token -Headers @{traceparent="00-$trace-$span-01"} `
        -MaximumRedirection 0 -TimeoutSec 45 -SkipHttpErrorCheck
    $sw.Stop()
    $answer = $reply.Content | ConvertFrom-Json
    $observedTrace = [string] ($reply.Headers['X-Correlation-Id'] | Select-Object -First 1)
    $row = [ordered]@{ phase=$Phase; host=$Origin.DnsSafeHost; case=$Case.text; http=[int]$reply.StatusCode
        status=$answer.status; citation=$answer.citation; value=$answer.value; elapsedMs=$sw.ElapsedMilliseconds
        traceId=$trace; observedTraceId=$observedTrace; utc=[DateTimeOffset]::UtcNow.ToString('o') }
    $state.cases.Add($row)
    if ($row.http -ne 200 -or $row.status -ne $Case.status -or $row.citation -ne $Case.citation -or
        ($Case.value -and $row.value -ne $Case.value) -or $observedTrace -ne $trace) {
        throw "$Phase failed expected outcome or correlation for trace $trace."
    }
}
$prior = @(Confirm-Traffic $baseline)
$base = Read-Revision $baseline
$next = Read-Revision $candidate
$baseContainer = $base.properties.template.containers[0]
$nextContainer = $next.properties.template.containers[0]
if ($baseContainer.image -ne $nextContainer.image -or $nextContainer.image -notmatch '@sha256:[0-9a-f]{64}$') {
    throw 'The comparison requires the same immutable image on both revisions.'
}
$baseIndex = 'course16-support-v1'
$nextIndex = 'course16-support-candidate'
$baseOrigin = Origin $base.properties.fqdn
$nextOrigin = Origin $next.properties.fqdn
$mainOrigin = Origin (Read-App).properties.configuration.ingress.fqdn
if (!$PSCmdlet.ShouldProcess($App, 'Compare both revisions, temporarily route to candidate, then restore baseline')) { return }
if ([string]::IsNullOrWhiteSpace($env:CAPSTONE_TEST_TOKEN_A)) { throw 'CAPSTONE_TEST_TOKEN_A is required in the operator process.' }
$token = ConvertTo-SecureString $env:CAPSTONE_TEST_TOKEN_A -AsPlainText -Force
$cases = @(
    @{text='Can I send an unopened item back?';status='answered';citation='RET-30';value='Unopened items can be returned within 30 days.'},
    @{text='Can I send an opened item back after 90 days?';status='no_evidence';citation=$null},
    @{text='How long is the warranty?';status='conflicting_evidence';citation=$null},
    @{text='Do gift cards expire?';status='no_evidence';citation=$null}
)
$state = [ordered]@{
    baseline=$baseline; candidate=$candidate; image=$baseContainer.image; initialTraffic=$prior
    cases=[Collections.Generic.List[object]]::new(); promotedTraffic=$null; restoredTraffic=$null
    candidateMainHealth=$null; restoredMainHealth=$null; passed=$false; failedStep=$null
}
$shouldRestore = $false
try {
    $null = Observe-Health $baseOrigin $baseIndex
    $null = Observe-Health $nextOrigin $nextIndex
    foreach ($case in $cases) {
        Run-Case $baseOrigin $case 'baseline-direct'
        Run-Case $nextOrigin $case 'candidate-direct'
    }
    $shouldRestore = $true
    $state.promotedTraffic = @(Set-Traffic $candidate)
    $state.candidateMainHealth = Observe-Health $mainOrigin $nextIndex 5
    Run-Case $mainOrigin $cases[0] 'candidate-main'
    $state.passed = $true
} catch {
    $state.failedStep = $_.ScriptStackTrace
    throw
} finally {
    try {
        if ($shouldRestore) {
            $state.passed, $primaryPassed = $false, $state.passed
            $state.restoredTraffic = @(Set-Traffic $baseline)
            $state.restoredMainHealth = Observe-Health $mainOrigin $baseIndex 5
            Run-Case $mainOrigin $cases[0] 'restored-baseline-main'
            $state.passed = $primaryPassed
        }
    } finally {
        $state | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Receipt
        $token.Dispose()
    }
}
Get-Content -LiteralPath $Receipt -Raw
