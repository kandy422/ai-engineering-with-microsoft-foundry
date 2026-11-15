param([string]$EvidenceDir = "$PSScriptRoot/../evidence/upstream-v1", [switch]$LocalModel)
$ErrorActionPreference = 'Stop'
$EvidenceDir = [IO.Path]::GetFullPath($EvidenceDir)
if (Test-Path $EvidenceDir) { throw 'Choose a new evidence directory.' }
$source = Resolve-Path "$PSScriptRoot/../../module-01/lab/Starter"
New-Item -ItemType Directory -Force "$EvidenceDir/starter-source" | Out-Null
Copy-Item -LiteralPath "$source/Starter.csproj","$source/Program.cs" -Destination "$EvidenceDir/starter-source"
Get-FileHash "$EvidenceDir/starter-source/*" | Select-Object Hash,@{n='File';e={Split-Path $_.Path -Leaf}} | ConvertTo-Json | Set-Content "$EvidenceDir/starter-sha256.json"
dotnet publish "$EvidenceDir/starter-source/Starter.csproj" -c Release -p:UseAppHost=false -o "$EvidenceDir/app"
if ($LASTEXITCODE) { throw 'Starter publish failed.' }
$starter = Start-Process dotnet -ArgumentList 'Starter.dll' -WorkingDirectory "$EvidenceDir/app" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$EvidenceDir/starter.log" -RedirectStandardError "$EvidenceDir/starter-errors.log"
try {
    $ready = $false
    for ($i=0; $i -lt 60; $i++) {
        if ($starter.HasExited) { throw 'Starter exited before readiness.' }
        try {
            $result = Invoke-RestMethod http://127.0.0.1:8401/lookup -Method Post -ContentType application/json -Body '{"orderId":"ORD-1042"}' -TimeoutSec 1
            $ready = $result.status -eq 'in_transit'
        } catch { Start-Sleep -Milliseconds 250 }
        if ($ready) { break }
    }
    if (!$ready) { throw 'Starter did not become ready.' }
    & "$PSScriptRoot/run.ps1" -EvidenceDir "$EvidenceDir/capstone" -Upstream -LocalModel:$LocalModel
} finally {
    if (!$starter.HasExited) { Stop-Process -Id $starter.Id }
}
