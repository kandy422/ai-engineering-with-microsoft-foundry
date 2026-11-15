import argparse
import csv
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("batch")
args = parser.parse_args()
if not args.batch.isascii() or not 1 <= len(args.batch) <= 32 or any(
    c not in "abcdefghijklmnopqrstuvwxyz0123456789-" for c in args.batch
):
    parser.error("Use the original lowercase batch ID.")
here = Path(__file__).resolve().parent
folder = here / "receipts" / args.batch

def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))

variants = ["keyword", "scoped-keyword"]
cases = {row[0]: row for row in read(here / "held-out-v1.json")}
records = {}
error_count = 0
judge_error_count = 0
for variant in variants:
    run = read(folder / f"{variant}-run.json")
    if run["status"] != "completed":
        raise ValueError(f"{variant} status is {run['status']}")
    rows = read(folder / f"{variant}-items.json")
    items = {row["datasource_item"]["caseId"]: row for row in rows}
    if len(rows) != len(cases) or items.keys() != cases.keys():
        raise ValueError(f"{variant} returned missing or duplicate case IDs")
    input_rows = (here / "work" / f"foundry-{variant}-v1.jsonl").read_text().splitlines()
    for line in input_rows:
        local = json.loads(line)
        case_id = local["caseId"]
        record = items[case_id]
        for key in ("query", "response", "reference", "context", "slice", "version"):
            if record["datasource_item"][key] != local[key]:
                raise ValueError(f"{variant} {case_id} differs from the submitted {key}")
        _, _, slice_name, _, expected_evidence, reference = cases[case_id]
        expected_ids = [expected_evidence] if expected_evidence else []
        result = dict(caseId=case_id, variant=variant, slice=slice_name, runId=run["id"],
                      itemId=record["id"], serviceStatus=record["status"], candidate=local["response"],
                      reference=reference, expectedEvidence=expected_ids, selectedEvidence=local["evidence"],
                      exactOutcome=local["response"] == reference, exactEvidence=local["evidence"] == expected_ids,
                      evaluatorResults=record["results"])
        if record["run_id"] != run["id"] or record["eval_id"] != run["eval_id"]:
            raise ValueError(f"{variant} {case_id} belongs to a different evaluation run")
        grades = {grade["name"]: grade for grade in record["results"]}
        if (record["status"] not in ("completed", "pass", "fail", "passed", "failed")
                or (record.get("sample") or {}).get("error")
                or grades.keys() != {"exact-outcome", "policy-outcome-judge"}
                or grades["exact-outcome"].get("passed") != result["exactOutcome"]
                ):
            error_count += 1
        else:
            judge = grades["policy-outcome-judge"]
            if judge.get("status") == "error" and (judge.get("sample") or {}).get("error"):
                judge_error_count += 1
            elif judge.get("score") not in (0, 1):
                error_count += 1
        records.setdefault(case_id, []).append(result)

with (folder / "comparison.csv").open("w", encoding="utf-8", newline="") as stream:
    rows = [entry for pair in records.values() for entry in pair]
    writer = csv.DictWriter(stream, fieldnames=rows[0].keys())
    writer.writeheader()
    for entry in rows:
        writer.writerow({k: json.dumps(v) if isinstance(v, (list, dict)) else v for k, v in entry.items()})

trace = {
    "source": "Foundry evaluation output-item receipts joined to precomputed local retrieval rows",
    "traceType": "dataset evaluation evidence chain",
    "cases": {case_id: records[case_id] for case_id in ("hold-05", "hold-10")},
    "serviceErrorsOrMissingCriteria": error_count,
    "recordedJudgeErrors": judge_error_count,
}
(folder / "item-trace.json").write_text(json.dumps(trace, indent=2), encoding="utf-8")
print(f"Paired {len(records)} cases. Items requiring service error review: {error_count}.")
if error_count:
    raise SystemExit(2)


