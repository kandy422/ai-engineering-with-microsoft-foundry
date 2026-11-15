import hashlib
import json
import shutil
import subprocess
from pathlib import Path

here = Path(__file__).resolve().parent
module = here.parent
reference_path = here / "held-out-v1.json"
raw = reference_path.read_bytes()
assert hashlib.sha256(raw).hexdigest() == reference_path.with_suffix(".sha256").read_text().strip()
cases = [
    dict(id=id, family=scope, split="holdout", slice=slice_name, query=query,
         expectedEvidence=[evidence] if evidence else [], allowedActions=[], expectedOutcome=outcome)
    for id, scope, slice_name, query, evidence, outcome in json.loads(raw)
]
assert 10 <= len(cases) <= 20 and len({c["id"] for c in cases}) == len(cases)
build = here / "work"
build.mkdir(exist_ok=True)
project = module / "lab/Evaluation.csproj"
subprocess.run(["dotnet", "build", str(project), "--nologo", "-o", str(build)], check=True)
(build / "cases-v1.json").write_text(json.dumps(cases, indent=2), encoding="utf-8")
shutil.copyfile(module / "lab/experiment-policies-v1.json", build / "experiment-policies-v1.json")
result = subprocess.run(["dotnet", "Evaluation.dll", "13.6"], cwd=build, check=True, capture_output=True, text=True)
(here / "local-run.txt").write_text(result.stdout, encoding="utf-8")
manifest = {"referenceSha256": hashlib.sha256(raw).hexdigest(), "datasets": {}}
for variant in ["keyword", "scoped-keyword"]:
    file = build / f"foundry-{variant}-v1.jsonl"
    rows = [json.loads(line) for line in file.read_text(encoding="utf-8").splitlines()]
    assert [row["caseId"] for row in rows] == [case["id"] for case in cases]
    for case, row in zip(cases, rows, strict=True):
        assert (row["query"], row["slice"], row["reference"], row["version"]) == (
            case["query"], case["slice"], case["expectedOutcome"], variant)
    manifest["datasets"][variant] = {
        "file": file.name,
        "sha256": hashlib.sha256(file.read_bytes()).hexdigest(),
        "rows": len(rows),
        "localMismatches": [case["id"] for case, row in zip(cases, rows, strict=True)
                            if row["response"] != case["expectedOutcome"] or row["evidence"] != case["expectedEvidence"]]
    }
(here / "prepared-inputs.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
print(result.stdout)
