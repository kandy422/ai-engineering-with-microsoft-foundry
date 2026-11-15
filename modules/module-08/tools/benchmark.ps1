param([Parameter(Mandatory=$true)][ValidateSet('0.5','1.5')][string]$Size,[switch]$Resmoke)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot)
$exe = (Get-ChildItem tools/llama -Recurse -Filter llama-server.exe | Select-Object -First 1).FullName
$model = (Resolve-Path "tools/models/qwen-$Size.gguf").Path
$port = 8408
$server = Start-Process -FilePath $exe -ArgumentList ('-m "{0}" --host 127.0.0.1 --port {1} -c 2048 -t 4 -ngl 0 -np 1 --alias qwen-{2}' -f $model,$port,$Size) -PassThru -WindowStyle Hidden -RedirectStandardOutput "evidence/server-$Size-$($Resmoke.IsPresent).log" -RedirectStandardError "evidence/server-$Size-$($Resmoke.IsPresent).err.log"
try {
    $base = "http://127.0.0.1:$port/"
    $ready = $false
    foreach ($attempt in 1..60) {
        if ($server.HasExited) { throw "llama-server exited. See evidence/server-$Size.err.log." }
        try { $null = Invoke-RestMethod "${base}health" -TimeoutSec 1; $ready = $true; break } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw 'llama-server did not become ready' }
    if ($Resmoke) {
        dotnet run --project lab/SupportLab.csproj -- smoke $base "qwen-$Size" v2 | Set-Content -Encoding utf8 "evidence/smoke-$Size-v2.json"
        if ($LASTEXITCODE -notin 0,2) { throw 'Recovery smoke command failed' }
        return
    }
    dotnet run --project lab/SupportLab.csproj -- benchmark lab/benchmark.json $base "qwen-$Size" 3 | Set-Content -Encoding utf8 "evidence/benchmark-$Size.json"
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed' }
    if ($Size -eq '1.5') {
        foreach ($set in 'diagnosis','heldout') {
            dotnet run --project lab/SupportLab.csproj -- benchmark "lab/$set.json" $base "qwen-$Size" 1 | Set-Content -Encoding utf8 "evidence/$set.json"
            if ($LASTEXITCODE -ne 0) { throw "Evaluation failed: $set" }
        }
    }
    dotnet run --project lab/SupportLab.csproj -- smoke $base "qwen-$Size" | Set-Content -Encoding utf8 "evidence/smoke-$Size.json"
    if ($LASTEXITCODE -notin 0,2) { throw 'Smoke command failed' }
    $server.Refresh()
    @{ peakWorkingSetBytes = $server.PeakWorkingSet64; modelFileBytes = (Get-Item $model).Length; modelSha256 = (Get-FileHash $model -Algorithm SHA256).Hash; runtime = 'llama.cpp b12287'; context = 2048; cpuThreads = 4 } | ConvertTo-Json | Set-Content -Encoding utf8 "evidence/runtime-$Size.json"
} finally {
    if (-not $server.HasExited) { Stop-Process -Id $server.Id }
}




