#Requires -Version 7.2
[CmdletBinding()]
param(
    [string] $Project = (Join-Path $PSScriptRoot '../../module-16/lab/Capstone.csproj'),
    [string] $Output = (Join-Path $PSScriptRoot ('build-' + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path -LiteralPath $Project).Path
$outputPath = [IO.Path]::GetFullPath($Output)
if ((Split-Path -Leaf $projectPath) -ne 'Capstone.csproj' -or (Test-Path -LiteralPath $outputPath)) {
    throw 'Supply the Module 16 Capstone.csproj and a new output directory.'
}
New-Item -Path $outputPath -ItemType Directory | Out-Null
# Keep the app checkout read-only and the upload context limited to publish output.
$work = Join-Path ([IO.Path]::GetTempPath()) ('capstone-build-' + [guid]::NewGuid())
try {
    dotnet publish $projectPath -c Release -p:UseAppHost=false --artifacts-path $work -o (Join-Path $outputPath 'publish')
    if ($LASTEXITCODE -ne 0) { throw 'Capstone publish failed.' }
    if (!(Test-Path (Join-Path $outputPath 'publish/Capstone.dll'))) { throw 'Capstone.dll was not published.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Dockerfile') -Destination $outputPath
    Get-Item -LiteralPath $outputPath
} finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ((Test-Path -LiteralPath $work) -and (Split-Path -Parent (Resolve-Path -LiteralPath $work).Path) -eq $tempRoot.TrimEnd('\', '/')) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
