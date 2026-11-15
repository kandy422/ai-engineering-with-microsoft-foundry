from collections import defaultdict
from datetime import date
from hashlib import sha256
import csv
import io
import json
from pathlib import Path
import sys

base = Path(__file__).resolve().parent
source = base / "pipeline-input.csv"
required = ["ticket_id", "conversation_id", "received_at", "channel",
            "subject", "routed_queue"]
labels = {"billing", "access", "technical"}
validation_start, test_start = "2026-01-12", "2026-01-20"
raw = source.read_bytes()
reader = csv.DictReader(io.StringIO(raw.decode("utf-8"), newline=""))
if reader.fieldnames != required:
    raise ValueError("Unexpected columns")
rows = list(reader)
seen, groups = set(), defaultdict(set)
splits = {name: [] for name in ("train", "validation", "test")}
for r in rows:
    if None in r or any(not r[k] or not r[k].strip() for k in required):
        raise ValueError("Missing or extra field")
    if r["ticket_id"] in seen or r["routed_queue"] not in labels:
        raise ValueError("Duplicate ticket ID or invalid queue")
    seen.add(r["ticket_id"])
    d = date.fromisoformat(r["received_at"]).isoformat()
    split = "test" if d >= test_start else "validation" if d >= validation_start else "train"
    groups[r["conversation_id"]].add(split)
    splits[split].append(r)
if any(len(s) != 1 for s in groups.values()):
    raise ValueError("Conversation crosses time cutoffs")
if any(not rs for rs in splits.values()):
    raise ValueError("Each split must contain records")

vocab = sorted({r["channel"] for r in splits["train"]})
def transform(r):
    channel = r["channel"]
    return {"ticket_id": r["ticket_id"], "features": {
        "subject": r["subject"], "channel": channel if channel in vocab else "[UNK]"},
        "target": r["routed_queue"]}

out = Path(sys.argv[1]).resolve()
out.mkdir(parents=True, exist_ok=False)
def save(name, obj):
    (out/name).write_text(json.dumps(obj, indent=2, sort_keys=True)+"\n", encoding="utf-8")
def digest(path):
    return sha256(path.read_bytes()).hexdigest()
save("preprocessor.json", {"channelVocabulary": vocab, "unknown": "[UNK]"})
for name, rs in splits.items():
    save(name+".json", [transform(r) for r in rs])
manifest = {"schemaVersion": 1, "datasetVersion": "2.6.1",
    "source": source.name, "sourceSha256": sha256(raw).hexdigest(),
    "scriptSha256": digest(Path(__file__)),
    "cutoffs": {"validation": validation_start, "test": test_start},
    "splits": {name: {"ids": [r["ticket_id"] for r in rs],
        "sha256": digest(out/(name+".json"))} for name, rs in splits.items()},
    "preprocessorSha256": digest(out/"preprocessor.json")}
save("manifest.json", manifest)
print("dataset version:", manifest["datasetVersion"])
print("split counts:", {name: len(rs) for name, rs in splits.items()})
print("train vocabulary:", vocab)
print("validation:", splits["validation"][0]["channel"], "→", transform(splits["validation"][0])["features"]["channel"])
print("test:", splits["test"][0]["channel"], "→", transform(splits["test"][0])["features"]["channel"])