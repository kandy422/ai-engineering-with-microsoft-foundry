param([ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Run = 'local',[switch]$UiState)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
  if ($env:Foundry__Endpoint -or $env:Foundry__Deployment) {
    throw 'Unset the Foundry endpoint and deployment to run the local stub checks.'
  }
  if (Get-NetTCPConnection -LocalPort 8407 -State Listen -ErrorAction SilentlyContinue) {
    throw 'Port 8407 is occupied. Stop the existing listener before running the checks.'
  }
  New-Item -ItemType Directory -Force ../qa | Out-Null
  $labServer = Start-Process dotnet -ArgumentList @('bin/Debug/net10.0/SupportChat.dll','--urls','http://127.0.0.1:8407') -WindowStyle Hidden -PassThru -RedirectStandardOutput "../qa/$Run-server.log" -RedirectStandardError "../qa/$Run-server-errors.log"
  try {
    $ready = $false
    for ($attempt=0; $attempt -lt 25; $attempt++) {
      try { $ready = (Invoke-RestMethod http://127.0.0.1:8407/mode).mode -eq 'Local stub'; if ($ready) { break } }
      catch { Start-Sleep -Milliseconds 300 }
    }
    if (!$ready) { throw 'Lab server failed to start' }
    $script = if ($UiState) { "ui-state-check.mjs" } else { "smoke.mjs" }
    node $script | Set-Content "../qa/$Run-smoke.json"
    if ($LASTEXITCODE -ne 0) { throw 'Smoke test failed' }
  } finally { if (!$labServer.HasExited) { Stop-Process -Id $labServer.Id } }
} finally { Pop-Location }

