from csv import DictReader
from pathlib import Path

rows = list(DictReader(Path(__file__).with_name("tickets.csv").open(encoding="utf-8")))
r = rows[0]
features = {k: r[k] for k in ("subject", "channel")}
label = r["routed_queue"]
print("raw:", r["ticket_id"], r["subject"], r["route_confirmation"])
print("input:")
for field, value in features.items():
    print(f"{field}: {value}")
print(f"routed_queue: {label}")
assert all(r["route_confirmation"] == r["routed_queue"] + " desk" for r in rows)
assert not {"route_confirmation", "routed_queue"} & features.keys()