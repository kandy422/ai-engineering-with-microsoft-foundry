# Deploy and operate the support capstone

Use PowerShell 7.2+, .NET 10, and the Azure CLI with Container Apps support and Bicep. Use the Module 16 hosted project, a seeded course Search index, an existing Entra-only Storage account in the lab group, the approved Foundry OpenAI v1 endpoint/deployment, and a single-tenant Microsoft Entra API registration with v2 access tokens and delegated `Support.Access`. Use a preapproved client application that can request this delegated scope for the mapped test users. Keep tokens in memory and use synthetic cases. Azure CLI's ordinary management token has the wrong audience.

## Prepare the lab and image

From this folder, set the shell variables for your approved subscription, disposable group, prefix, Entra registrations and shared dependencies. The command runner requires deployment rights in the disposable group and permission to assign AcrPull on the registry, Storage Table Data Contributor on the `SupportActions` table, Cognitive Services OpenAI User on the Foundry account, and Search Index Data Reader on each approved index.

```powershell
az account set --subscription $subscription
az deployment group what-if -g $group -f foundation.bicep -p prefix=$prefix location=eastus2 storageAccount=$storageAccount
az deployment group create -g $group -n capstone-foundation -f foundation.bicep -p prefix=$prefix location=eastus2 storageAccount=$storageAccount --query properties.outputs.ids.value -o json > foundation-ids.json
$ids = Get-Content foundation-ids.json -Raw | ConvertFrom-Json
$registry = $ids.registry.Split('/')[-1]
az acr config authentication-as-arm show -r $registry
$context = (& ./New-BuildContext.ps1 | Select-Object -Last 1).FullName
az acr build -r $registry -t "capstone:$tag" --platform linux/amd64 --build-arg "RUNTIME_IMAGE=$runtimeImage" $context
$digest = az acr repository show -n $registry --image "capstone:$tag" --query digest -o tsv
```

Review ACR authentication-as-ARM before build and pull. The runtime image must be the approved `mcr.microsoft.com/dotnet/aspnet:10.0@sha256:...` digest for Linux AMD64. Use a unique image tag and retain the immutable registry digest, runtime digest, source revision/hashes and build run ID. The build context carries published app files only. A local Docker daemon is optional for ACR builds.

## Install and verify the baseline

```powershell
$base = @{
  FoundationIds = 'foundation-ids.json'
  App = "$prefix-support"
  Digest = $digest
  Tenant = $tenant
  Audience = $apiClientId
  SpaClientId = $spaClientId
  Customers = @{ "$testOidA" = 'customer-a'; "$testOidB" = 'customer-b' }
  AutomationClients = @{
    "$automationOidA" = @{ ClientId = $automationClientIdA; Customer = 'customer-a' }
    "$automationOidB" = @{ ClientId = $automationClientIdB; Customer = 'customer-b' }
  }
  FoundryEndpoint = $foundryEndpoint
  Deployment = $modelDeployment
  SearchEndpoint = $searchEndpoint
  SemanticConfiguration = $semanticConfiguration
}
./New-AppParameters.ps1 @base -Output baseline.json -Index $searchIndex -Suffix baseline
az deployment group what-if -g $group -f app.bicep -p '@baseline.json'
az deployment group create -g $group -n capstone-baseline -f app.bicep -p '@baseline.json' --query properties.outputs -o json > baseline-outputs.json
$deployment = Get-Content baseline-outputs.json -Raw | ConvertFrom-Json
$principal = $deployment.principalId.value
./Set-SharedAccess.ps1 -Subscription $subscription -Principal $principal -FoundryAccountId $foundryId -SearchIndexId $indexId -Receipt baseline-grants.json
```

Verify revision and authentication configuration and allow for RBAC propagation. Register the exact SPA redirect URI `https://<app-host>/hosted/redirect.html`. Use the same path with the revision host if recording sign-in there. Container Apps sets authentication to return 401 for unauthenticated requests outside the explicit public shell paths. The app independently validates the JWT and authorizes its customer scope. Confirm the shell, sign-in configuration and `/health` work anonymously on the primary and revision origins. Confirm an anonymous POST to `/answer` returns 401. Sign in through MSAL and exercise `/answer`, `/order`, `/propose` and `/approve` using Module 16's published test cases. Browser access uses `api://<api-client-id>/Support.Access`. The two approved certificate clients use the API's `Support.Automation` app role and request `api://<api-client-id>/.default`. Configure the API registration to emit the optional `idtyp` access token claim. The app checks `idtyp=app`, the certificate client authentication claim `azpacr=2`, and the enrolled application and service principal IDs. Use the enterprise application's service principal object ID as the automation mapping key and its application client ID as `ClientId`. Record customer separation, approval replay and persistence after a revision restart.

## Challenge a change, shift traffic and roll back

Deploy a candidate with `-PreviousRevision "$($base.App)--baseline"`. Pin the unchanged digest when only a setting changes. Use a new revision suffix. Call the candidate's direct authenticated origin at zero public traffic using `Measure-HostedRequest.ps1`. For the dependency drill, configure a unique nonexistent Search index for that revision and run one request with `-ExpectedStatus 503`. Check the real outcome and trace, then deactivate the failed revision. Verify the main route still serves the baseline answer. There is no reason to grant access to the nonexistent index or alter the shared Search service for this drill.

For a healthy candidate, use a real seeded candidate index with its matching scope grant. Use `Set-SharedAccess.ps1 -SearchOnly` with a separate receipt for the candidate index. Run matching answer, denied-scope and durable replay checks against both revisions before promotion. Try 10% then 100% using the script below and capture actual traffic after each change. Repeat with `FromRevision` and `ToRevision` swapped to restore the baseline to 100%.

The hosted course regression uses the revision suffixes `baseline` and `candidate` with indexes `course16-support-v1` and `course16-support-candidate`. To reproduce that bounded release check, provide the enrolled customer A bearer in the current process environment and run:

```powershell
./Invoke-ReleaseAcceptance.ps1 -Subscription $subscription -ResourceGroup $group -App $base.App -Receipt release-acceptance.json
```

It sends the same four read-only questions to both revisions, verifies the candidate at 100% on the main route, then restores and verifies the baseline in a finally block. Review any failed receipt and confirm the intended route before continuing. Remove the bearer from the process environment after testing.

```powershell
./Switch-Traffic.ps1 -ResourceGroup $group -App $base.App -FromRevision $baseline -ToRevision $candidate -ToWeight 10 -AccessToken $token
./Switch-Traffic.ps1 -ResourceGroup $group -App $base.App -FromRevision $baseline -ToRevision $candidate -ToWeight 100 -AccessToken $token
./Switch-Traffic.ps1 -ResourceGroup $group -App $base.App -FromRevision $candidate -ToRevision $baseline -AccessToken $token
```

For telemetry, start with [operations.kql](operations.kql). Correlate actual console or Application Insights records by the W3C trace ID. Pass `-EnableApplicationInsights` to the parameter generator only when the packaged app contains a verified exporter; otherwise show console evidence. Count observed Foundry tokens and summarize measured timings per operator run. A few smoke requests demonstrate outcomes and timing for those cases. Use a separate approved traffic budget for a capacity test. Optimization requires the same cases, scope and comparison criteria across both revisions; show regressions or inconclusive results as measured.

For Module 11, the Application Insights target is `$ids.insights` in workspace `$ids.workspace`. In the current Foundry portal, open **Build → Agents → Traces → Connect** in `support-assistant` and select `$ids.insightsName`. If the Connect button is absent, try **Manage → Project details → Connected resources → Add connection → Application Insights**. Where supported and approved, choose **Project Managed Identity** and record its scope-specific Monitoring Metrics Publisher assignment. Confirm the connection, run a single approved agent request and capture the project trace and matching Application Insights entry. Keep any connection string out of manifests and screen captures. The [Microsoft Foundry tracing guide](https://learn.microsoft.com/en-us/azure/foundry/observability/how-to/trace-agent-setup) describes reader permissions and trace verification.

## Tear down

Export redacted receipts, traces, policy and traffic outcomes, runtime/image IDs, and a cost snapshot. Keep the known-good index and image through the rollback window. Remove Foundry/Search grants using `Set-SharedAccess.ps1 -Remove` with each original receipt and the same `-SearchOnly` selection. Then, using exact recorded IDs, have the coordinator remove the app, table role assignment and table, environment, ACR, pull identity, Application Insights component and workspace after their consumers finish. Verify the recorded grants and app-owned resources are absent. Shared Search, Storage and Foundry resources are coordinator-owned and have a separate cleanup window. Remove course-only Entra registrations separately if approved and owned by the operator.

For the Search cleanup demonstrated in 15.8, work from `learner-course/modules/module-15/deploy` in PowerShell 7. Use your own approved subscription ID, resource group name and disposable Search service name in `$subscription`, `$group` and `$searchName`. The instructor’s execution inventory is recording evidence. Resolve and save your own full resource ID while the service still exists, and compare its subscription, group and name with your lab plan.

```powershell
$lookup = @(
  '--subscription', $subscription, '--resource-group', $group
  '--name', $searchName, '--resource-type', 'Microsoft.Search/searchServices'
  '--query', 'id', '-o', 'tsv'
)
$searchId = az resource show @lookup
if ($LASTEXITCODE -ne 0 -or !$searchId) { throw 'No reviewed Search ID' }
$searchId
```

Once the owner has closed every Search-dependent capture and verified this exact ID, run `az resource delete --ids $searchId`. Verify the same target with `az resource show --ids $searchId` and save the `ResourceNotFound` receipt. Keep the Foundry project available if a separate exercise still depends on it. Reconcile costs after the billing view catches up.
