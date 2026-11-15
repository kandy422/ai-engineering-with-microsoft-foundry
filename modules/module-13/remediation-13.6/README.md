# Evaluate a retrieval change in Microsoft Foundry

Use .NET 10, Python 3.12, Azure CLI and a Microsoft Foundry project. Run the commands from the Module 13 directory. The SDK package lock resolves Azure.AI.Projects 2.1.0-beta.4 and its dependencies. Lesson 13.6 reuses the local C# checkpoint to produce the two datasets. The project endpoint and judge are needed for the hosted step.

1. Read `remediation-13.6/held-out-v1.json`. Each row has a case ID, policy scope, failure slice, request, expected document ID and reference decision. The accompanying SHA-256 pins the reference labels before the comparison.
2. Run the local preflight. The C# checkpoint writes two JSONL datasets for the same 12 questions. Both versions use the same policy snapshot and ranker. The scoped version filters candidates before ranking. The generated `prepared-inputs.json` records both dataset hashes and the observed local mismatches.

```powershell
py -3.12 remediation-13.6/prepare.py
dotnet restore foundry-lab/FoundryExperiment.csproj --locked-mode --source https://api.nuget.org/v3/index.json
dotnet run --no-restore --project foundry-lab/FoundryExperiment.csproj -- held-out
```

3. For the hosted run, sign in through Azure CLI and use your assigned project endpoint. The course environment uses the East US 2 Phase A project and its `course-chat` Global Standard deployment of GPT-5-mini (2025-08-07). Use a project whose deployment has that name. The operator needs Foundry User access for dataset upload, evaluation creation, submission and output reads. Because the evaluation invokes a model, verify Foundry User at the Foundry account scope. The plan uses default project storage. If a separate Entra-connected storage account is used, verify Storage Blob Data Contributor for the project managed identity there. The evaluation uses precomputed dataset inputs and requires no Application Insights workspace. [Permission requirements](https://learn.microsoft.com/en-us/azure/foundry/observability/how-to/evaluation-permissions) · [Evaluation region availability](https://learn.microsoft.com/en-us/azure/foundry/concepts/evaluation-regions-limits-virtual-network)

4. Use the coordinator's assigned batch ID for the course recording. The command creates one evaluation definition, uploads two versioned JSONL datasets, and submits two runs. The request sets one exact match grader and one model grader per item. The grader uses minimal reasoning with a 1,024 completion-token limit. The C# client saves the resource IDs, raw responses and request IDs when supplied. If submission stops partway, inspect the saved receipts and the portal before any new submission. The same batch ID cannot be submitted twice by this runner.

```powershell
$env:AZURE_AI_PROJECT_ENDPOINT = 'https://<account>.services.ai.azure.com/api/projects/<project>'
$batch = '<allocated-batch-id>'
dotnet run --no-build --project foundry-lab/FoundryExperiment.csproj -- held-out submit $batch
dotnet run --no-build --project foundry-lab/FoundryExperiment.csproj -- held-out collect $batch
```

5. Repeat the read-only collect command after the runs finish. Inspect both runs under Evaluations in the same project. Save the run summary and the two individual results for `hold-05` and `hold-10`. Compare the exact grade, judge grade, question, retrieved context, candidate, and reference. Capture the evaluation and run IDs so the screenshots can be traced to the JSON receipts.

```powershell
py -3.12 remediation-13.6/compare.py $batch
```

The resulting `comparison.csv` retains actual grader disagreements and failure rows. The `item-trace.json` links the local policy lookup, Foundry runs and service items by case ID. A completed evaluation can contain failed items. Report missing outputs and judge errors separately from low scores. The 12 fictional cases teach experiment design and failure diagnosis. They cannot establish generalization. Add a separate paraphrase slice for a new experiment, freeze the references, and check whether the same comparisons still hold.

The batch schedules 24 model grades. At 2,048 estimated prompt tokens and up to 1,024 completion tokens per grade, Global Standard retail at $0.25 and $2.00 per million tokens gives an estimated $0.06144 in model tokens. Allocate $0.10 of model-token planning headroom and check for storage or platform charges before submission. Service-side retries and prompt overhead can affect actual usage. Reconcile the observed usage to the course ledger. [Azure pricing](https://azure.microsoft.com/en-us/pricing/details/azure-openai/)

[Dataset SDK workflow](https://learn.microsoft.com/en-us/azure/foundry/observability/how-to/cloud-evaluation-datasets) · [Item-level results](https://learn.microsoft.com/en-us/azure/foundry/observability/how-to/cloud-evaluation-results) · [Foundry grader request schema](https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/aiproject)

## Inspect and recover throttled grades

The recorded evaluation returned seven HTTP 429 errors in the model grader. The team's deployment quota update raised the existing deployment from 10,000 to 50,000 tokens per minute. The recovery run used seven inline rows and the original grader. It scheduled no additional exact checks. The original exact results and 429 receipts remain part of the comparison.

The following commands reproduce the course evidence workflow when the original course receipts and dataset files are available. Each command uses the original project configuration. The recovery submission starts one managed batch.

```powershell
dotnet run --no-build --project foundry-lab/FoundryExperiment.csproj -- held-out recover prepare
dotnet run --no-build --project foundry-lab/FoundryExperiment.csproj -- held-out recover submit
dotnet run --no-build --project foundry-lab/FoundryExperiment.csproj -- held-out recover collect
py -3.12 remediation-13.6/join-recovery.py
```

For an independently run experiment, review its grader errors and select its failed items by case ID and version. Keep the original evaluation and judge configuration, verify the rows against the original dataset hashes, and run a single small recovery batch sized for the available quota. The course recovery command is pinned to the recorded batch. [Inline data for small Foundry evaluations](https://learn.microsoft.com/en-us/azure/foundry/observability/how-to/cloud-evaluation-datasets)
