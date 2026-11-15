import json
from pathlib import Path

def descend(rate, steps=8):
    weight = 0.
    trace = []
    for _ in range(steps+1):
        error = weight-3
        trace.append([round(weight,4),round(error**2/2,4)])
        weight -= rate * error
    return dict(rate=rate, trace=trace, final=trace[-1][1], first=trace[1][0])

result = [descend(rate) for rate in (.05,.5,2.1)]
assert result[1]['final'] < result[0]['final'] < result[2]['final']
Path(__file__).with_name('result.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result))

