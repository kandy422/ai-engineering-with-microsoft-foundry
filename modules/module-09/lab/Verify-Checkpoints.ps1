$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot/..
dotnet restore lab/SearchLab.csproj --locked-mode --source https://api.nuget.org/v3/index.json
if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
dotnet build lab/SearchLab.csproj --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
function Run-Lab([string]$Mode) {
    $argsForLab = @('run','--project','lab/SearchLab.csproj','--no-build','--')
    if ($Mode) { $argsForLab += $Mode }
    $json = dotnet @argsForLab
    if ($LASTEXITCODE -ne 0) { throw "Lab failed: $Mode" }
    ($json -join "`n") | ConvertFrom-Json -AsHashtable
}
function Check([bool]$Condition,[string]$Message) {
    if (-not $Condition) { throw $Message }
    Write-Host $Message
}
$a = Run-Lab ''
Check ($a.Dimensions -eq 384 -and ($a.Neighbors.Id -join ',') -eq 'deadline,paraphrase,policy,unrelated') '9.1 local model: 384 dimensions and verified neighbor order'
$b = Run-Lab 'similarity'
Check ($b.Results[1].Cosine -gt $b.Results[0].Cosine -and $b.Results[1].ProjectedCosine -lt $b.Results[0].ProjectedCosine -and [Math]::Abs($b.Results[1].Cosine-$b.Results[2].Cosine) -lt 0.000001) '9.2 toy vectors: projection reverses order and cosine is scale invariant'
$c = Run-Lab 'chunk'
Check ((@($c.Methods[0].Chunks + $c.Methods[1].Chunks | Where-Object CompleteException)).Count -eq 0 -and (@($c.Methods[2].Chunks | Where-Object CompleteException)).Count -eq 1) '9.3 only the section chunk preserves the complete recall condition'
$d = Run-Lab 'azure-payloads'
Check ($d.Verification.Documents -eq $d.Verification.UniqueKeys -and $d.Vector.vectorQueries[0].vector.Count -eq 384 -and $d.Semantic.vectorQueries[0].k -eq 50) '9.4 generated indexing and query payload checks passed locally'
$e = Run-Lab 'hybrid'
Check ($e[0].Keyword.Count -eq 1 -and $e[0].Hybrid[0].Id -eq 'rtn-7041-v1' -and $e[1].Keyword -notcontains 'standard-v1' -and $e[1].Vector[0].Id -eq 'standard-v1') '9.5 exact code preserved and vector ranking recovers the paraphrase match'
$f = Run-Lab 'filter'
Check ($f.Before.Id -notcontains 'partner-v1' -and $f.After.Id -notcontains 'partner-v1' -and $f.After[0].Id -eq 'recall-v1' -and $f.Before[0].Id -eq 'standard-v1') '9.6 access filtering precedes ranking and local reranking promotes the recall result'
$g = Run-Lab 'evaluate'
Check ($g.Rows[0].Recall -eq 0.5 -and [Math]::Abs($g.MeanRecall - 5/6) -lt 0.000001 -and $g.Removed -eq 'standard-v1' -and $g.After.Id -eq 'standard-v2') '9.7 recall measured and obsolete chunk removed'

