# Hosted support capstone checkpoint

From the Module 16 directory, run `dotnet build lab/Capstone.csproj`.
For the existing local walkthrough, run
`pwsh -File lab/run-upstream.ps1 -EvidenceDir ./evidence/learner-fixture`.
Fixture mode binds only to loopback on port 8416 and uses the historical fixture
API. Its customer fields are test inputs. Hosted mode uses Entra bearer authentication
and serves a separate browser desk at `/hosted/`.

Start the Azurite Table emulator on loopback port 10002 in another terminal,
then run `dotnet run --project tests/HostedChecks.csproj`. The Search and GPT
responses in those tests are controlled HTTP fixtures. The approval tests use
actual Azurite transactions, race several approvals, and replay in a fresh process.

Module 15 deploys the same project. Its build context is modules/module-16
and its Dockerfile is modules/module-16/Dockerfile. From the course root run
`docker build -f modules/module-16/Dockerfile -t capstone:review modules/module-16`.
Listen on 8080, with health at GET /health. The image starts in hosted mode.
The SDK can also build a local Linux archive with `dotnet publish lab/Capstone.csproj
-c Release --os linux --arch x64 /t:PublishContainer
-p:ContainerBaseImage=mcr.microsoft.com/dotnet/aspnet:10.0
-p:ContainerRepository=capstone -p:ContainerImageTag=review
-p:ContainerArchiveOutputPath=../evidence/capstone.tar.gz`, from the module.
Pass hosted settings when running the SDK-built image.

| Setting | Value supplied by coordinator |
| --- | --- |
| Capstone__Mode | hosted |
| ASPNETCORE_HTTP_PORTS | 8080 |
| Identity__TenantId | Entra tenant GUID |
| Identity__Audience | API application GUID for v2 access tokens |
| Identity__BrowserClientId | Browser SPA application GUID |
| Identity__Customers__&lt;actor-object-guid&gt; | Enrolled customer scope code for that actor |
| Identity__Automation__&lt;application-client-guid&gt; | Enterprise application object GUID for that exact client |
| APPLICATIONINSIGHTS_CONNECTION_STRING | App Insights connection string, supplied through deployment configuration |
| Document__Endpoint | Document Intelligence HTTPS resource origin with trailing slash. Enables the v3 receipt workflow. |
| Search__Endpoint | Azure AI Search service HTTPS origin with trailing slash |
| Search__Index | Versioned index name |
| Search__SemanticConfiguration | Semantic configuration name |
| Foundry__Endpoint | Azure OpenAI resource URL ending in /openai/v1/ |
| Foundry__Deployment | Approved GPT-5-mini deployment alias |
| Storage__Endpoint | Table Storage HTTPS origin with trailing slash |
| Storage__Table | Scope-limited table name |

Register a single-tenant SPA with redirect URI
`https://<app-origin>/hosted/redirect.html`. Register a single-tenant API with
Application ID URI `api://<API application GUID>`, v2 access tokens, and
delegated scope `Support.Access`. Grant the SPA only the delegated permission.
The API also requires the token's `azp` to match the configured SPA and
looks up the user's object ID in its customer enrollment map.
The browser uses the vendored MSAL Browser 4.30.0 UMD package with its access-token
cache in memory. Verify tool use with two delegated lab actors in different
customer scopes. Use fresh fixture records for browser and scripted write tests.

For service-to-service checks, add the API app role `Support.Automation` with
allowed member type `Application` and configure the optional `idtyp` access-token
claim. Use a dedicated client app with a certificate and assign the app role to
its service principal. Request `api://<API application GUID>/.default` from the
tenant authority. Enroll the exact client GUID and enterprise application object
GUID pair, and enroll that object GUID for its own customer scope. The API
requires app-only type, certificate authentication (`azpacr=2`), role, exact
enrollment, tenant, audience, and a valid signature. Capture HTTP 403 for a second
principal with the role but without enrollment. Keep the private key in the
coordinator's credential store.

The app's system-assigned
identity needs Search index read, inference, and Table entity data access.
Search's system-assigned identity needs access to the embedding deployment.
Use the index template as the contract. Match model and dimensions to the
actual embedding deployment and index vectors. Seed rows using the same
embedding model as the query vectorizer. For each current policy row, author
the factual answer and normalized fact key/value together. Different values
for the same fact key in one customer scope are treated as a conflict.
The model selects a citation or abstains. The server returns the curated answer
associated with a validated citation. Relevance and condition handling depend
on the deployed retrieval and selector and need held-out evaluation.

Seed Table entities using uppercase hex SHA-256 of UTF-8 tenant GUID, slash,
customer scope. Use `order:ORD-1042` with string Status=in_transit and
`ticket:T-17` with Boolean CallbackRequested=false in customer A's partition.
Start with a fresh synthetic ticket for a recorded approval. Each approval ID
is actor-bound and expires after 15 minutes if unused. Ticket state and approval
outcome update atomically using ETags. Use a fresh evidence path per run.

Coordinator runs live API acceptance in PowerShell 7 with two short-lived
enrolled workload tokens provided through CAPSTONE_TEST_TOKEN_A and
CAPSTONE_TEST_TOKEN_B. Browser acceptance uses the enrolled delegated operator.
Use `./lab/hosted-acceptance.ps1 -BaseUrl $url -Output ./evidence/hosted-exercise.json`.
Then restart the same deployed app with the same table, capture the restart
receipt, and run the script with `-Stage Replay -Previous
./evidence/hosted-exercise.json -Output ./evidence/hosted-replay.json`.
Use `-Stage Evaluate` for the held-out and cross-scope questions. Run
`-Stage DependencyFailure` against the designated revision with a broken
knowledge configuration and working order storage. Each stage writes results
and correlation IDs even if the observed outcomes disagree with expectations.
Record redacted token-claim verification, index/version and service receipts,
deployment/image digest, correlation IDs, traces and candidate/rollback outcomes.
Keep cost and role changes in the coordinator's ledger.

The App Insights exporter uses OpenTelemetry and bounded operation names.
The lab registers the Azure Monitor trace exporter before assigning the
explicit sampler, so an unsampled ambient ASP.NET request can still produce
the capstone's route and dependency spans.
Search, Foundry, and Table operations emit dependency spans. Look up the
response `X-Correlation-Id` as `operation_Id` in Application Insights, then
verify request, dependency, and trace rows from the same call. Capture both a
successful and an unavailable dependency. Logs carry route, HTTP status,
correlation, and dependency exception type. Live ingestion remains a deployment
check.

V3 adds a signed-in receipt form and authenticated `POST /attachment`. Send
raw PNG or JPEG bytes up to 100,000 bytes, with the matching MIME type and the
canonical order ID in `X-Order-Id`. The server first looks up that order using
the customer scope mapped from the verified token. A missing order returns a
generic denial before contacting Document Intelligence. Set string
`ReceiptReference=048271` on the existing customer-A `order:ORD-1042` row.
Preserve the canonical order status and any other properties. The v3 app's
system-assigned identity needs Cognitive Services User scoped to the exact
Document Intelligence resource.

The app submits one page to `prebuilt-receipt` using API `2024-11-30`.
It reads the `ORDER 048271` line in the observed OCR output and compares it
to the canonical receipt reference. It returns a match and canonical status
only after both checks succeed. Mismatch or ambiguous extraction goes to
review without exposing identifiers. This establishes that the uploaded text
matches the scoped record. It is not receipt authenticity verification.
The app does not persist the image or emit it into telemetry. Use the included
fictional receipt for deployment acceptance.

Set the two short-lived workload token environment variables, then run
`pwsh -NoProfile -File lab/hosted-receipt-acceptance.ps1 -BaseUrl $revisionOrigin
-Output ./evidence/receipt-acceptance.json`. Use the direct HTTPS revision
origin. The script verifies a match, a cross-customer denial, and a changed
reference. It writes response bodies, request hashes, and correlation IDs.
Each analysis submits at most one page. Trace evidence establishes whether
each request reached Document Intelligence.

The legacy metadata endpoint `POST /attachment/preflight` remains available
for walkthrough compatibility. Capture a single delegated operator browser
session, two enrolled certificate workload identities across scopes, live
service acceptance, independent evaluation, and rollback before recording
the finale.
