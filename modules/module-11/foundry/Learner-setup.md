# Module 11 learner setup

AI Engineering with Microsoft Foundry · Tools, workflows and agents

Use the packaged module directory as the working directory. Install .NET 10 SDK (tested with 10.0.403). Internet access to NuGet is required for the initial locked restore.

```powershell
dotnet restore lab --locked-mode
dotnet build lab --no-restore --nologo
dotnet run --project lab --no-build -- 11.2
dotnet run --project lab --no-build -- 11.5
dotnet run --project lab --no-build -- 11.6
dotnet run --project lab --no-build -- 11.7
dotnet run --project lab --no-build -- 11.8
```

| Checkpoint | Expected observation |
|---|---|
| 11.2 | Valid input ships. A number and extra caller field are rejected. A different customer's order is unavailable. |
| 11.5 | Only the original caller can resume the unexpired fact. Expired and forgotten facts read as null. |
| 11.6 | The retry returns the same ticket after an injected timeout. The loop stops after three steps. |
| 11.7 | An altered or unconfirmed proposal is rejected. Applying and replaying an approved proposal creates one audit entry. |
| 11.8 | The local MCP subprocess advertises the tool and denies the other customer's lookup. |

For 11.1 and 11.3, the numeric checkpoints use scripted fixtures. The model experiments in 11.1, 11.3 and 11.9 require Python 3.12, the pinned llama.cpp b12287 Windows CPU runtime at `.model/runtime/llama-server.exe` and Qwen3-0.6B-Q8_0 at `.model/Qwen3-0.6B-Q8_0.gguf`. Use the pinned downloads and archive hashes in the [module README](../README.md). The model SHA-256 is `9465E63A22ADD5354D9BB4B99E90117043C7124007664907259BD16D043BB031`. Run `py -3.12 model-checkpoint.py live-tool-loop --repeat` after building the C# lab. It binds to loopback port 8411, runs both model checkpoints, and saves traces under `qa/reproduction`. A fresh run can have different timing and generated call identifiers. Source, lockfiles and runner are the learner inputs. Keep weights, generated media and build caches out of the distributed package.

## 11.4 · Foundry Agent Service

```powershell
dotnet restore foundry --locked-mode
dotnet build foundry --no-restore --nologo
dotnet run --project foundry --no-build -- --local
```

The local command checks six application-side branches against fictional fixtures. It requires no Azure sign-in. Keep the sibling `lab/Orders.cs` in the learner package because the Foundry project compiles it as shared source.

The managed run additionally needs a Foundry project, an Agent Service and function-tool compatible model deployment in that project's region, and Azure CLI signed in with the intended Entra principal. Verify the principal has project-scoped Foundry User permissions for version creation, conversations and inference, and confirm the project managed identity has its required Foundry User assignment on the account. Trace review needs connected Application Insights and Log Analytics Reader access, with additional protected-table read permissions only where applicable. Confirm existing assignments before requesting anything broader. [Foundry RBAC](https://learn.microsoft.com/en-us/azure/foundry/concepts/rbac-foundry) · [Tracing setup](https://learn.microsoft.com/en-us/azure/foundry/observability/how-to/trace-agent-setup)

Select the approved Azure CLI subscription. From the project overview, copy the endpoint in the form `https://<account>.services.ai.azure.com/api/projects/<project>`. Use the deployment alias, not the catalog model ID unless they are identical. Use your own approved Azure lab values. Choose a unique lowercase agent name and a local run label for FOUNDRY_RECORDING_ALLOCATION; the label is a guard and receipt field, not an Azure credential. The command creates one agent version and two conversations.

```powershell
az account set --subscription '<approved-subscription>'
$env:FOUNDRY_PROJECT_ENDPOINT = '<project-endpoint>'
$env:FOUNDRY_MODEL_DEPLOYMENT = '<approved-deployment-alias>'
$env:FOUNDRY_AGENT_NAME = '<allocated-unique-agent-name>'
$env:FOUNDRY_RECORDING_ALLOCATION = '<coordinator-allocation-id>'
dotnet run --project foundry --no-build -- --run-authorized-cloud-demo |
  Tee-Object -FilePath foundry/live-11.4.ndjson
$LASTEXITCODE
```

The locked adapter uses Azure.AI.Projects, Azure.AI.Projects.Agents and Azure.AI.Extensions.OpenAI 3.0.0-beta.6, OpenAI 2.14.0 and Azure.Identity 1.21.0. It selects project API v1 and uses CLI credentials. One invocation attempts one agent version, two independent conversations and at most four Responses operations with 512 output tokens per call, automatic retries disabled in the project client options, a 60-second network timeout and a four-minute batch deadline. Agent-selected tool calls are handled in C# using Alex's fixed fictional identity. Each accepted first response can lead to at most one local invocation and one correlated follow-up. Reconcile actual usage from all successful response receipts. An interrupted or failed attempt may still have created resources or incurred usage. Inspect partial evidence before any rerun. After saving your evidence, have the authorized lab operator remove only the agent version and conversations created by your run, using the identifiers in its receipt and your lab cleanup procedure. Keep shared deployments and monitoring intact. [Function tool flow](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/tools/function-calling)

### Recording acceptance

1. Show the project overview and deployment alias, then the real agent version and function definition. Capture the source path and command. Mask unrelated private identifiers and secure configuration.
2. Keep the same agent, deployment, trusted caller and question pattern. Change only ORD-1042 to ORD-2045 in the second conversation. Save both prompts, conversation IDs, tool arguments, call IDs, local results and correlated submissions.
3. Verify the actual first response requests the correct tool and exact order once per case. Inspect both final texts. The allowed case must report Shipped. The denied case must say the status is unavailable and disclose no ownership or private status. The harness checks tool outcomes and response structure. Semantic acceptance comes from the recorded final texts.
4. Open Foundry agent Traces, narrow the UTC time window, and correlate the actual run using the agent version and available response or conversation IDs. Save trace IDs and a sanitized capture showing the matched operations. The application-owned lookup is evidenced by the C# log. Describe it as a portal span only if instrumented and actually observed.
5. Preserve misses and errors. Hand off sanitized NDJSON, trace proof and evidence of actual usage. An absent trace or incomplete chain remains a gate.


### Match a hosted response to telemetry

Open Application Insights Logs for the connected project. Use the response ID from your application receipt. Run this query within the recording window; set the time picker to include the run.

```kusto
let responseId = "<response-id-from-receipt>";
dependencies
| where tostring(customDimensions["gen_ai.response.id"]) == responseId
| project timestamp, name, success, operation_Id,
          responseId=tostring(customDimensions["gen_ai.response.id"]),
          conversationId=tostring(customDimensions["gen_ai.conversation.id"])
| order by timestamp asc
```

Check that the agent invocation shows the same conversation ID as the receipt. Use the operation ID to inspect other spans under that operation. Local tool authorization remains evidenced by the application log. The course recording matched all four hosted response receipts; each has one successful agent invocation span. The two final response operations also have model chat spans in the exported query.

