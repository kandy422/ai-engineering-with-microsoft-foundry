#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Starter/Starter.csproj'
dotnet build $project --nologo --configfile (Join-Path $PSScriptRoot 'Starter/NuGet.Config')
if ($LASTEXITCODE) { throw 'Build failed' }
$program = Join-Path $PSScriptRoot 'Starter/bin/Debug/net10.0/Starter.dll'
$base = 'http://127.0.0.1:8401'
$evidence = Join-Path $PSScriptRoot '../qa'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$listenerCheck = [System.Net.Sockets.TcpClient]::new()
try {
  try { $listenerCheck.Connect('127.0.0.1', 8401) } catch [System.Net.Sockets.SocketException] {}
  if ($listenerCheck.Connected) { throw 'Port 8401 is already in use. Stop the process using it before running the starter.' }
} finally { $listenerCheck.Dispose() }
$server = Start-Process dotnet -ArgumentList ('"' + $program + '"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'starter-server.txt') -RedirectStandardError (Join-Path $evidence 'starter-server-errors.txt')
$prior = $env:ORDER_API_BASE
try {
  $ready = $false
  for ($i=0; $i -lt 30; $i++) {
    if ($server.HasExited) { throw 'Mock API exited early' }
    try {
      $probe = Invoke-WebRequest "$base/lookup" -Method Post -ContentType 'application/json' -Body '{"orderId":"ORD-1042"}' -TimeoutSec 1
      if ($probe.StatusCode -eq 200) { $ready=$true; break }
    } catch { Start-Sleep -Milliseconds 200 }
  }
  if (!$ready) { throw 'Mock API never became ready' }
  $env:ORDER_API_BASE = $base
  $ok = @(dotnet $program --check)
  $expected = @(
    'ORD-1042 200 {"orderId":"ORD-1042","status":"in_transit"}',
    'ORD-9999 404 {"error":"order not found"}',
    '         400 {"error":"orderId is required"}'
  )
  if ($LASTEXITCODE -ne 0 -or ($ok -join "`n") -cne ($expected -join "`n")) { throw "Unexpected client results: $ok" }
  $env:ORDER_API_BASE = 'missing-scheme'
  $broken = @(dotnet $program --check 2>&1 | ForEach-Object { "$_" })
  $code = $LASTEXITCODE
  if ($code -ne 2 -or $broken -notmatch 'CONFIG: ORDER_API_BASE') { throw 'Broken config check failed' }
  $env:ORDER_API_BASE = $base
  $fixed = @(dotnet $program --check)
  if ($LASTEXITCODE -ne 0 -or ($ok -join "`n") -cne ($fixed -join "`n")) { throw 'Recovery check failed' }
  [ordered]@{sdk=(dotnet --version);base=$base;success=$ok;broken=$broken;exitCode=$code;recovered=$fixed} |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidence 'starter-results.json') -Encoding utf8
  Get-Content (Join-Path $evidence 'starter-results.json')
  Write-Output 'PASS: local starter smoke checks'
} finally {
  $env:ORDER_API_BASE = $prior
  if (!$server.HasExited) { Stop-Process -Id $server.Id }
}

