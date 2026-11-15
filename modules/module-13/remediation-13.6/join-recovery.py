from collections import Counter
from decimal import Decimal
import hashlib
import json
from pathlib import Path

base = Path(__file__).resolve().parent
original = base / "receipts/course-20261115"
recovery = base / "receipts/course-20261115-recovery"

def read(folder, file):
    return json.loads((folder / (file + ".json")).read_text(encoding="utf-8-sig"))

def write(file, data):
    (recovery / (file + ".json")).write_text(json.dumps(data, indent=2), encoding="utf-8")

def key(row):
    return (row["version"], row["caseId"])

def grader(item):
    return next(row for row in item["results"] if row["name"] == "policy-outcome-judge")

run = read(recovery, "run")
assert run["status"] == "completed"
replacement = read(recovery, "items")
manifest = {(r["variant"], r["caseId"]): r for r in read(recovery, "subset-manifest")}
payload = {key(r["item"]): r["item"] for r in read(recovery, "run-request")["data_source"]["source"]["content"]}
results = {key(r["datasource_item"]): r for r in replacement}
assert len(replacement) == len(results) == 7 and results.keys() == manifest.keys() == payload.keys()
original_definition = read(original, "evaluation-request")
assert read(recovery, "evaluation-request")["testing_criteria"] == [
    g for g in original_definition["testing_criteria"] if g["name"] == "policy-outcome-judge"]
linked = {}
for variant in ("keyword", "scoped-keyword"):
    data = (base / "work" / f"foundry-{variant}-v1.jsonl").read_bytes()
    dataset_hash = hashlib.sha256(data).hexdigest()
    raw_rows = {key(json.loads(line)): line for line in data.splitlines()}
    for prior in read(original, f"{variant}-items"):
        row = prior["datasource_item"]
        identity = key(row)
        original_grade = grader(prior)
        record = {"input": row, "originalRunId": prior["run_id"], "originalItemId": prior["id"],
                  "originalExact": next(g for g in prior["results"] if g["name"] == "exact-outcome"),
                  "originalJudge": original_grade, "effectiveJudge": original_grade}
        if identity in results:
            item = results[identity]
            entry = manifest[identity]
            raw = raw_rows[identity]
            assert entry["sourceDatasetSha256"] == dataset_hash
            assert hashlib.sha256(raw).hexdigest() == entry["rowSha256"]
            assert entry["originalRunId"] == prior["run_id"] and entry["originalItemId"] == prior["id"]
            assert entry["originalJudge"] == original_grade
            assert original_grade["sample"]["error"]["code"] == "429"
            assert item["run_id"] == run["id"] and item["eval_id"] == run["eval_id"]
            assert payload[identity] == json.loads(raw)
            assert all(item["datasource_item"][k] == row[k] == payload[identity][k]
                       for k in ("caseId", "query", "slice", "version", "response", "reference", "context"))
            assert len(item["results"]) == 1
            recovered_grade = grader(item)
            record.update(recoveryRunId=item["run_id"], recoveryItemId=item["id"],
                          recoveryJudge=recovered_grade, effectiveJudge=recovered_grade)
        else:
            assert original_grade["status"] == "completed"
        linked[identity] = record
assert len(linked) == 24

summary = {}
for variant in ("keyword", "scoped-keyword"):
    rows = [r for (name, _), r in linked.items() if name == variant]
    counts = Counter("pass" if g["passed"] is True else "fail" if g["passed"] is False else "error"
                     for g in (r["effectiveJudge"] for r in rows))
    summary[variant] = {"exactPass": sum(r["originalExact"]["passed"] is True for r in rows),
                        "judgePass": counts["pass"], "judgeFail": counts["fail"],
                        "judgeError": counts["error"], "judgeCompleted": counts["pass"] + counts["fail"]}
write("joined-comparison", {"recoveryRunId": run["id"], "summary": summary, "rows": list(linked.values())})
usage = [u for u in run["per_model_usage"] if u["model_name"] == "gpt-5-mini-2025-08-07"]
prompt = sum(u["prompt_tokens"] for u in usage)
completion = sum(u["completion_tokens"] for u in usage)
cost = (Decimal(prompt) * Decimal(".25") + Decimal(completion) * 2) / 1000000
write("cost-and-cleanup", {"promptTokens": prompt, "completionTokens": completion,
    "recordedModelInvocations":sum(u["invocation_count"] for u in usage), "estimatedUsd":str(cost),
    "meterBasis":"Global Standard public retail model tokens, excluding duplicate evaluation attribution.",
    "cleanup":{"evaluation":run["eval_id"],"run":run["id"],"datasetUploads":0,
               "disposition":"Coordinator cleanup after evidence retention."}})
print(json.dumps(summary, indent=2))
