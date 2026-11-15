# Module 03 · Build and evaluate a small ML model

Part of **AI Engineering with Microsoft Foundry**. These labs train and evaluate small models locally using fictional tickets. They require no Foundry resource, Azure account, API key or downloaded model.

## Set up once

Use Windows PowerShell in the packaged `module-03` directory. Install CPython 3.12.10 (64-bit) and the .NET SDK 10.0.403. The latter is needed for 3.7. The included `global.json` selects that exact SDK. Check both runtimes and install the pinned Python packages in a virtual environment:

```powershell
py -3.12 --version
dotnet --version
py -3.12 -m venv .venv
$python = (Resolve-Path .venv/Scripts/python.exe).Path
& $python -m pip install -r lab/requirements.txt
$env:COURSE_PYTHON = $python
$env:OMP_NUM_THREADS = '2'
```

Run subsequent commands from this same directory and PowerShell session. In the videos, `python` means the interpreter in your active environment. The commands here use its exact path without requiring activation. Package installation may need network access. The labs run locally after installation.

The package contains runnable source and data. Before training, `lab/router.joblib` should be absent. Lesson 3.2 creates it from the training split. Only load the artifact you created yourself. The package does not ship trained weights. Keep the `train`, `development`, and `final` splits in `lab/data.json` separate. The final split is reserved for 3.7.

## Run the lessons

| Lesson | Open first | Run | Completed checkpoint |
| --- | --- | --- | --- |
| 3.1 Baselines | `lab/baseline.py`, `lab/data.json` | `& $python lab/baseline.py` | Majority gets 10/14 correct and catches 0/4 urgent. Keywords get 11/14 and catch 2/4. Both use the same development tickets. |
| 3.2 Train a classifier | `fit()` in `lab/experiments.py`, training rows in `lab/data.json` | `& $python lab/experiments.py fit` | Creates and reloads `lab/router.joblib`. Gets 12/14 correct on development, with 2/4 urgent caught. Compare the returned per-ticket predictions with 3.1. |
| 3.3 Confusion matrix | `metrics()` in `lab/experiments.py` | `& $python lab/experiments.py metrics` | In routine/urgent order: TN 10, FP 0, FN 2, TP 2. Urgent precision 1.0, recall 0.5, F1 about 0.667. Read the two actual missed tickets. |
| 3.4 Model complexity | `complexity()` in `lab/experiments.py` | `& $python lab/experiments.py complexity` | 160 seeded rows and seven depths. Depths 2 and 4 tie at 0.713 validation accuracy; the first tied maximum selects 2. Depth 16 reaches 1.0 training and 0.669 validation. |
| 3.5 Regression | `regression()` and `lab/resolution.json` | `& $python lab/experiments.py regression` | Mean baseline MAE 15.0 minutes and RMSE 16.54. Fitted line MAE 4.8 and RMSE 6.59. At five checks, actual 52 minus predicted 39.39 gives residual +12.61 minutes. |
| 3.6 Clustering | `cluster()` and `lab/clusters.json` | `& $python lab/experiments.py cluster` | Three groups with 3, 3 and 4 tickets. The mixed checkout/password description groups with password tickets. Cluster numbers are identifiers, not labels. Follow the checkpoint sequence below. |
| 3.7 C# routing | `app/Program.cs`, `route()`, and `lab/app_check.py` | Run the commands below | The C# harness checks eight requests. The final Python report routes 5/8 held-out tickets, with 5/5 routed tickets correct. Three go to review. |

### 3.6 · Reproduce the setting change and repair

The three snapshots in `lab/` are the exact Python sources for the recorded walkthrough. They live beside the common inputs and imports so each can run directly.

```powershell
& $python lab/experiments.py cluster
& $python lab/cluster-k2-before.py cluster
$LASTEXITCODE  # Expected: 1
& $python lab/cluster-k2-after.py cluster
& $python lab/cluster-k3-after.py cluster
```

The starter runs with three clusters. The first snapshot changes only `n_clusters` to 2 and intentionally raises an `IndexError` because the reporting loop assumes three groups. In the completed two-cluster snapshot, `range(k.n_clusters)` makes the report follow the fitted estimator. It returns group sizes 6 and 4. The final snapshot restores three clusters while retaining the report repair. It returns group sizes 3, 3 and 4. Compare memberships using the same descriptions and seed.

### 3.7 · Build and run the local application

Complete the 3.2 fit checkpoint first. Leave the routing threshold fixed at 0.70 for these checks.

```powershell
dotnet build app -c Release
& $python lab/app_check.py
& $python lab/experiments.py final
'{"text":"Production outage: checkout down, payments failing"}' | dotnet app/bin/Release/net10.0/TicketRouter.dll
```

The console application reads one JSON document from stdin and writes a JSON response to stdout. Expected statuses are `invalid`, `review`, `routed`, and `unavailable`. The explicit outage request returns `status: routed`, `queue: urgent`, and a score of about 0.808. This score is uncalibrated. Lesson 3.7 exercises the C# to Python process boundary; its separate held-out evaluation tests the shared Python policy.

If a previously working request returns `unavailable`, check stderr first. A missing `joblib` or `sklearn` import usually means C# launched a different interpreter. Set `$env:COURSE_PYTHON = (Resolve-Path .venv/Scripts/python.exe).Path` in the current shell and retry. If stderr reports a missing `router.joblib`, rerun the 3.2 fit checkpoint.

After completing 3.7, run `& $python verify_labs.py` to reproduce and assert all completed checkpoints. This includes the expected clustering failure and a deliberate missing-worker run. It builds the app and regenerates the model. Keep an untouched copy if you plan to edit the lab.

## Independent exercise · Improve urgent recall on development

Copy `lab/baseline.py` to `lab/baseline_exercise.py`. Change only the keyword decision rule to catch the two missed urgent tickets. Run your copy on the same development tickets and compare it with the unedited script.

Acceptance: report 4/4 urgent caught, at least 12/14 correct, and no more than one routine ticket misrouted as urgent. Name the changes and one limitation. Preserve the majority baseline and the dataset, and leave the final split unused. Run `& $python verify_labs.py --exercise` to check these conditions after implementing your rule.

## Checkpoint boundaries

The included 3.1 rules are completed references. Start 3.2 with untrained source and the provided data, then use the generated artifact for 3.3 and 3.7. Lessons 3.4–3.6 are independent experiments. Nothing in this module depends on another module's files. The console stdin/stdout JSON interface is a local teaching example; it is not the managed Foundry capstone or a production service contract.
