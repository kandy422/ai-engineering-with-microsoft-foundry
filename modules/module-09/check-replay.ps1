param([switch]$IncludeLocalModel)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
function Read-Lab([string]$Mode) {
    $result = dotnet run --project lab/SearchLab.csproj --no-build -- $Mode
    if ($LASTEXITCODE -ne 0) { throw "Lab command failed: $Mode" }
    ($result -join "`n") | ConvertFrom-Json -AsHashtable
}

foreach ($name in 'm09-embed-01','m09-embed-02') {
    $folder = "lab/hosted-evidence/$name"
    $provenance = Get-Content -Raw "$folder/provenance.json" | ConvertFrom-Json -AsHashtable
    foreach ($entry in $provenance.sha256.GetEnumerator()) {
        if ((Get-FileHash -LiteralPath "$folder/$($entry.Key)").Hash -ne $entry.Value) {
            throw "Recording digest mismatch: $name/$($entry.Key)"
        }
    }
}

$replay = Read-Lab hosted
if ($replay.Embeddings.Dimensions -ne 1536 -or
    $replay.Embeddings.Neighbors[0].Id -ne 'deadline' -or
    $replay.AfterDeadline[0].Id -ne 'paraphrase') { throw '9.1 embedding comparison failed.' }

$similarity = Read-Lab similarity
$a, $b, $scaled = $similarity.Results
if ($b.Cosine -le $a.Cosine -or $a.ProjectedCosine -le $b.ProjectedCosine -or
    [math]::Abs($b.Cosine - $scaled.Cosine) -gt 0.000001) { throw '9.2 similarity comparison failed.' }

$chunk = Read-Lab chunk
$wholeMatches = foreach ($method in $chunk.Methods) {
    @($method.Chunks | Where-Object { $_.CompleteException }).Count
}
if (($wholeMatches -join ',') -ne '0,0,1') { throw '9.3 chunk boundary check failed.' }

$work = Join-Path ([IO.Path]::GetTempPath()) ("module09-" + [guid]::NewGuid().ToString('N'))
try {
    $null = New-Item -ItemType Directory -Path $work
    $payload = Join-Path $work requests.json
    py -3.12 lab/search-live/prepare.py --index course09-learner-check --out $payload
    if ($LASTEXITCODE -ne 0) { throw '9.4 request preparation failed.' }
    $plan = Get-Content -Raw $payload | ConvertFrom-Json -AsHashtable
    $positive = $plan.query.vectorQueries[0].vector
    $negative = $plan.negative.vectorQueries[0].vector
    if ($positive.Count -ne 1536 -or ($positive -join ',') -ne ($negative -join ',') -or
        $plan.query.filter.Replace('customer-a','customer-missing') -cne $plan.negative.filter -or
        ($plan.expectedKeys -join ',') -ne 'recall-v1,return-v2') { throw '9.4 scope comparison failed.' }
} finally {
    if ($payload -and (Test-Path -LiteralPath $payload)) { Remove-Item -LiteralPath $payload }
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work }
}

$code, $paraphrase, $ambiguous = $replay.Hybrid
if ($code.Keyword[0] -ne 'rtn-7041-v1' -or $code.Vector[0].Id -ne 'rtn-7041-v1' -or
    $code.Hybrid[0].Id -ne 'rtn-7041-v1' -or $paraphrase.Keyword -contains 'standard-v1' -or
    $paraphrase.Vector.Id -notcontains 'standard-v1') { throw '9.5 hybrid comparison failed.' }

if ($replay.Filter.Before[0].Id -ne 'standard-v1' -or $replay.Filter.After[0].Id -ne 'recall-v1' -or
    $replay.Filter.Before.Id -contains 'partner-v1' -or $replay.Filter.After.Id -contains 'partner-v1' -or
    $replay.Filter.TrustedScopeExclusions -notcontains 'partner-v1') { throw '9.6 eligibility or reranking failed.' }

if ($replay.Evaluation.Rows[1].Recall -ne 0 -or $replay.Evaluation.MeanRecall -ne 0.5 -or
    $replay.Evaluation.CoexistingVersions.Count -ne 2 -or $replay.Evaluation.Removed -ne 'standard-v1' -or
    $replay.Evaluation.After.Id -ne 'standard-v2') { throw '9.7 evaluation or removal failed.' }

Write-Host 'Lessons 9.1–9.7 passed local and recorded-vector checks.'
if ($IncludeLocalModel) {
    & ./lab/Verify-Checkpoints.ps1
    foreach ($name in 'starter','completed') {
        $result = Get-Content -Raw "exercises/retrieval-$name.json" | dotnet run --project lab/SearchLab.csproj --no-build -- retrieve
        if ($LASTEXITCODE -ne 0) { throw "Exercise failed: $name" }
        $replayResult = ($result -join "`n") | ConvertFrom-Json -AsHashtable
        $expectedCount = if ($name -eq 'starter') { 3 } else { 1 }
        if ($replayResult.Hits.Count -ne $expectedCount -or $replayResult.Scope -ne 'public' -or
            $replayResult.Hits.Id -contains 'partner-v1') { throw "Exercise hit count or scope failed: $name" }
    }
    Write-Host 'Local MiniLM starter and completed retrieval exercises passed.'
}
