# Lesson 10.8 managed retrieval lab

The stable REST API `2026-04-01` queries a public-only source and returns extractive grounding and references. The native document ACL exercise uses `2026-08-01-preview`, an ACL-enabled index, and minimal extractive retrieval. The same delegated user is tested before a grant, after the grant, and after revocation. Answer synthesis is a separate preview capability.

## Optional live setup

Use a dedicated Azure AI Search lab with semantic ranker and agentic retrieval available. The schema and knowledge-resource operator needs Search Service Contributor. The ingestion operator needs Search Index Data Contributor. The retrieve client needs Search Index Data Reader. Acquire Search tokens through Entra. Use fictional policies and keep token values out of recorded commands and saved evidence.

Generate data-plane request bodies locally. Supply the unrelated principal as `AllowedUserObjectId` and the signed-in test user as `DeniedUserObjectId`. The unrelated principal is an ACL fixture entry. The test only authenticates the signed-in user.

```powershell
./managed-iq/New-Fixture.ps1 -Prefix <prefix> -AllowedUserObjectId <unrelated-oid> -DeniedUserObjectId <signed-in-user-oid> -OutputDirectory <new-run-folder>
```

Apply the routes and bodies in each generated routes file in order. Check every document's ingestion status. Set `RAG_IQ_SEARCH_ENDPOINT` to the Search HTTPS origin, `RAG_IQ_SEARCH_TOKEN` to the Search bearer credential, and `RAG_IQ_TEST_QUESTION` to `What is the return window for an unopened accessory?`. Set `RAG_IQ_KNOWLEDGE_BASE` and `RAG_IQ_INDEX_SOURCE` to the generated names for the selected mode.

1. Set `RAG_IQ_MODE=stable`. Run `dotnet run --project lab --no-build -- --foundry-iq`. Look for `returns-unopened` and its 30-day passage.
2. Set `RAG_IQ_MODE=acl` and switch both resource names to their ACL equivalents. Set `RAG_IQ_USER_TOKEN` to the same trusted user's delegated Search access token for every stage. Set `RAG_IQ_ACL_STAGE=denied` and run the same command. The public control should be present and the internal document absent.
3. Merge one document in the ACL index via `POST /indexes/<prefix>-acl-index/docs/index?api-version=2026-08-01-preview`. Use `{"value":[{"@search.action":"merge","id":"returns-unopened-internal","allowedUsers":["<unrelated-oid>","<signed-in-user-oid>"]}]}`. Confirm the per-document merge succeeded. Set the stage to `granted` and rerun the same query.
4. Merge again with only the unrelated principal in `allowedUsers`. Confirm the merge succeeded. Set the stage to `revoked` and rerun. Verify the public control still appears and the restricted content and reference disappear.
5. Save each response and request ID. A missing public control or failed request does not establish denied access. The test uses one semantic intent, a 30-second service runtime limit, and a 5,001 output-token limit. The preview request uses `maxOutputSize`. The stable request uses `maxOutputSizeInTokens`.

## Capstone integration contract

For a public corpus, use the stable semantic-intent retrieve contract. For this native document ACL path, use the preview extractive contract and forward the verified caller's delegated Search token using `x-ms-query-source-authorization`. Authenticate the server separately via `Authorization: Bearer ...`. Caller identity comes from validated Entra authentication and token acquisition, never from caller-supplied IDs or arbitrary identity headers. Do not fall back to an unfiltered result on auth, permission, response-shape or service failure.

The success payload contains `response[]` messages with `content[]` entries of type `text`, `references[]` and retrieval `activity[]` when requested or supplied. Extractive content can be JSON encoded inside the text value. Match its reference IDs to returned source references and preserve access-scoped source IDs and content. Use the recorded stable service envelope packaged in `recordings/10.8.json`. In that envelope, inner `ref_id` is numeric, reference `id` is a string, and `docKey` is the stable document key. Normalize the ref ID for the join and preserve `docKey` for citations. The learner recording also includes the three preview reference sets. Missing usable evidence is a distinct outcome from failed retrieval. Keep the local fixture path explicitly labelled. The preview ACL exercise proves only the tested identity and corpus, with no GA claim for the preview feature.

## References

- [Stable and preview migration](https://learn.microsoft.com/en-us/azure/search/agentic-retrieval-how-to-migrate)
- [Knowledge base retrieve](https://learn.microsoft.com/en-us/azure/search/agentic-retrieval-how-to-retrieve)
- [Pushed ACL index schema](https://learn.microsoft.com/en-us/azure/search/search-index-access-control-lists-and-rbac-push-api)
- [Query-time permission enforcement](https://learn.microsoft.com/en-us/azure/search/search-query-access-control-rbac-enforcement)


