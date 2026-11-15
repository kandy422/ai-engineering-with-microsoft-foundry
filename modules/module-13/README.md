# Module 13 — Evaluate and improve the whole system

Open PowerShell in the extracted module folder. Install the .NET SDK pinned in `global.json` (10.0.403) and Python 3.12 (verified with 3.12.10). The local C# project has no NuGet packages, and the Python scripts use only the standard library. The optional Foundry project pins Azure.AI.Projects 2.1.0-beta.4 and locks the dependency tree. Azure CLI with Entra sign-in is required only for a new hosted run.

Build once, then run a lesson from inside `lab`. Each row below gives the argument to replace `13.1` in the example.

```powershell
Push-Location lab
dotnet build Evaluation.csproj --nologo
dotnet run --no-build --project Evaluation.csproj -- 13.1
Pop-Location
```

| Lesson | Argument | Checkpoint and exercise |
| --- | --- | --- |
| 13.1 Define what good means | `13.1` | Read `rubric.json` and predict why a baseline with 100% routing and support is blocked. Change the allowed action in a scratch copy of the baseline only if the scenario permits it, then rerun and explain any other threshold that still blocks release. |
| 13.2 Build an evaluation dataset | `13.2` | Verify zero cross-split families. Copy a case into the other split with a new ID but the same family and verify the leakage error. Restore the file. |
| 13.3 Evaluate retrieval and answers separately | `13.3` | Find the case with expected evidence cited and an incorrect answer. Fix only its answer in `rag-v1.json` and verify that the retrieval scores stay fixed. |
| 13.4 Use model-based graders carefully | `13.4` | Compare the deterministic calibration checks (2/3 and 3/3), then group the recorded local model verdicts in `judge-trials-v1.json` by rubric and case. Which case changes verdict across seeds? Inspect its authored label in `judge-cases-v1.json`. |
| 13.5 Trace and evaluate agents | `13.5` | Compare the planning error, tool execution error, and completed trace. Change the bad plan to the intended tool order and arguments in a scratch copy, introduce a tool failure, and check the new diagnosis. |
| 13.6 Run experiments in Foundry | `13.6` | The local checkpoint writes keyword and scoped-keyword JSONL. From the module root, run the frozen holdout and replay below. Explain a keyword failure and follow one recovered judge grade to its original HTTP 429. Draft a separate paraphrase slice and freeze its labels before testing it. |
| 13.7 Turn failures into regression tests | `13.7` | Verify that the targeted fix passes, while the baseline and blanket ban block. Rerun `gate targeted` and `gate blanket-ban` as arguments and check exit codes 0 and 1. Add a new held-out case for a blocked action and explain whether it requires a change to the gate. |

Run exercises in a copy of the module. Restore edited inputs from the extracted original before moving on. Lesson 13.4 includes saved judge outputs so its exercise runs without a model server. Those verdicts are observations of the recorded run.

## 13.6 Reproduce the recorded evaluation and recovery

Run from the module root on Windows. The preparation command rebuilds the data with the recorded line endings so the exact submitted dataset bytes can be checked against the recorded hashes.

```powershell
py -3.12 remediation-13.6/prepare.py
dotnet restore foundry-lab/FoundryExperiment.csproj --locked-mode --source https://api.nuget.org/v3/index.json
dotnet run --no-restore --project foundry-lab/FoundryExperiment.csproj -- held-out
dotnet run --no-build --project foundry-lab/FoundryExperiment.csproj -- held-out recover prepare
py -3.12 remediation-13.6/compare.py course-20261115
py -3.12 remediation-13.6/join-recovery.py
```

The last two commands read packaged instructor results. The original comparison pairs 12 cases and records seven grader errors. The recovery join verifies those seven replacement rows against the original items and dataset hashes. Compare keyword at 7/12 and scoped-keyword at 12/12 on both exact match and completed model grades. The keyword judge recorded five valid failures. These commands generate evidence files under `remediation-13.6` and make zero model requests. The saved run metadata contains the fields used for reproduction, and the course project endpoint is replaced by a placeholder.

For a new hosted experiment, use your own authorized Foundry project, Entra identity and a fresh batch ID. The course recovery runner is pinned to the instructor's original seven errors. See the detailed [hosted setup, cost and recovery steps](remediation-13.6/README.md).
