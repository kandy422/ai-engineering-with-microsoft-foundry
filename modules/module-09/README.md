# Module 09 · Embeddings and semantic search

AI Engineering with Microsoft Foundry

Install PowerShell 7, .NET SDK 10.0.403 and Python 3.12. Run commands from this module folder. The SDK is pinned in `global.json` and NuGet versions in `lab/packages.lock.json`. The optional local MiniLM model downloader verifies the pinned revision and asset hashes.

```powershell
dotnet restore lab/SearchLab.csproj --locked-mode --source https://api.nuget.org/v3/index.json
dotnet build lab/SearchLab.csproj --no-restore
pwsh -File check-replay.ps1
```

The replay reads recorded Foundry vectors and runs the C# comparisons locally. The restore downloads dependencies, and the replay needs no Azure credentials or new model requests. The commands below use fictional support policies.

| Lesson | Run the completed checkpoint | Learner exercise |
| --- | --- | --- |
| 9.1 Represent meaning with embeddings | Run `hosted` and inspect `Embeddings.Neighbors` and `AfterDeadline`. The 1,536-dimensional replay ranks the deadline match first before the rule and the paraphrase first afterward. | Open `lab/HostedRetrieval.cs`. Change the literal rule to match “after 30 days”, rebuild and compare with the original winners. Explain why exact-phrase rules can miss a paraphrase. |
| 9.2 Measure similarity | Run `similarity` and compare `Cosine` with `ProjectedCosine`. B leads in three coordinates and A leads after slicing to two. | In `Measure()` change both slices to `[1..]`. Predict the ranking before rebuilding and rerunning. B leads for that coordinate pair. |
| 9.3 Prepare documents for retrieval | Run `chunk`. Only section chunking preserves the full recall exception. | Change the first window to `Window(40, 0)` and rerun. Compare the complete match count and how much unrelated context comes with the match. |
| 9.4 Build a vector index | Follow the [managed Search lab](lab/search-live/README.md). Local preparation reuses the recorded vectors. The separate Create, Load, Query, Negative and Cleanup actions call your assigned Search service. | Before using Azure, compare the prepared positive and negative requests. Confirm the question vector is identical and predict which documents can match under each scope. |
| 9.5 Combine keyword and vector search | Run `hosted` and compare `Hybrid[0]` and `Hybrid[1]`. The exact order ID leads all three methods. The paraphrase’s standard policy is present in vector results and absent from keyword results. | Inspect `Hybrid[2]` for the ambiguous “return” query. Compare the first result of each method and write a follow-up question for the customer. |
| 9.6 Filter and rerank results | Run `hosted` and inspect `Filter.Before` and `Filter.After`. Recall moves from second to first and the partner passage stays excluded. | In `Filter()`, change the candidate count in `VectorOrder(scoped, vector, 3)` to one. Rebuild and rerun. The rule cannot promote a passage outside the shortlist. |
| 9.7 Evaluate and maintain search | Run `hosted` and inspect `Evaluation`. The second query has recall at two of zero. Both policy versions coexist until the old ID is removed. | In a local copy, comment out `documents.Remove(old)`, rebuild and rerun. The obsolete-chunk assertion should stop the run. Restore the line for later labs. |

To run an individual checkpoint, substitute its command for `hosted`:

```powershell
dotnet run --project lab/SearchLab.csproj --no-build -- hosted
```

Make one exercise edit at a time and rebuild without `--no-build` after editing. Restore the changed line before moving on. The assertions in `check-replay.ps1` document the expected completed checkpoints.

For optional MiniLM practice, download the pinned 384-dimensional model and run the local checks. This is a different encoder from the recorded `course-embed` deployment.

```powershell
pwsh -File lab/Get-Model.ps1
pwsh -File lab/Verify-Checkpoints.ps1
Get-Content -Raw exercises/retrieval-starter.json | dotnet run --project lab/SearchLab.csproj --no-build -- retrieve
Get-Content -Raw exercises/retrieval-completed.json | dotnet run --project lab/SearchLab.csproj --no-build -- retrieve
```

The starter requests three public laptop hits. The completed input changes only `TopK` to one. Compare the first result and hit counts. Application scope stays fixed by the local checkpoint. Use the managed Search lab for the tenant and customer filtering exercise.
