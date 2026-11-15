param([switch]$TryThree)

$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '../..')
try {
    if (Get-NetTCPConnection -LocalPort 8407 -State Listen -ErrorAction SilentlyContinue) {
        throw 'Port 8407 is occupied. Stop the existing listener before starting the exercise.'
    }
    dotnet build lab/SupportChat.csproj --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Lab build failed.' }

    function Invoke-LocalRun([string]$Label, [int]$Limit, [string[]]$Arguments) {
        $server = Start-Process dotnet -WorkingDirectory (Resolve-Path lab).Path `
            -ArgumentList @('bin/Debug/net10.0/SupportChat.dll', '--urls=http://127.0.0.1:8407',
                '--Foundry:Endpoint=', '--Foundry:Deployment=', "--Chat:PermitLimit=$Limit") `
            -WindowStyle Hidden -PassThru
        try {
            $mode = $null
            for ($attempt = 0; $attempt -lt 40; $attempt++) {
                if ($server.HasExited) { break }
                try {
                    $mode = (Invoke-RestMethod 'http://127.0.0.1:8407/mode' -TimeoutSec 2).mode
                    break
                } catch { Start-Sleep -Milliseconds 200 }
            }
            if ($mode -ne 'Local stub') { throw 'The local stub failed readiness.' }

            Write-Host $Label
            node @Arguments
            if ($LASTEXITCODE -ne 0) { throw "$Label check failed." }
        } finally {
            if (!$server.HasExited) {
                Stop-Process -Id $server.Id
                $server.WaitForExit()
            }
        }
    }

    Invoke-LocalRun 'limit-two' 2 @('lab/workflow-check.mjs','limit','2')
    Invoke-LocalRun 'limit-one' 1 @('lab/workflow-check.mjs','limit','1')
    if ($TryThree) { Invoke-LocalRun 'limit-three' 3 @('lab/workflow-check.mjs','limit','3') }
} finally { Pop-Location }
