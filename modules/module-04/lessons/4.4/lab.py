import json
from pathlib import Path

train = [(1,2),(2,4),(3,6),(4,8)]
validation = [(1.5,3),(3.5,7)]
a,b = .5,.5
def loss(rows):
    return sum((a*b*x-y)**2/2 for x,y in rows)/len(rows)

first = {}
curves = []
for epoch in range(1,13):
    for start in (0,2):
        batch = train[start:start+2]
        errors = [(a*x,b*a*x,b*a*x-y) for x,y in batch]
        da = sum(error*b*x for (x,_),(_,_,error) in zip(batch,errors))/len(batch)
        db = sum(error*h for h,_,error in errors)/len(batch)
        if epoch == 1 and start == 0:
            first.update(h=errors[0][0],prediction=errors[0][1],da=da,db=db)
        a,b = a-.03*da,b-.03*db
        if epoch == 1 and start == 0:
            first['after'] = round(a,6)
    curves.append([epoch,round(loss(train),6),round(loss(validation),6)])
assert curves[-1][1] < curves[0][1] and curves[-1][2] < curves[0][2]
result = [first,dict(curves=curves,train=curves[-1][1],validation=curves[-1][2])]
Path(__file__).with_name('result.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result))

