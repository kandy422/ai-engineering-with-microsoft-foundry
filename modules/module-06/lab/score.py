import json
from pathlib import Path

def read(name):
    return json.loads(Path("lab/results", name).read_text(encoding="utf-8-sig"))

evidence = read("6.6.json")["evidence"]
scores = read("6.6-rubric.json")["scores"]
keys = {item["value"]["caseId"]:item["value"] for item in evidence if item["name"] == "version key"}
assert len(scores) == len(keys) and {row["caseId"] for row in scores} == set(keys)
tally = {"v1": 0, "v2": 0}
for row in scores:
    key = keys[row["caseId"]]
    for side in ("left", "right"):
        assert len(row[side]) == 3 and all(type(value) is bool for value in row[side])
        tally[key[side]] += all(row[side])
print(json.dumps({"passingCases": tally, "casesPerVersion": len(scores)}, indent=2))

