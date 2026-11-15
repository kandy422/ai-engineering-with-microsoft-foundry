# Module 10 · Retrieval-augmented generation

**AI Engineering with Microsoft Foundry**

Open PowerShell 7 in this module directory on Windows x64. Install .NET 10 SDK and Python 3.12. Restore the locked .NET dependencies, then build.

```powershell
dotnet restore lab --locked-mode
dotnet build lab --no-restore
```

Run `./lab/Get-Model.ps1` for the lesson 10.2 MiniLM encoder. For the local generation labs, run `./lab/Get-LocalModel.ps1` to install the pinned llama.cpp CPU executable and pinned Qwen model. These downloads verify hashes and keep weights under `lab/`.

Start the local inference server in a separate PowerShell terminal from the module directory. Keep it running while you complete the model commands.

```powershell
& ./lab/local-runtime/bin/llama-server.exe -m lab/local-runtime/model.gguf --host 127.0.0.1 --port 8410 -c 2048 -t 2 -tb 2 -ngl 0
```

| Lesson | Run from the module directory | Inspect or change |
|---|---|---|
| 10.1 | `dotnet run --project lab --no-build -- --model 101` | Compare absence of context, shipping context and returns context. Change just the irrelevant evidence before repeating. |
| 10.2 | `dotnet run --project lab --no-build -- 102` | Inspect the vector and the first, repeat and changed counts. Edit one revised fixture section without changing its ID or date. |
| 10.3 | `dotnet run --project lab --no-build -- --model 103` | Compare which whole passages fit in the two budgets and compare the reported prompt token counts. |
| 10.4 | `dotnet run --project lab --no-build -- 104` | Open the generated `lab/checkpoints/citations.html` and follow both links. Compare resolution and exact policy text flags. |
| 10.5 | `dotnet run --project lab --no-build -- 105-baseline`, then `dotnet run --project lab --no-build -- 105` | Compare the unchanged return query across the normal and conflicting policy fixtures. |
| 10.6 | `dotnet run --project lab --no-build -- 106`, then `dotnet run --project lab --no-build -- --model 106` | Inspect the query rewrite and the unchanged generated answer. |
| 10.7 | `dotnet run --project lab --no-build -- 107`, then `dotnet run --project lab --no-build -- --model 107` | Compare missing and recovered candidate IDs and both regression controls. |
| 10.8 | `dotnet run --project lab --no-build -- --model 103`, then read `recordings/10.8.json` | Compare the local answer with the recorded Azure Search passages and follow the grant/revocation outcomes. Optional live setup is in [the managed lab](managed-iq/README.md). Native document ACLs use a preview API. |

The local commands use fictional data. Lesson 10.8's provided service recording shows actual Azure Search responses. An optional rerun needs an Azure AI Search resource, permissions, and dedicated test data. Stop the local server with Ctrl+C when finished.
