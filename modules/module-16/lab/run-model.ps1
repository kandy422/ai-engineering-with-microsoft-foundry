param([string]$EvidenceDir = "$PSScriptRoot/../evidence/model-v1")
$ErrorActionPreference = 'Stop'
$EvidenceDir = [IO.Path]::GetFullPath($EvidenceDir)
if (Test-Path $EvidenceDir) { throw 'Choose a new evidence directory.' }
$tools = Resolve-Path "$PSScriptRoot/../../module-08/tools"
$exe = (Get-ChildItem "$tools/llama" -Recurse -Filter llama-server.exe | Select-Object -First 1).FullName
if (!$exe) { throw 'Module 08 llama.cpp checkpoint is missing.' }
$model = (Resolve-Path "$tools/models/qwen-1.5.gguf").Path
$hash = (Get-FileHash -LiteralPath $model -Algorithm SHA256).Hash
if ($hash -ne '6A1A2EB6D15622BF3C96857206351BA97E1AF16C30D7A74EE38970E434E9407E') { throw 'Model weights differ from Module 06/08 handoff.' }
New-Item -ItemType Directory -Force $EvidenceDir | Out-Null
@{ name='Qwen2.5-1.5B-Instruct Q4_K_M'; sha256=$hash; version='a615a81362316d7b9f5a7a9c4313adfdf9b54588'; server='llama.cpp b12287'; port=8516 } | ConvertTo-Json | Set-Content "$EvidenceDir/runtime.json"
$server = Start-Process -FilePath $exe -ArgumentList ('-m "{0}" --host 127.0.0.1 --port 8516 -c 2048 -t 4 -ngl 0 -np 1 --alias capstone-qwen' -f $model) -WorkingDirectory $EvidenceDir -WindowStyle Hidden -PassThru -RedirectStandardOutput "$EvidenceDir/model.log" -RedirectStandardError "$EvidenceDir/model-errors.log"
try {
    $ready = $false
    for ($i=0; $i -lt 60; $i++) {
        if ($server.HasExited) { throw 'Local model server exited before readiness.' }
        try { $null = Invoke-RestMethod http://127.0.0.1:8516/health -TimeoutSec 1; $ready = $true; break } catch { Start-Sleep -Seconds 1 }
    }
    if (!$ready) { throw 'Local model server did not become ready.' }
    & "$PSScriptRoot/run-upstream.ps1" -EvidenceDir "$EvidenceDir/integration" -LocalModel
    Stop-Process -Id $server.Id
    $server.WaitForExit()
    # Re-run against a genuinely stopped model service. The gate must fail on
    # answerable cases while the captured result remains available to review.
    $rejected = $false
    try {
        & "$PSScriptRoot/run-upstream.ps1" -EvidenceDir "$EvidenceDir/model-down" -LocalModel
    } catch {
        if ($_.Exception.Message -ne 'Acceptance checks failed.') { throw }
        $rejected = $true
    }
    if (!$rejected) { throw 'The gate accepted the unavailable model.' }
    $outage = Get-Content "$EvidenceDir/model-down/capstone/acceptance.json" -Raw | ConvertFrom-Json
    $failed = @($outage.cases | Where-Object { !$_.pass })
    if ($failed.Count -ne 2 -or @($failed | Where-Object { $_.actual.status -ne 'unavailable' }).Count -ne 0) {
        throw 'Unexpected outage outcomes.'
    }
    'Model outage: two answerable cases returned unavailable. All other cases matched.'
} finally {
    if (!$server.HasExited) { Stop-Process -Id $server.Id }
}
