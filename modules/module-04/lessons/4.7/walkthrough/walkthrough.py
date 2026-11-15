import sys
import json, math
from pathlib import Path

def head(rows,i,axis,causal):
    scores = [row[axis]*rows[i][axis] if not causal or j<=i else -math.inf
              for j,row in enumerate(rows)]
    exp = [math.exp(s-max(scores)) for s in scores]
    weights = [e/sum(exp) for e in exp]
    return sum(w*row[axis] for w,row in zip(weights,rows)),weights

def block(rows,causal):
    out, trace = [], []
    for i,row in enumerate(rows):
        heads = [head(rows,i,axis,causal) for axis in (0,1)]
        attention = [value for value,_ in heads]
        residual = [x+h for x,h in zip(row,attention)]
        ff = [max(0,residual[0]-residual[1])/4,max(0,residual[1]-residual[0])/4]
        out.append([x+h for x,h in zip(residual,ff)])
        trace.append(dict(attention=attention, weights=[w for _,w in heads], residual=residual, ff=ff))
    return out,trace

causal = bool(int(sys.argv[1]))
print(f"causal={causal}  inspecting position 2 (my)")
for future in ((0,2),(2,0)):
    rows = [(1,0),(.5,1),future]
    one, trace = block(rows,causal)
    two, _ = block(one,causal)
    print("future", future, "earlier output", [round(x,3) for x in two[1]])
    print("future weights", [round(w[-1],3) for w in trace[1]['weights']])
