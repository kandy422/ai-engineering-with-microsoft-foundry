# Module 14 · Security and responsible AI

This module is part of **AI Engineering with Microsoft Foundry**. The local labs use synthetic support data and .NET SDK 10.0.403. Run the commands from this module directory. The C# projects target `net10.0` and have zero external package dependencies. Python 3.12 is only needed for the optional offline receipt checker.

```powershell
dotnet --version
dotnet build lab/BoundaryLab.csproj
dotnet run --no-build --project lab/BoundaryLab.csproj -- 14.1
```

Change the lesson ID to run the corresponding checkpoint. Compare its output with `lab/<lesson>-results.txt`. For the video walkthroughs, use the baseline and revised snapshots in each lesson's case directory. For example, reproduce 14.1 with the following commands in a disposable copy of the module.

```powershell
$case = 'workflow/cases/14.1'
Copy-Item "$case/baseline.cs.txt" "$case/Program.cs" -Force
dotnet run --project "$case/BoundaryWorkflow.csproj" --verbosity quiet
Copy-Item "$case/revised.cs.txt" "$case/Program.cs" -Force
dotnet run --project "$case/BoundaryWorkflow.csproj" --verbosity quiet
```

Compare both runs to `baseline.stdout.txt` and `revised.stdout.txt` in the case directory. Read the source snapshots to find the specific edit before attempting the exercise.

| Lesson | Checkpoint | Exercise |
|---|---|---|
| 14.1 · Threat boundaries | The inventory identifies assets, boundaries and proposed controls. The ownership check permits the matching customer. | Change the synthetic owner and predict which result changes. |
| 14.2 · Prompt injection | The capability allowlist permits the named read action. The original lab routes a preclassified document for review. | Add a fictional unsupported capability and confirm the gateway denies it. |
| 14.3 · Identity and access | The fixture filters documents by customer audience and tenant, and denies out-of-scope orders. | Add a staff-only record and check the document decision. |
| 14.4 · Private data | The telemetry projection excludes the synthetic email. Repeating the local deletion leaves zero records. | Add a synthetic private field and check what the projection emits. |
| 14.5 · Safe failure | The revised fallback routes an unavailable signal to human review. | Add an unknown signal and check the route. |
| 14.6 · Fairness and oversight | An added phrase fixes one observed miss. The unrelated prompt still needs review. | Add a different paraphrase and record the baseline outcome before changing the rule. |
| 14.7 · Release review | Only the matching request is permitted. Deployment evidence remains pending and the release is held. | Change the approved request ID and confirm the two decisions and release hold. |

Local outputs describe the fixture behavior. Live detection, hosted data scoping and durable writes require separate evidence. The [capstone acceptance runbook](production-acceptance/README.md) explains the prepared cases and how to check sanitized evidence from a coordinator-authorized run.
