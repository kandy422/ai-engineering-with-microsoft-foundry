# Module 12 | Multimodal AI

Use one fictional damaged-kettle case to route inputs, inspect images, read receipts, transcribe speech, handle a voice response, edit an illustration and reconcile evidence. Run the commands from this folder.

## Setup

Install [.NET SDK 10.0.403](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-10.0.403-windows-x64-installer) and [Python 3.12.10](https://www.python.org/downloads/release/python-31210/). The SDK selector permits servicing patches in the same feature band. The C# projects have no external package dependencies. For the deterministic illustration edit, install the Python dependencies in a virtual environment.

```powershell
dotnet build lab/lab.csproj
dotnet build hosted/Hosted.csproj
dotnet run --no-build --project hosted/Hosted.csproj -- check
py -3.12 -m venv .venv
& ./.venv/Scripts/python.exe -m pip install -r requirements.txt
```

## Work through the seven checkpoints

The offline commands below use bundled fiction and recorded service results. They make no Azure requests. The replay verifies response hashes before interpreting evidence. The recorded HTTP metadata retains the original status, endpoint path, UTC time, duration and response hash. Azure resource names and transport IDs are omitted from the learner copies.

| Lesson | Run | Check the result |
|---|---|---|
| 12.1 Choose a modality | `dotnet run --no-build --project lab/lab.csproj -- lab/fixtures.json` | Four routes and two rejections. Inspect how `Router.cs` separates receipt extraction from damage inspection. |
| 12.2 Understand images | `py -3.12 replay.py 12.2` | Compare both model answers with the actual images under `public/`. The thumbnail gives an unsupported product label. The crop locates the mark, but its other claims still need review. |
| 12.3 Extract receipt fields | `py -3.12 replay.py 12.3` | Read the structured date and totals. Forty plus four validates 44.00; 48.00 goes to review. The layout pass returns lines and zero tables. |
| 12.4 Transcribe speech | `py -3.12 replay.py 12.4` | Compare the clean and noisy transcripts and WAVs. Both preserve `048271`; the complaint wording changes to “newspaper”. |
| 12.5 Build a voice interaction | `py -3.12 replay.py 12.5` | Replay the short-audio recognition result and the failed and corrected synthesis attempts. Inspect the 4.925-second synthesis WAV, the actual Windows stop request at 1.407 seconds and the accepted text fixture. The timing is software evidence. |
| 12.6 Generate and edit responsibly | `& ./.venv/Scripts/python.exe finish-illustration.py --replay ./illustration-replay` | Compare the archived generation and attempted edit under `public/`. Reproduce the deterministic mask edit in a new folder and verify 34,401 changed pixels with zero changes outside the mask. |
| 12.7 Combine modalities | `py -3.12 replay.py 12.7` | Compare hosted speech and receipt results with the fictional canonical binding. The matching case can use the local policy; the conflicting case asks for clarification. |

For lesson 12.6, `qa/12.6-provenance.json` records the exact prompt, seed, model revision, model SHA-256, sampling settings and output hashes. `learner-recordings/12.6/` preserves the captured save results. The bundled archived generation makes the lab runnable on machines without a diffusion runtime. The deterministic mask edit is the required reproducible checkpoint.

To reproduce the original generation separately, obtain the stable-diffusion.cpp `master-1086-465adf3` Windows build and the Stable Diffusion 1.5 Q4_0 GGUF at the recorded revision. Verify the model hash in the provenance file and set `$runtime` and `$model` to those paths. The device and runtime build can affect pixel-level reproducibility.

```powershell
$p = Get-Content qa/12.6-provenance.json -Raw | ConvertFrom-Json
if ((Get-FileHash $model).Hash.ToLowerInvariant() -ne $p.modelSha256) { throw 'Model hash mismatch' }
New-Item -ItemType Directory ./generation-replay
$common = @('-m',$model,'-W','512','-H','512','--steps','24','--sampling-method','euler_a','--cfg-scale','7','-n',$p.negativePrompt,'-s','48271','-t','4')
& $runtime @common -p $p.assets[0].prompt -o ./generation-replay/draft.png
if ($LASTEXITCODE) { throw 'Generation failed' }
& $runtime @common -p $p.assets[1].prompt -i ./generation-replay/draft.png --strength 0.42 -o ./generation-replay/attempt.png
```

## Run against your own allocated services

[Hosted runbook](hosted/README.md) lists Entra setup, commands, API versions and request limits. The recorded runs used GPT-5-mini `2025-08-07`, Document Intelligence `2024-11-30`, and Microsoft Speech short-audio recognition with `en-GB` and `en-GB-SoniaNeural`. The cloud rounds are optional for the offline checkpoint. Use your own assigned endpoints and keep credentials out of recordings and source control.

For 12.5, `learner-recordings/12.5/synthesis-retry/reply.wav` is the complete returned audio. The playback log records Windows start and stop return values. The source audio and timer evidence leave acoustic cutoff, continuous microphone input, and speech barge-in unmeasured.
