# Module 12 hosted multimodal checkpoints

These .NET 10 checkpoints accompany **AI Engineering with Microsoft Foundry**. Run from the Module 12 directory. The bundled media contains fictional customer requests, a synthetic damage illustration and fictional receipts.

```powershell
dotnet build hosted/Hosted.csproj
dotnet run --no-build --project hosted/Hosted.csproj -- check
```

The check inspects five bundled WAVs and exercises agreement, disagreement, missing and ambiguous evidence, cross-customer denial, receipt arithmetic and review routing using local data. It requires neither credentials nor Azure. The package needs `hosted/Hosted.csproj`, `hosted/*.cs`, the five `hosted/fixtures/*.wav`, `lab/Receipt.cs`, `lab/policy.txt`, `lab/evidence/receipt*.png` (only the baseline and mismatch), and `public/damage-low.png` and `public/damage-detail.png`. Exclude `bin`, `obj`, cloud evidence, keys and production assets.

## Resource setup

Use the assigned subscription and resource endpoints. Confirm region, quota, SKU and expected charges before using the cloud commands. Run one allocated batch per command. Each requires a fresh output directory and uses fixed fixtures, with no automatic POST retries.

- **Vision** uses an Azure OpenAI resource origin such as `https://<name>.openai.azure.com`, the assigned deployment alias, and a short-lived Entra token scoped to `https://cognitiveservices.azure.com/`. Set `M12_VISION_ORIGIN`, `M12_VISION_DEPLOYMENT` and `M12_VISION_TOKEN`. The first candidate is the existing Phase A GPT-5-mini deployment if the coordinator verifies current image input support, deployment version, quota and caller inference access on the assigned resource. [Vision requests](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/gpt-v-quickstart) · [Model catalog](https://learn.microsoft.com/en-us/azure/ai-foundry/model-inference/concepts/models)
- **Documents** uses a Document Intelligence HTTPS custom origin, API `2024-11-30`, `prebuilt-receipt` and `prebuilt-layout`. Set `M12_DOCUMENT_ORIGIN` and use resource-scoped Cognitive Services User access with a short-lived `M12_AZURE_TOKEN`. [Entra setup](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/sdk-overview-v4-0) [REST reference](https://learn.microsoft.com/en-us/rest/api/aiservices/document-models/analyze-document?view=rest-aiservices-v4.0%2B%282024-11-30%29) · [Pricing](https://azure.microsoft.com/en-us/pricing/details/document-intelligence/)
- **Speech** uses a Speech-enabled HTTPS custom origin. Set `M12_SPEECH_ORIGIN`, `M12_SPEECH_RESOURCE_ID` (the exact ARM resource ID) and `M12_AZURE_TOKEN`. The client constructs the documented `Bearer aad#<ARM resource ID>#<Entra token>` header. Confirm `en-GB` short-audio recognition and the `en-GB-SoniaNeural` synthesis voice in the assigned region. Sonia is the support application's voice. George remains the course narrator. [Short-audio recognition](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-speech-to-text-short) · [Synthesis](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-text-to-speech) · [Pricing](https://azure.microsoft.com/en-us/pricing/details/speech/)

Use resource-scoped credentials for this recording. Acquire the Entra access token in the process environment from the assigned Azure CLI context. Keep the token off-screen and clear it when the run ends. The capstone uses its own workload identity and scoped access.

```powershell
$env:M12_AZURE_TOKEN = az account get-access-token --resource 'https://cognitiveservices.azure.com/' --query accessToken -o tsv --only-show-errors
# Set M12_VISION_TOKEN to the same token only when the vision caller is authorized.
```

The resource configuration uses Entra only. A service failure stays in the recording evidence for review before an allocated retry.

## Run, inspect and explain

Use distinct output paths and show source, exact changed input, command, actual result and interpretation in the same recording. Requests save fixture hashes, status, elapsed time, selected request-ID headers and response hashes. Preserve service errors and unexpected results. The program can exit successfully after a service returns weak evidence, so inspect the actual result before marking the teaching checkpoint complete.

| Lesson | Command suffix after `dotnet run --no-build --project hosted/Hosted.csproj --` | What to show and inspect |
|---|---|---|
| 12.2 | `vision ./capture-122` | Fixed prompt and deployment, thumbnail versus detail crop. Compare actual responses, finish reason and usage. Explain any context loss or uncertainty and avoid inferring cause, safety or warranty. |
| 12.3 | `documents ./capture-123` | Single-page layout and receipt analysis. Open layout lines and inspect actual table count and cells when detected. Compare structured baseline and changed-total receipts, field confidence and arithmetic. Low confidence or missing fields can send either receipt to review. |
| 12.4 | `speech ./capture-124` | Fixed language and recognition format with paired clean and noisy inputs. Compare `RecognitionStatus`, exact six-digit reference and the actual transcription with `lab/evidence/reference.txt`. Noise need not change the result. |
| 12.5 | `voice ./capture-125` | Use the bundled prerecorded fictional request with an incomplete reference, run hosted STT and TTS, play the returned WAV through the Windows speaker, press Space during the reply and type `048271`. Capture audible start and cutoff, the code's stop result, and the typed fallback. |
| 12.7 | `combine ./capture-127` | Same receipt and lab policy, matched versus conflicting speech. Inspect both hosted STT outputs and hosted receipt fields. Follow the local canonical mapping and policy decision. The policy fixture has no managed retrieval claim. |

For 12.5 use Windows with a speaker and interactive terminal. Send the bundled synthetic `request-unclear.wav` to the Speech short-audio REST endpoint. No microphone capture is needed. Use a screen recorder with system audio to demonstrate the audible cutoff. The app records a stop-call result and local elapsed time. Recognition finishes before synthesis, and an explicit keypress can interrupt speaker playback. The lab demonstrates short-audio recognition, hosted synthesis, playback cancellation and a text fallback. Streaming recognition, automatic speech barge-in, echo cancellation and acoustic stop latency remain unmeasured.

For a deterministic device trial, set `M12_VOICE_STOP_MS` to a value from 500 to 2000 and supply the fictional text fallback after playback. The output distinguishes the scripted timer from a Space keypress. A recorded run with successful recognition and failed synthesis can resume with `voice-retry ./capture-125-retry ./capture-125` after the failed request is reconciled and a retry is allocated. This path validates the previous recognition response and fixture checksums and issues only one additional synthesis request.

## Allocation and evidence acceptance

One pass across all five modes allows **2 vision POSTs** with one PNG under 100 KB and 512 completion tokens each, **4 single-page document POSTs** (three receipt and one layout), at most **40 document result GETs**, **5 short STT POSTs** (about 29.7 source seconds with the five bundled inputs, hard maximum 50 seconds), and **1 TTS POST** (under 100 text characters). There are **zero image-generation or edit requests**. Pause after a failed batch and reconcile request IDs before any additional allocation.

Proposed incremental usage allocation is at most **US$1**, subject to the coordinator's verified deployment and region rates and the core lab ledger. This figure is a requested allocation ceiling, not a billed amount or a service-enforced cap. Price the actual vision input/output tokens, receipt and layout pages separately if metered differently, effective billable speech duration, neural characters and any fixed resource charges. Reuse eligible resources where possible. Record exact resource IDs, SKU, deployment version, UTC run window, estimated and reconciled charges and disposal or retention in the coordinator ledger. [Azure Pricing Calculator](https://azure.microsoft.com/en-us/pricing/calculator/)

For 12.7, the teaching mapping binds receipt reference `048271` to `ORD-1042` for fictional `lab-tenant/customer-a`. The coordinator and Module 16 must establish the actual deployed, authenticated tenant/customer binding server-side. Look up the canonical order within trusted scope before evaluating extracted references. OCR and speech only corroborate that record. A missing or conflicting reference leads to clarification, invalid receipt data to review, and an out-of-scope canonical lookup to denial. Do not expose identifiers in a denial. Module 16 owns the actual identity, order lookup and scoped policy retrieval integration.

Lesson 12.1 retains the runnable local routing and unsupported-input checks. Lesson 12.6 retains actual local generated/edit provenance and the controlled edit. A demonstration of a hosted image generator/editor would need a separate coordinator scope and exact SKU estimate and is excluded from this recording batch.

The clean and noisy files are 16 kHz conversions of the earlier Emma fixtures. The agreement and conflict files use the same Windows Zira voice at the same settings, with only the last spoken digit changed. Their source text and reproducible generator are in [fixtures/prepare-voice.ps1](fixtures/prepare-voice.ps1).
