import json
from pathlib import Path

def neuron(outage, repeat, outage_weight):
    weighted = outage * outage_weight + repeat * 0.75
    z = weighted - 2
    score = max(0, z)
    return dict(weight=outage_weight, weighted=weighted, z=z, score=score)

if __name__ == "__main__":
    result = [neuron(1, 1, weight) for weight in (1, 3)]
    assert [row["score"] for row in result] == [0, 1.75]
    Path(__file__).with_name("result.json").write_text(json.dumps(result, indent=2))
    print(json.dumps(result, indent=2))


