param([string]$EvidenceDir = "$PSScriptRoot/../evidence/attachments-v1")
$ErrorActionPreference = 'Stop'
$EvidenceDir = [IO.Path]::GetFullPath($EvidenceDir)
if (Test-Path "$EvidenceDir/decisions.json") { throw 'Choose a new evidence directory.' }
New-Item -ItemType Directory -Force $EvidenceDir | Out-Null
dotnet publish "$PSScriptRoot/Capstone.csproj" -c Release -p:UseAppHost=false -o "$EvidenceDir/app"
if ($LASTEXITCODE) { throw 'Publish failed.' }
$process = Start-Process dotnet -ArgumentList @('Capstone.dll','--urls','http://127.0.0.1:8416') -WorkingDirectory "$EvidenceDir/app" -PassThru -WindowStyle Hidden
try {
    $ready = $false
    for ($attempt=0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { throw 'The application exited before readiness.' }
        try { $null = Invoke-RestMethod http://127.0.0.1:8416/health -TimeoutSec 1; $ready = $true; break } catch { Start-Sleep -Milliseconds 250 }
    }
    if (!$ready) { throw 'The application did not become ready.' }
    $cases = @(
        @{label='JPEG metadata'; mime='image/jpeg'; bytes=340000; state='routed'; target='receipt extraction'}
        @{label='Over the byte limit'; mime='image/jpeg'; bytes=2000001; state='rejected'; target='app byte limit'}
        @{label='Unsupported media'; mime='image/png'; bytes=340000; state='rejected'; target='unsupported task or format'}
    )
    $rows = foreach ($case in $cases) {
        $request = @{mime=$case.mime;task='extract';bytes=$case.bytes}
        $actual = Invoke-RestMethod http://127.0.0.1:8416/attachment-check -Method Post -ContentType application/json -Body (ConvertTo-Json $request -Compress)
        @{name=$case.label;input=$request;actual=$actual;pass=$actual.state -eq $case.state -and $actual.target -eq $case.target}
    }
    ConvertTo-Json -InputObject @($rows) -Depth 5 | Set-Content -Encoding utf8 "$EvidenceDir/decisions.json"
    if (@($rows | Where-Object {!$_.pass}).Count) { throw 'Attachment preflight expectations failed.' }
    'Three attachment metadata cases passed.'
} finally {
    if (!$process.HasExited) { Stop-Process -Id $process.Id }
}
