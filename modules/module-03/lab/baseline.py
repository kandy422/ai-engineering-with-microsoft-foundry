from collections import Counter
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parent
DATA = json.loads((ROOT / "data.json").read_text())
LABELS = ("routine", "urgent")
KEYWORDS = {"down", "outage", "unavailable"}

def rows(split):
    return [(text, label) for label in LABELS for text in DATA[split][label]]

def keyword_route(text):
    words = set(re.findall(r"[a-z]+", text.lower()))
    return "urgent" if words & KEYWORDS else "routine"

def report(predict, examples):
    records = [{"text": text, "actual": label, "predicted": predict(text)}
               for text, label in examples]
    correct = sum(r["actual"] == r["predicted"] for r in records)
    urgent = [r for r in records if r["actual"] == "urgent"]
    caught = sum(r["predicted"] == "urgent" for r in urgent)
    return {"correct": correct, "total": len(records), "urgentCaught": caught,
            "urgentTotal": len(urgent), "rows": records}

if __name__ == "__main__":
    counts = Counter(label for _, label in rows("train"))
    majority = counts.most_common(1)[0][0]
    held_out = rows("development")
    results = {"majority": report(lambda _: majority, held_out),
               "keywords": report(keyword_route, held_out)}
    print(json.dumps(results, indent=2))

