# Lesson 9.4 · Build a vector index

Use PowerShell 7, Azure CLI and Python 3.12. Sign in with `az login`, select your lab subscription, and use a dedicated Azure AI Search endpoint with Entra roles enabled. The setup operator needs Search Service Contributor to create/delete the lab index and Search Index Data Contributor to load and query it. An application that only queries needs Search Index Data Reader scoped to its index. Confirm the endpoint and unique `course09-` index name with the lab owner.

From the module directory:

```powershell
$endpoint = 'https://<your-approved-service>.search.windows.net/'
$index = 'course09-your-unique-lab'
$work = Join-Path $PWD $index
New-Item -ItemType Directory -Path $work
$payload = Join-Path $work 'requests.json'
py -3.12 lab/search-live/prepare.py --index $index --out $payload
$lab = @{Endpoint=$endpoint; Payload=$payload; EvidenceDirectory=(Join-Path $work 'receipts')}
./lab/search-live/Invoke-VectorLab.ps1 @lab -Action Create
./lab/search-live/Invoke-VectorLab.ps1 @lab -Action Load
./lab/search-live/Invoke-VectorLab.ps1 @lab -Action Query
./lab/search-live/Invoke-VectorLab.ps1 @lab -Action Negative
```

1. Open `requests.json`. Under `schema.fields`, inspect `contentVector`, dimension 1,536, and the filterable metadata. The payload preparation verifies the saved Foundry request, response and receipt hashes. It reuses approved `course-embed` vectors for the exact recorded text.
2. Inspect the created index response and all six per-document statuses. An HTTP error or a failed document status stops the checkpoint. If the index already exists, choose a new lab index name. Run only against a lab instance assigned to you.
3. Inspect `query.filter` and the actual Search response. The expected matching set is `recall-v1` and `return-v2`. The old policy, tablet policy, other tenant and other customer are excluded. The check allows either ordering because the teaching checkpoint concerns membership.
4. Compare `negative.filter`. Only the customer value changes to `customer-missing`. An HTTP 200 with zero eligible documents means the application should abstain. A failed HTTP operation is a different outcome. Both the positive and negative actions write the actual response and request identifiers.
5. Keep the receipts for review, then run `./lab/search-live/Invoke-VectorLab.ps1 @lab -Action Cleanup` on the index you created. The cleanup verifies a subsequent GET returns 404. Service deletion and role management are owned by the lab administrator.

For Module 16, see [the field and query contract](contract.json). The required equality predicates map to Module 16's existing `tenantId`, `customerScope` and `isActive` as `tenantId`, `customerId` and `isCurrent` in this lab. Module 16 also needs a separate `product` predicate tied to trusted entitlements. Its capstone answer/fact fields are outside the Module 9 index. These fixtures exercise metadata filtering. Authenticate callers and resolve their entitlements in the application before issuing the query.

Microsoft references: [Create a vector index](https://learn.microsoft.com/en-us/azure/search/vector-search-how-to-create-index), [Vector query filters](https://learn.microsoft.com/en-us/azure/search/vector-search-filters), [Search roles](https://learn.microsoft.com/en-us/azure/search/search-security-rbac).
