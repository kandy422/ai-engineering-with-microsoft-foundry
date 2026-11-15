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

result = []
for causal in (False,True):
    for future in ((0,2),(2,0)):
        rows = [(1,0),(.5,1),future]
        one,trace = block(rows,causal)
        two,_ = block(one,causal)
        result.append(dict(causal=causal, future=future, first=trace[1],
                           block1=[round(x,3) for x in one[1]], block2=[round(x,3) for x in two[1]]))
assert result[0]['block2'] != result[1]['block2']
assert result[2]['block2'] == result[3]['block2']
assert all(row[-1]==0 for row in result[2]['first']['weights'])
Path(__file__).with_name('result.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result))

