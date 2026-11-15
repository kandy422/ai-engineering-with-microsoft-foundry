# Module 05 — How language models generate answers

AI Engineering with Microsoft Foundry

Run all commands from this directory (`learner-course/modules/module-05` in the course bundle). The setup below uses Windows x64 and PowerShell 7. Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), feature band 10.0.4xx, patch 403 or later in that band. `global.json` pins that band and lets installed security patches roll forward. Validation used SDK 10.0.403. An editor is optional.

```powershell
dotnet --version
dotnet restore lab --locked-mode --source https://api.nuget.org/v3/index.json
dotnet build lab --no-restore
pwsh -File ./check-labs.ps1
```

The locked restore pins direct and transitive dependencies, including Microsoft.ML.Tokenizers and its cl100k_base data (3.0.0), LLamaSharp and its CPU backend (0.30.0). The first restore needs NuGet access. The default checks run without Azure or a model download. They execute the local labs and parse recorded service runs from disk.

| Lesson | Start here | Completed checkpoint |
| --- | --- | --- |
| 5.1 Tokens | Run `dotnet run --project lab --no-build -- 5.1`. In `InspectTokens` in `lab/Program.cs`, predict how the support request, C# snippet, and Chinese query will split. | Counts are 7, 4, and 11. The program checks exact decode round trips. Compare the IDs to `evidence/5.1-tokens.json`. These IDs belong to cl100k_base. |
| 5.2 Generation | Inspect `m05-52-short/request.json` and predict the result. Compare it to `m05-52-room/request.json` under `evidence/5.2-cinematic-foundry-v3/recordings/`. | The cap changes from 16 to 1,024. The short run returned an empty visible response with `length`; the long run returned 12 greetings and `stop`. Saved usage records 16 and 643 completion tokens. The local toy sampler below illustrates probabilities separately. |
| 5.3 Training and inference | In `Train` in `lab/Program.cs`, start with the two zero scores. Run `dotnet run --project lab --no-build -- 5.3` and compare the training stages. | The preferred continuation becomes `ask for the order ID` and inference reports `weightsUnchanged: true`. Compare with `evidence/5.3-lab.json`. |
| 5.4 Context | Compare the full, trimmed, and restored requests in `evidence/5.4-cinematic-foundry-v3/recordings/`. Find the same follow-up in each request. | Recorded outputs are `A-1042`, `MISSING`, and `A-1042`. The separate compiled C# run confirms `A-1042` on the full request. Changing the messages changes the inference input. |
| 5.5 Evidence | Compare the four requests in `evidence/5.5-cinematic-foundry-v3/recordings/`. Predict what the model should ask about or report when it lacks evidence. | The model states 30 days from delivery, asks about final-sale status and delivery date, reports the fee is unspecified, then quotes USD 12 after the new policy sentence is provided. |
| 5.6 Reasoning | Compare `m05-56-low` and `m05-56-medium` in `evidence/5.6-cinematic-foundry-v3/recordings/`. Run each recorded response through the checker below. | Both schedules pass with Ava 11, Ben 09, and Chen 10. The recorded reasoning counts are 128 and 256. These measurements describe the two runs. |
| 5.7 Application | In `Application` in `lab/Program.cs`, trace `Lookup("A-1043")` and then `Lookup(request.OrderId)`. Run `dotnet run --project lab --no-build -- 5.7`. | The app rejects the wrong-order result, accepts the verified order result, and displays `Order A-1042 is delayed.` Compare with `evidence/5.7-lab.json`. |

Read a saved response with the same C# parser used for the exercises. The folders contain exact request bodies, learner copies of response content and usage, and a small provenance file with the recorded timing and transport. Response copies omit operational IDs and routing metadata.

```powershell
$run = 'evidence/5.6-cinematic-foundry-v3/recordings/m05-56-low'
Get-Content "$run/request.json"
dotnet run --project lab --no-build -- foundry-recording "$run/response.json"
```

## Optional local model labs

The four recorded Foundry comparisons above can be studied fully without a local model. To repeat the separate CPU demos from the module source, download [SmolLM2-360M-Instruct-Q4_K_M.gguf](https://huggingface.co/lmstudio-community/SmolLM2-360M-Instruct-GGUF/blob/main/SmolLM2-360M-Instruct-Q4_K_M.gguf) (about 271 MB) and save it as `tools/smollm2.gguf` under this directory. Verify the SHA-256 digest before use.

```powershell
Get-FileHash ./tools/smollm2.gguf -Algorithm SHA256
pwsh -File ./check-labs.ps1 -IncludeLocalModel
dotnet run --project lab --no-build -- 5.2
```

Expected SHA-256 is `e55c2a5df7f8f6eed1a843ff709fa8008dbce65d82e57c5c06dec5ee8d060415`. You can also run lesson IDs `5.4`, `5.5`, and `5.6` in place of `5.2`. The local 5.2 check expects softmax probability near 0.66524 for the first candidate and three generations. The local 5.4 app retains or restores the exact ID, while the model's reply can vary. The local 5.5 lab returns three answers to inspect against the fixture. The local 5.6 checker finds the unique valid schedule. Local output wording and timing can vary. These use SmolLM2 and local fixtures.

## Optional live Foundry reproduction

Use your own authorized [Azure OpenAI v1 endpoint and Microsoft Entra identity](https://learn.microsoft.com/en-us/azure/developer/ai/how-to/switching-endpoints), plus Azure CLI. The bundled HTTP runner uses only the .NET libraries and accepts a deployment named `course-chat`. The recordings used GPT-5-mini version 2025-08-07. The optional command sends a billable request to your deployment. The replay and exercises above work with the saved files.

```powershell
az login
$env:FOUNDRY_OPENAI_V1_BASE = 'https://YOUR-RESOURCE.openai.azure.com/openai/v1/'
$env:FOUNDRY_ENTRA_TOKEN = az account get-access-token --resource https://cognitiveservices.azure.com --query accessToken -o tsv
try {
    $run = 'evidence/5.4-cinematic-foundry-v3/recordings/m05-54-full'
    dotnet run --project lab --no-build -- foundry-request "$run/request.json"
} finally {
    Remove-Item Env:FOUNDRY_ENTRA_TOKEN -ErrorAction SilentlyContinue
}
```

If authentication fails, check that the endpoint ends in `/openai/v1/`, your token is current, and your identity has inference access on your deployment. A fresh run can produce different text and usage. Compare the observable result with the checkpoint, and preserve the original captured response.

## Independent exercise — reject an invalid schedule

Copy `exercises/5.6/starter-response.json` to `exercises/5.6/my-response.json`. Run the parser below, then edit the JSON string inside `choices[0].message.content` so Ava, Ben, and Chen satisfy all three constraints from 5.6 and occupy distinct slots. Keep the outer structure and rerun. Compare with `completed-response.json` after checking your answer.

```powershell
Copy-Item exercises/5.6/starter-response.json exercises/5.6/my-response.json
dotnet run --project lab --no-build -- foundry-recording exercises/5.6/my-response.json
```

Acceptance is `scheduleValid: false` for the starter with a duplicate slot and `scheduleValid: true` for your repaired response. The checker also rejects out-of-range slots, unknown or missing names, and invalid JSON. The exercise files are synthetic checker inputs.

Module 05 is self-contained. Module 01 introduces Foundry and the support assistant. The order IDs, policy, and schedules here are fictional lab fixtures with no dependency on other modules’ services or contracts.

