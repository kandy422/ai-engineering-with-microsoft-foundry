import sys
import json
from pathlib import Path

def descend(rate, steps=8):
    weight = 0.
    trace = []
    for step in range(steps+1):
        error = weight-3
        trace.append([round(weight,4),round(error**2/2,4)])
        if step < steps: weight -= rate * error
    return dict(rate=rate, trace=trace, final=trace[-1][1], first=trace[1][0])

rate = float(sys.argv[1])
run = descend(rate)
print(f"rate={rate}  initial weight=0  target=3")
print(f"step 1 weight={run['first']}")
print(f"step 8 loss={run['final']}")
