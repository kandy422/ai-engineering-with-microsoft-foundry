# Module 7 learner runbook

## Launch the local support app

Use .NET SDK 10.0.401 or a later patch in that feature band, Node.js 24 or later, and PowerShell 7. From the packaged Module 7 directory:

```powershell
Set-Location lab
Remove-Item Env:Foundry__Endpoint, Env:Foundry__Deployment -ErrorAction SilentlyContinue
dotnet --version
dotnet build SupportChat.csproj
./check.ps1
dotnet run --no-build -- --urls http://127.0.0.1:8407
```

Run the check before launching the interactive server. The check creates a sibling qa directory for test output. Open the app at http://127.0.0.1:8407 and verify the Local stub label. Send a fictional question in session A, switch to B and send a different question, then return to A. Cancel a follow-up and verify that the committed history stays intact. Run the local timeout and throttle cases from the test selector. Stop the server with Ctrl+C when finished.

## Find your Foundry settings

1. In the project selector, confirm the intended subscription and project. On Home, locate the Project endpoint and Azure OpenAI endpoint fields.
2. Open Catalog to inspect model capabilities. Open Models, then Deployments. Select the intended deployment and note its alias, model, version, deployment type, and rate limits. Deployment rate limits are different from subscription quota.
3. Use the resource-level OpenAI base with the /openai/v1/ suffix for this app. Use the project endpoint for project-scoped operations. Fill in your own authorized values. Keep credentials out of files, screenshots, and shell output.

```powershell
az login
$env:Foundry__Subscription = '<authorized subscription GUID>'
$env:Foundry__Endpoint = 'https://<resource>.openai.azure.com/openai/v1/'
$env:Foundry__Deployment = '<deployment alias>'
dotnet run --no-build -- --urls http://127.0.0.1:8407
```

Use approved quota and an appropriate inference role. Verify the Microsoft Foundry mode through /mode and send only a fictional prompt within your allowance. In the app result check completion and content. Match a failure's request ID to the server log. A project URL in the endpoint variable should fail startup validation before inference.

## Set up a cost notification

In Azure Cost Management, select the authorized scope, open Budgets, and choose Add. Verify scope and billing currency. Enter the agreed amount, reset interval, validity dates, and actual or forecast threshold. Save only with the authorized recipient configuration. Reopen the saved budget and verify enabled state and thresholds. A configured alert and an observed delivery are different checkpoints. The budget itself does not stop spending. The app's fixed-window limiter rejects requests before inference, and applies only to traffic through that app instance.

## Inventory and verify final cleanup

Capture UTC time and the exact approved subscription and resource-group scope before disposal. List group resources and explicitly capture Foundry projects, deployments, and scoped role grants because the top-level resource list can omit child objects. Export needed data, traces and receipt files. Confirm dependencies have finished before the authorized owner deletes the exact disposable scope. Verify the group existence result and inventory from the parent subscription. Identify resources intentionally retained outside the group. Review Cost Analysis for the same scope and usage period once reporting data is available. Empty billing rows leave cost unverified.

```powershell
az resource list --subscription $subscription --resource-group $group -o table
az group exists --subscription $subscription --name $group
```

Microsoft references

- https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/tutorial-acm-create-budgets
- https://learn.microsoft.com/en-us/azure/foundry/concepts/manage-costs
- https://learn.microsoft.com/en-us/cli/azure/group


## Lesson checkpoints

| Lesson | Exercise | What to verify |
| --- | --- | --- |
| 7.1 | Follow the Foundry navigation above in your own project. | Write down the project endpoint type, resource inference route and deployment alias. |
| 7.2 | Review Budgets at the approved scope. Open `qa/workflow/capture.ps1`, inspect `Invoke-LocalRun`, and run the local exercise below. | The same three requests return 200/200/429 and 200/429/429 at two and one permit. Try three permits and verify all three requests return 200. |
| 7.3 | Optionally open your deployment's Playground and ask “Where is my order?” with instructions to ask for a reference and state the lack of lookup capability. | The response requests a reference, claims no lookup capability, and shows usage if reported. |
| 7.4 | Reproduce local validation with the example project URL below. For optional live replay, set the authorized inference base and deployment alias from the earlier section. | Startup rejects the project URL. In hosted mode, distinguish application completion from HTTP status and inspect the request ID. |
| 7.5 | Send fictional prompts in A and B, then return to A and cancel a follow-up. Run the UI state test against the local stub. | B has its own history and the cancelled preview never commits to A. |
| 7.6 | Run the stream check below while the local stub runs. | The fixed prompt produces a done event normally and error events for the two simulated failures. |
| 7.7 | With the interactive server stopped, run the smoke check and locate a matching ID in its output files. | The response and server log agree on the request ID and terminal outcome. |
| 7.8 | Rebuild and rerun local checks. Inventory your authorized disposable lab before cleanup and compare the after-inventory. | The local app still builds. The cloud evidence identifies removed and retained scopes and states the billing data available. |

From the packaged Module 7 directory, stop any running lab server and run the practice card exercise. The script starts a fresh loopback server for each setting and prints the check results. Use `-TryThree` to test the third setting.

```powershell
./qa/workflow/capture.ps1 -TryThree
```

To change the limiter manually, use two PowerShell terminals in the packaged lab directory and start a fresh local server for each setting.

```powershell
# Server terminal
$env:Chat__PermitLimit = '2'
dotnet run --no-build -- --urls http://127.0.0.1:8407

# Test terminal
node workflow-check.mjs limit 2
```

Stop the server, repeat with 1 in both commands, then clear Chat__PermitLimit and restart for the streaming check.

```powershell
node workflow-check.mjs stream
```

After stopping the interactive server, run the smoke and browser-logic checks:

```powershell
Remove-Item Env:Chat__PermitLimit -ErrorAction SilentlyContinue
./check.ps1 -Run trace
./check.ps1 -Run state -UiState
```

Read ../qa/trace-smoke.json and ../qa/trace-server.log. The UI state check runs the browser script in a Node document fixture with the actual localhost server and a local model stub.

To reproduce the 7.4 startup error, use fictional values from the lab directory and restore the settings afterward.

```powershell
$env:Foundry__Endpoint = 'https://example.services.ai.azure.com/api/projects/training'
$env:Foundry__Deployment = 'training'
dotnet run --no-build -- --urls http://127.0.0.1:8407
Remove-Item Env:Foundry__Endpoint, Env:Foundry__Deployment
```

