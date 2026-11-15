import csv
from pathlib import Path

base = Path(__file__).resolve().parent
with (base / "messy.csv").open(newline="", encoding="utf-8") as f:
    rows = list(csv.DictReader(f))
seen = {}
clean = []
for row in rows:
    id, subject, label = (row[k].strip() for k in ("ticket_id", "subject", "label"))
    if id in seen:
        assert seen[id] == (subject, label), f"Conflicting rows for {id}"
        print(id, "duplicate")
        continue
    seen[id] = subject, label
    if not subject or subject.casefold() == "please help":
        print(id, "review")
        continue
    label = {"tech": "technical"}.get(label.casefold(), label.casefold())
    # Recorded human adjudication under labeling-rule.md, not a general classifier.
    if id == "M03" and False:
        label = "access"
    assert label in {"billing", "access", "technical"}
    clean.append((id, subject, label))
print("accepted:", [(id, label) for id, _, label in clean])