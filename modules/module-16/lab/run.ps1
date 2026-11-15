param(
    [string]$EvidenceDir = "$PSScriptRoot/../evidence/run-$(Get-Date -Format yyyyMMdd-HHmmss-fff)",
    [string]$PublishedApp,
    [switch]$Unavailable,
    [switch]$Upstream,
    [switch]$InvalidDraft,
    [switch]$LocalModel
)
$ErrorActionPreference = 'Stop'
$EvidenceDir = [IO.Path]::GetFullPath($EvidenceDir)
if (Test-Path "$EvidenceDir/acceptance.json") { throw 'Choose a new evidence directory.' }
New-Item -ItemType Directory -Force $EvidenceDir | Out-Null
if (!$PublishedApp) {
    $PublishedApp = "$EvidenceDir/app"
    dotnet publish "$PSScriptRoot/Capstone.csproj" -c Release -p:UseAppHost=false -o $PublishedApp
    if ($LASTEXITCODE) { throw 'Publish failed.' }
}
$PublishedApp = (Resolve-Path $PublishedApp).Path
$launchArgs = @('Capstone.dll','--urls','http://127.0.0.1:8416')
if ($Unavailable) { $launchArgs += '--ForcePolicyUnavailable=true' }
if ($Upstream) { $launchArgs += '--UseStarterOrders=true' }
if ($InvalidDraft) { $launchArgs += '--ForceInvalidDraft=true' }
if ($LocalModel) { $launchArgs += '--UseLocalModel=true' }
$app = Start-Process dotnet -ArgumentList $launchArgs -WorkingDirectory $PublishedApp -WindowStyle Hidden -PassThru -RedirectStandardOutput "$EvidenceDir/server.log" -RedirectStandardError "$EvidenceDir/server-errors.log"
try {
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        if ($app.HasExited) { throw 'The published app exited before readiness.' }
        try { $health = Invoke-RestMethod http://127.0.0.1:8416/health -TimeoutSec 1; $ready = $health.status -eq 'ready' } catch { Start-Sleep -Milliseconds 250 }
        if ($ready) { break }
    }
    if (!$ready) { throw 'The published app did not become ready.' }
    $page = Invoke-WebRequest http://127.0.0.1:8416/ -TimeoutSec 5
    $client = Invoke-WebRequest http://127.0.0.1:8416/desk.js -TimeoutSec 5
    $clientServed = $page.Content.Contains('id="policy-result" role="status"') -and $client.Content.Contains('fetch(route')
    if (!$clientServed) { throw 'The published client was unavailable.' }
    @{ pageStatus=$page.StatusCode; clientStatus=$client.StatusCode; statusRegionPresent=$clientServed } | ConvertTo-Json | Set-Content "$EvidenceDir/client-smoke.json"
    & "$PSScriptRoot/acceptance.ps1" -Output "$EvidenceDir/acceptance.json" -Upstream:$Upstream -InvalidDraft:$InvalidDraft | Out-Null
    $report = Get-Content "$EvidenceDir/acceptance.json" -Raw | ConvertFrom-Json
    $report | Select-Object mode,total,passed,failed
    Write-Host "Report: $EvidenceDir/acceptance.json"
    if ($report.failed -gt 0) { throw 'Acceptance checks failed.' }
} finally {
    if (!$app.HasExited) { Stop-Process -Id $app.Id }
}
