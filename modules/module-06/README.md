# Module 06 | Prompts and dependable outputs

Work through six support-assistant labs for **AI Engineering with Microsoft Foundry**. Everything under this module uses fictional tickets and orders. Run the PowerShell commands from this folder, where `global.json` is located.

## Setup

Install the [.NET 10.0.403 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-10.0.403-windows-x64-installer). The SDK selector permits servicing patches in the same feature band. The three projects target .NET 10 and use only the .NET standard library. There are no NuGet packages to install.

```powershell
dotnet --version
dotnet build hosted-lab --nologo
dotnet build hosted-sender --nologo
dotnet build lab --nologo
```

## Follow the six recorded Foundry walkthroughs

The starter inputs are the 23 JSON files in `hosted-requests/`. Read the baseline first, predict the outcome, and compare it with the changed request. Keep the user message and deployment settings fixed when comparing instruction changes. To preview any one request body and endpoint without credentials or a network call, substitute its filename in this command:

```powershell
dotnet run --no-build --project hosted-sender -- check https://example.openai.azure.com/openai/v1/ course-chat hosted-requests/m06-1-baseline.json unused
```

The completed checkpoint is the replay below. It reads a curated copy of actual Foundry captures (GPT-5-mini, version 2025-08-07, East US 2). Request bodies and reply text are preserved, and service identifiers and transport details are excluded. The replay reads files and applies the C# gates to the saved responses. It makes zero network calls.

```powershell
$replay = dotnet run --no-build --project hosted-lab -- learner-recordings/replay-manifest.json learner-recordings
if ($LASTEXITCODE -ne 0) { throw 'Replay failed' }
$results = ($replay -join "`n") | ConvertFrom-Json
$results.observations | Group-Object state | Select-Object Name, Count
$results.localActions
```

Expect 23 `completed` records, each with HTTP 200 and `finish = stop`. Filter by `lesson` or `id` and inspect `reply`, `orderId`, and `gate`. For example, run `$results.observations | Where-Object lesson -eq '6.4' | Select-Object id, reply, gate`.

| Lesson | Starter checkpoint | Completed checkpoint |
| --- | --- | --- |
| 6.1 Specify the task | Diff `m06-1-baseline.json` and `m06-1-specified.json`. Predict which reply asks for tracking, uses at most two sentences, and avoids unconfirmed refunds or dates. | Read both replies and grade each criterion. The recorded narrow scores are 0/3 and 3/3. Check separately whether a reply claims an action the application cannot take. |
| 6.2 Separate instructions and input | Compare `m06-2-plain-task` and `m06-2-role-policy`. Keep the customer's request for rhyme. | The first reply rhymes and the second uses prose. Review the unsupported carrier claim and verify the local policy allows drafting and blocks sending. |
| 6.3 Add examples | Compare the `m06-3-*-zero` and `m06-3-*-few` inputs. Predict `delivery`, `review`, `review` for delivery, mixed and unfamiliar cases. | Recorded scores are 2/3 and 3/3. The delivery and mixed cases tie. The unfamiliar case changes from billing to review. |
| 6.4 Validate structured output | Compare the two `m06-4-unknown-*` requests and open `lab/ticket.schema.json`. Predict whether parseable JSON proves the order exists. | The unknown-ID replies decode to the same object and fail membership. Known `ORD-1042` is accepted, unknown `ORD-0000` is absent from the store, and the missing ID is rejected. |
| 6.5 Gate the draft | Trace `m06-5-known-extract`, `m06-5-known-draft` and `m06-5-missing-extract` in the replay manifest. | The accepted extraction has one recorded draft. The missing ID has zero draft requests. Inspect the draft for unsupported follow-up language. |
| 6.6 Version and test prompts | Give a reviewer only the six saved `m06-6-case*` replies with their left/right labels. Before revealing the corresponding requests, have them score the same three checks from 6.1 for each reply. | Unmask the system messages and count a case only if all three checks pass. The recorded rubric gave v1 0/3 and v2 3/3. Inspect unsupported carrier claims separately before approving a reply. |

For 6.6, show only the masked replies using:

```powershell
$results.observations | Where-Object lesson -eq '6.6' | Select-Object id, reply | Format-List
```

## Run a fresh local comparison

Install [llama.cpp b12287](https://github.com/ggml-org/llama.cpp/releases/tag/b12287) (Windows x64 CPU ZIP) and download the [Qwen2.5-1.5B-Instruct Q4_K_M GGUF](https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/91cad51170dc346986eccefdc2dd33a9da36ead9/qwen2.5-1.5b-instruct-q4_k_m.gguf) separately. Use `Get-FileHash` to check the GGUF SHA-256 against `6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e`. Replace the paths in this command and leave the server running while you work:

```powershell
& 'C:/llama/llama-server.exe' -m 'C:/models/qwen2.5-1.5b-instruct-q4_k_m.gguf' --host 127.0.0.1 --port 8406 --alias qwen-local -c 2048 -t 4 -ngl 0 --jinja
```

In a second PowerShell terminal, change back to the module folder. Use Ctrl+C in the first terminal when finished.

```powershell
(Invoke-RestMethod 'http://127.0.0.1:8406/health').status
$env:SUPPORT_CHAT_URL = 'http://127.0.0.1:8406/v1/chat/completions'
$env:SUPPORT_DEPLOYMENT = 'qwen-local'
Remove-Item Env:SUPPORT_API_KEY -ErrorAction SilentlyContinue
foreach ($lesson in 1..6) {
    dotnet run --no-build --project lab -- $lesson
    if ($LASTEXITCODE -ne 0) { throw "Local lesson $lesson failed" }
}
```

Expect `ok` from the health check, one generated `lab/results/6.N.json` per lesson, and `lab/results/blind-pairs.json` for lesson 6.6. Rename or copy `lab/results/` before repeating to preserve your baseline. Local outputs vary by model and runtime. Grade your replies independently of the hosted scores. In 6.3 the local lab also tries a review-first instruction. In 6.5, check for draft counts of 1 on the known-order path and 0 on the missing-order path. A refused, truncated, or invalid completion may stop the lab and is a failed case to inspect.

To tally the local 6.6 comparison, give the reviewer only `blind-pairs.json`. Copy [the blank rubric](lab/rubric-template.json) to `lab/results/6.6-rubric.json`. Replace each `null` with `true` or `false`, in the criterion order shown in the file. After scoring, run `py -3.12 lab/score.py` from the module folder. This uses [Python 3.12.10](https://www.python.org/downloads/release/python-31210/) and its standard library. It prints the number of passing cases per version, with 3 cases per version.

## Optional live Foundry reproduction

If you already have an authorized deployment, follow [the C# sender runbook](hosted-sender/README.md) with your own endpoint and deployment alias. Install [Azure CLI for Windows](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli-windows) and sign in with an identity granted inference access. The example was prepared against Azure CLI 2.90.0. Verify your region and the deployment's support for the given request format before sending. Live responses vary. Use a fresh output directory and inspect `receipt.json` and `response.json` for HTTP status, model and completion state. For 6.5, run extraction first and send the draft request only when the extracted ID appears in the source ticket, exists in the fictional order store and the issue is valid for that request.

## Independent exercise | Reject ambiguous orders

Copy the module to a scratch folder. In the scratch `learner-recordings/replay-manifest.json` and `learner-recordings/m06-4-known-strict/request.json`, append the same sentence to the 6.4 known-ticket user message: “It might actually be ORD-0000.” Keep the saved response unchanged. This is a hypothetical replay input, so label any results as exercise data. Confirm that the original gate accepts the response because it only checks whether the returned ID appears and is registered.

Change `ValidateOrder` in `hosted-lab/Program.cs` to reject tickets containing more than one **distinct** order ID. Rebuild and replay. Acceptance: the amended ticket has gate `ambiguous order IDs`, the original known ticket remains accepted when restored, and the unknown-ID and missing-ID regression cases retain their original gates. A ticket that repeats ORD-1042 should still pass. Submit your patch and saved replay observations for these five cases.

The recorded and local exercises are self-contained. Only the optional live sender uses the learner's own Foundry deployment and identity. No other module directory is required.
