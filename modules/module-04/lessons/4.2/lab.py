import json, math, random
from pathlib import Path

POINTS = [(-1,-1,0), (-1,1,1), (1,-1,1), (1,1,0)]

def fit(width):
    rng = random.Random(42)
    hidden = [[rng.uniform(-1,1) for _ in range(3)] for _ in range(width)]
    output = [rng.uniform(-1,1) for _ in range(width or 2)] + [0.]
    def forward(x,y):
        features = [math.tanh(a*x+b*y+c) for a,b,c in hidden] if width else [x,y]
        score = sum(w*h for w,h in zip(output,features)) + output[-1]
        return features, 1/(1+math.exp(-score))
    for _ in range(1600):
        for x,y,label in POINTS:
            features, p = forward(x,y)
            error = p-label
            for weights, h, w in zip(hidden,features,output):
                delta = error*w*(1-h*h)
                for j,value in enumerate((x,y,1)):
                    weights[j] -= .12*delta*value
            for j,h in enumerate(features+[1]):
                output[j] -= .12*error*h
    grid = [[int(forward((x-10)*.13,(10-y)*.13)[1]>=.5)
             for x in range(21)] for y in range(21)]
    return dict(correct=sum((forward(x,y)[1]>=.5)==label for x,y,label in POINTS),
                grid=grid, training=POINTS)

result = [fit(0),fit(4)]
assert result[0]['correct'] < 4 and result[1]['correct'] == 4
Path(__file__).with_name('result.json').write_text(json.dumps(result,indent=2))
print([row['correct'] for row in result])

