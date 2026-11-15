$ErrorActionPreference='Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$runtime='lab/local-runtime'
New-Item -ItemType Directory -Force $runtime | Out-Null
$assets=@(
    @{url='https://github.com/ggml-org/llama.cpp/releases/download/b10000/llama-b10000-bin-win-cpu-x64.zip';name='llama.zip';hash='1398427465FE3634CC29778B82B8BA990E47A442EF8453AE9D2D114EC2AD6563'},
    @{url='https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/91cad51170dc346986eccefdc2dd33a9da36ead9/qwen2.5-1.5b-instruct-q4_k_m.gguf';name='model.gguf';hash='6A1A2EB6D15622BF3C96857206351BA97E1AF16C30D7A74EE38970E434E9407E'}
)
foreach($asset in $assets){
    $target=Join-Path $runtime $asset.name
    if((Test-Path $target) -and (Get-FileHash -LiteralPath $target).Hash -eq $asset.hash){continue}
    curl.exe -fsSL --retry 2 $asset.url -o "$target.download"
    if($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath "$target.download").Hash -ne $asset.hash){throw "Download verification failed for $($asset.name)"}
    Move-Item -LiteralPath "$target.download" -Destination $target -Force
}
Expand-Archive -LiteralPath "$runtime/llama.zip" -DestinationPath "$runtime/bin" -Force
