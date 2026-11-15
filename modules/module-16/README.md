# Module 16 · End-to-end capstone

This module brings together a policy assistant, authenticated order lookup,
an explicit callback approval flow, and receipt corroboration. Run the
Windows local checkpoints first, then repeat the relevant cases against
your own approved Azure deployment.

## Setup

From `learner-course/modules/module-16`, use PowerShell 7 and the .NET 10
SDK. Keep Module 1 in the adjacent `module-01` directory. The integrated
local runner publishes its starter order service to a new evidence directory.
Use separate evidence directories for successive runs.

```powershell
dotnet build lab/Capstone.csproj
pwsh -NoProfile -File lab/run-upstream.ps1 -EvidenceDir ./evidence/first-integration
```

The integration run should pass 19 checks. The local architecture suite was
tested with Node.js 24.16.0, npm 11.13.0, and Azurite 3.37.0. Install the
tested Node.js version with its bundled npm, then install and verify the
pinned emulator. [Microsoft's Azurite installation guide](https://learn.microsoft.com/en-us/azure/storage/common/storage-install-azurite?tabs=npm)

```powershell
node --version          # v24.16.0
npm --version           # 11.13.0
npm install --global azurite@3.37.0
azurite-table --version # 3.37.0
```

In a second PowerShell terminal at the module root, start only the Table
service.

```powershell
New-Item -ItemType Directory -Force ./evidence/azurite | Out-Null
azurite-table --tableHost 127.0.0.1 --tablePort 10002 --location ./evidence/azurite
```

Back in the first terminal, run the C# check project.

```powershell
dotnet run --project tests/HostedChecks.csproj
```

The C# suite exercises real emulator transactions and controlled HTTP
responses. Its receipt replay reads the included fictional image and a
recorded Document Intelligence response. The local fixture web app listens
on port 8416. The upstream starter listens on 8401.

## Lesson checkpoints

| Lesson | Start with | Check and extend |
| --- | --- | --- |
| 16.1 Define the product and acceptance tests | Run `lab/run.ps1` and read `lab/acceptance.ps1`. | Write expected outcomes for an answer, refusal, order lookup, and request approval before inspecting the responses. Change one expected citation, observe the failure, and restore the verified expectation. |
| 16.2 Integrate knowledge and model layers | Open `lab/ScopedSearch.cs`, `lab/HostedKnowledge.cs`, and the hosted index template. | Trace one question from trusted scope to semantic hybrid retrieval, one model-selected ID, the conflict query, and the returned canonical answer. Try an unsupported eligibility condition and inspect the empty citation. |
| 16.3 Add tools and the user experience | Run the integrated check, then inspect `lab/DurableApproval.cs` and the hosted browser code. | Compare scope A and scope B for one order. Propose a callback, approve it, and retry with the same proposal ID. With the hosted receipt setup, compare the matching and changed-reference fixtures against the same canonical order. |
| 16.4 Test the complete system | Run `tests/HostedChecks.csproj` with Azurite running. Use the hosted `Evaluate` stage against a deployed revision. | Add a new held-out paraphrase and a partially answerable question. Record expected answers, abstentions, and safety outcomes before running. Compare a knowledge failure with a still-available order lookup. |
| 16.5 Release and operate | Rehearse with `lab/rehearse-release.ps1`, then use the Module 15 deployment guide. | Compare baseline and candidate for the same cases, inspect correlated traces on your deployed target, and verify the restored baseline after rollback. |
| 16.6 End-to-end demonstration | Follow [the hosted setup](lab/Hosted-checkpoint.md). | Demonstrate a policy answer, scoped order, explicit approval, replay after restart, and receipt corroboration. Record outcomes and limits, then add a new policy with an independent held-out evaluation. |

## Hosted run

Use the [hosted setup and acceptance guide](lab/Hosted-checkpoint.md) for
Entra registrations, runtime roles, index and storage seeding, deployment
settings, and review commands. The Azure services and short-lived tokens come
from your authorized environment. The local run uses its own fictional fixture
records. The deployed run uses the enrolled tenant and customer scopes.

- Use order `ORD-1042` and ticket `T-17` for the scripted scoped acceptance.
  Use a separate fresh fictional ticket for the browser approval so both
  writes can be observed independently. Reset sample data only in your own
  disposable environment.
- The receipt form accepts a PNG or JPEG up to 100,000 bytes. Use
  `tests/fixtures/lumen-receipt.png` for the match and
  `tests/fixtures/lumen-receipt-reference-mismatch.png` for the changed reference.
  Configure the canonical sample order's string `ReceiptReference` as `048271`.
- Keep request IDs, correlation IDs, redacted results, and expected outcomes
  together. Review the hosted trace and compare activity across the candidate
  and restored baseline. Remove learner-created billable lab resources after
  capturing the required evidence.

The optional `lab/run-model.ps1` path uses the pinned Module 8 local model
and runtime in the adjacent module. The normal fixture and hosted workflows
do not require those binary assets.
