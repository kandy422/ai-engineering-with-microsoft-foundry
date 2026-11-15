import json
from pathlib import Path

embedding = {'reset': (2,1,0), 'password': (0,2,1), 'account': (0,2,2)}
position = [(0,0,0),(1,0,-1)]
def encode(tokens):
    return [tuple(a+b for a,b in zip(embedding[token],position[i]))
            for i,token in enumerate(tokens)]
result = []
for tokens in [('reset','password'),('password','reset')]:
    combined = encode(tokens)
    result.append(dict(tokens=tokens, combined=combined, reset=list(combined[tokens.index('reset')]),
                       projected=[list(v[:2]) for v in combined]))
assert result[0]['reset'] != result[1]['reset']
Path(__file__).with_name('result.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result))

