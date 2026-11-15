param([string]$EvidenceDir = "$PSScriptRoot/../evidence/rehearsal-v2")
$ErrorActionPreference = 'Stop'
$EvidenceDir = [IO.Path]::GetFullPath($EvidenceDir)
& "$PSScriptRoot/run.ps1" -EvidenceDir "$EvidenceDir/baseline"
$published = "$EvidenceDir/baseline/app"
$sha = (Get-FileHash "$published/Capstone.dll").Hash
$rejected = $false
try {
    & "$PSScriptRoot/run.ps1" -EvidenceDir "$EvidenceDir/candidate" -PublishedApp $published -Unavailable
} catch {
    if ($_.Exception.Message -ne 'Acceptance checks failed.') { throw }
    $rejected = $true
}
if (!$rejected) { throw 'The candidate unexpectedly passed.' }
& "$PSScriptRoot/run.ps1" -EvidenceDir "$EvidenceDir/restored" -PublishedApp $published
if ((Get-FileHash "$published/Capstone.dll").Hash -ne $sha) { throw 'The published assembly changed.' }
@{ assemblySha256=$sha; candidateRejected=$rejected; restored=$true } | ConvertTo-Json | Set-Content "$EvidenceDir/release.json"
