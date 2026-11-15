import json, math
from pathlib import Path

tokens = ['reset','my','password']
query = (1,1)
keys = [(1,0),(0,0),(2,1)]
values = [(1,0),(0,1),(2,2)]
scores = [sum(q*k for q,k in zip(query,key))/math.sqrt(len(query)) for key in keys]
exp = [math.exp(score-max(scores)) for score in scores]
weights = [value/sum(exp) for value in exp]
mixed = [sum(w*v for w,v in zip(weights,dim)) for dim in zip(*values)]
assert math.isclose(sum(weights),1) and weights[2] > weights[0] > weights[1]
round3 = lambda values: [round(value,3) for value in values]
result = [dict(tokens=tokens, query=query, keys=keys, values=values,
               scores=round3(scores), weights=round3(weights), output=round3(mixed))]
Path(__file__).with_name('result.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result))

