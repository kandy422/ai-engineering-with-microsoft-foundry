# Module 11 · Tools, workflows and agents

AI Engineering with Microsoft Foundry

Start in this directory on Windows x64 with PowerShell and .NET SDK 10.0.403. The fixture labs require no Azure access or model download. NuGet access is needed on the first restore.

```powershell
dotnet restore lab --locked-mode
dotnet build lab --no-restore --nologo
dotnet restore foundry --locked-mode
dotnet build foundry --no-restore --nologo
```

Run each command from this directory. Confirm a zero exit code and compare the named output events.

| Lesson | Command after build | Expected observation |
|---|---|---|
| 11.1 | `dotnet run --project lab --no-build -- 11.1` | The text-only answer has no order data; the fixed workflow and simulated tool decision both return Shipped; an unsupported tool returns `unknown_tool`. |
| 11.2 | `dotnet run --project lab --no-build -- 11.2` | `valid` returns `ok`; `malformed` and `spoof` return `invalid_argument`; `inaccessible` returns `not_available` with a null status. |
| 11.3 | `dotnet run --project lab --no-build -- 11.3` | The text event invokes nothing. The structured call produces one lookup, and the reply cites the same call ID. Repeat with the actual local model below. |
| 11.4 | `dotnet run --project foundry --no-build -- --local` | Six policy and argument checks pass locally. Then follow the [hosted lab](foundry/Learner-setup.md) for two actual Foundry conversations and their Application Insights traces. |
| 11.5 | `dotnet run --project lab --no-build -- 11.5` | The original caller resumes ORD-1042. The other caller, expired item, and forgotten item each return null. |
| 11.6 | `dotnet run --project lab --no-build -- 11.6` | Retrying after the injected lost response recovers T-1; only one ticket was committed. The loop stops after three steps and escalates. |
| 11.7 | `dotnet run --project lab --no-build -- 11.7` | Wrong caller, altered proposal, and unconfirmed change are rejected. Confirm, apply and replay; the audit has one entry. |
| 11.8 | `dotnet run --project lab --no-build -- 11.8` | The MCP client launches a local subprocess over standard I/O, discovers `lookup_order` and sees an unavailable result for another customer's order. |
| 11.9 | `dotnet run --project lab --no-build -- 11.9` | Both fixture routes match four expected outcomes. Run the real local model comparison below and compare result quality and calls; treat one timing sample as illustrative. |

## Run the complete local agent loop

Install Python 3.12. No pip dependencies are needed. Download the Windows CPU x64 release of [llama.cpp b12287](https://github.com/ggml-org/llama.cpp/releases/tag/b12287) (commit `459ab1cdd`, asset `llama-b12287-bin-win-cpu-x64.zip`), and [Qwen3-0.6B-Q8_0.gguf at revision 23749fefcc72300e3a2ad315e1317431b06b590a](https://huggingface.co/Qwen/Qwen3-0.6B-GGUF/tree/23749fefcc72300e3a2ad315e1317431b06b590a). Keep release dependencies beside `llama-server.exe` in `.model/runtime`; the archive may have an outer folder. Place the GGUF at `.model/Qwen3-0.6B-Q8_0.gguf`.

| Download | SHA-256 |
|---|---|
| Windows CPU x64 archive | `BCC47AF9F91B0B7512A84E592553855D581573B9BFA7182C17040EAC3516A4C6` |
| GGUF | `9465E63A22ADD5354D9BB4B99E90117043C7124007664907259BD16D043BB031` |

Use `Get-FileHash -Algorithm SHA256 <file>` to verify each download. Leave loopback port 8411 free.

```powershell
py -3.12 model-checkpoint.py live-tool-loop --repeat
```

This runs both live local checkpoints. For 11.1 and 11.3 inspect `live-tool-loop.ndjson`: model selection, structured `lookup_order` with ORD-1042, C# lookup, same call ID in the submitted result, and a final reply reporting Shipped. For 11.9 inspect `live-orchestration.ndjson`: compare all four actual outcomes in each route against their expected values, then compare model and tool calls. Inspect the server log if a checkpoint fails. Results appear under `qa/reproduction/<UTC timestamp>`. Identifiers, free text, and timings can vary.

## Independent exercise: prove denial looks like absence

In the 11.2 cases in `lab/Program.cs`, add a valid but absent order, ORD-9999, and an order with a malformed ID, ORD-104. Predict the response codes before running the checkpoint. Build again, then compare the absent case with the inaccessible ORD-2045 result. The two results should be identical (`not_available`, null status); the malformed ID should return `invalid_argument`. Also verify ORD-1042 still returns Shipped. Explain which layer decides access and why caller identity must not come from model arguments. This exercise uses only the local fixture.
