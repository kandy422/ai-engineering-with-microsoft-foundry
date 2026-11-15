import sys
import json
from pathlib import Path

def neuron(outage, repeat, outage_weight):
    weighted = outage * outage_weight + repeat * 0.75
    z = weighted - 2
    score = max(0, z)
    return dict(weight=outage_weight, weighted=weighted, z=z, score=score)

weight = float(sys.argv[1])
print(json.dumps(neuron(1, 1, weight), indent=2))
