from pathlib import Path
import argparse
import hashlib
import json
import math
import re

HERE = Path(__file__).resolve().parent
CONTRACT = json.loads((HERE / "contract.json").read_bytes())
DATA = HERE.parent / "hosted-evidence" / "m09-embed-02"


def recorded_vectors():
    provenance = json.loads((DATA / "provenance.json").read_bytes())
    payloads = {}
    for filename in ("request.json", "response.json", "receipt.json"):
        raw = (DATA / filename).read_bytes()
        if hashlib.sha256(raw).hexdigest() != provenance["sha256"][filename]:
            raise ValueError(f"Recording digest mismatch: {filename}")
        payloads[filename] = json.loads(raw)
    request = payloads["request.json"]
    response = payloads["response.json"]
    if request["model"] != CONTRACT["deployment"] or response["model"] != CONTRACT["model"]:
        raise ValueError("Wrong embedding deployment or model")
    indexed = {}
    for row in response["data"]:
        index, vector = row["index"], row["embedding"]
        if index in indexed or len(vector) != CONTRACT["dimensions"] or not all(map(math.isfinite, vector)) or not any(vector):
            raise ValueError(f"Invalid vector at index {index}")
        indexed[index] = vector
    if set(indexed) != set(range(len(request["input"]))) or len(set(request["input"])) != len(indexed):
        raise ValueError("Response indexes and request inputs do not map one to one")
    return request["input"], indexed, provenance


def scoped_filter(tenant, customer, product):
    values = (tenant, customer, product)
    if any(not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,62}", value) for value in values):
        raise ValueError("Invalid scope identifier")
    return " and ".join(f"{key} eq '{value}'" for key, value in zip(CONTRACT["trustedFields"], values)) + " and isCurrent eq true"


def prepare(name):
    if not re.fullmatch(r"course09-[a-z0-9]+(?:-[a-z0-9]+)*", name) or len(name) > 128:
        raise ValueError("Use a dedicated course09- index name")
    inputs, vectors, provenance = recorded_vectors()
    fields = [{"name": "id", "type": "Edm.String", "key": True, "filterable": True}]
    fields += [{"name": field, "type": "Edm.String", "filterable": True}
               for field in ("sourceId", "policyVersion", *CONTRACT["trustedFields"])]
    fields += [{"name": "isCurrent", "type": "Edm.Boolean", "filterable": True},
               {"name": "content", "type": "Edm.String", "searchable": True},
               {"name": CONTRACT["vectorField"], "type": "Collection(Edm.Single)", "dimensions": CONTRACT["dimensions"],
                "searchable": True, "retrievable": False, "vectorSearchProfile": "policy-cosine"}]
    schema = {"name": name, "fields": fields, "vectorSearch": {
        "algorithms": [{"name": "cosine-hnsw", "kind": "hnsw", "hnswParameters": {"metric": CONTRACT["metric"]}}],
        "profiles": [{"name": "policy-cosine", "algorithm": "cosine-hnsw"}]}}
    # The shadow rows reuse exact text and vectors, isolating each filter predicate.
    corpus = [
        ("return-v1", "return", "v1", "tenant-a", "customer-a", "laptop", False, 0),
        ("return-v2", "return", "v2", "tenant-a", "customer-a", "laptop", True, 7),
        ("recall-v1", "recall", "v1", "tenant-a", "customer-a", "laptop", True, 1),
        ("recall-other-tenant", "recall", "v1", "tenant-b", "customer-a", "laptop", True, 1),
        ("recall-other-customer", "recall", "v1", "tenant-a", "customer-b", "laptop", True, 1),
        ("tablet-v1", "tablet-return", "v1", "tenant-a", "customer-a", "tablet", True, 2),
    ]
    metadata = ("id", "sourceId", "policyVersion", *CONTRACT["trustedFields"], "isCurrent")
    documents = [dict(zip(metadata, row[:-1]), content=inputs[row[-1]], contentVector=vectors[row[-1]],
                      **{"@search.action": "upload"}) for row in corpus]
    query_vector = vectors[11]
    request = {"top": 5, "select": CONTRACT["select"], "vectorFilterMode": CONTRACT["filterMode"],
               "filter": scoped_filter("tenant-a", "customer-a", "laptop"),
               "vectorQueries": [{"kind": "vector", "vector": query_vector, "fields": CONTRACT["vectorField"],
                                  "k": 5, "exhaustive": True}]}
    return {"apiVersion": CONTRACT["apiVersion"], "embeddingRequestId": provenance["requestId"],
            "queryText": inputs[11], "schema": schema, "upload": {"value": documents}, "query": request,
            "negative": dict(request, filter=scoped_filter("tenant-a", "customer-missing", "laptop")),
            "expectedKeys": ["recall-v1", "return-v2"]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--index", required=True)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    with args.out.open("x", encoding="utf-8") as stream:
        json.dump(prepare(args.index), stream, indent=2, allow_nan=False)
        stream.write("\n")
