$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot/..
$revision = '1110a243fdf4706b3f48f1d95db1a4f5529b4d41'
$base = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/$revision"
$files = [ordered]@{
    'model.onnx' = @('onnx/model.onnx','6FD5D72FE4589F189F8EBC006442DBB529BB7CE38F8082112682524616046452')
    'vocab.txt' = @('vocab.txt','07ECED375CEC144D27C900241F3E339478DEC958F92FDDBC551F295C992038A3')
}
New-Item -ItemType Directory -Force lab/model | Out-Null
foreach ($entry in $files.GetEnumerator()) {
    $path = "lab/model/$($entry.Key)"
    if ((Test-Path -LiteralPath $path) -and (Get-FileHash -LiteralPath $path).Hash -eq $entry.Value[1]) { continue }
    $download = "$path.download"
    curl.exe -fsSL --retry 2 "$base/$($entry.Value[0])" -o $download
    if ($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath $download).Hash -ne $entry.Value[1]) {
        throw "Failed to retrieve verified model asset $($entry.Key)."
    }
    Move-Item -LiteralPath $download -Destination $path -Force
}
$revision | Set-Content lab/model/revision.txt

