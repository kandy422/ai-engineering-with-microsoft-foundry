import sys
train = [(1, 2), (2, 4), (3, 6), (4, 8)]
batch = train[:2]
a, b = 0.5, 0.5
rate = float(sys.argv[1])
errors = [(a*x, a*b*x-y) for x,y in batch]
da = sum(error*b*x for (x,_),(_,error) in zip(batch,errors))/len(batch)
db = sum(error*h for h,error in errors)/len(batch)
print(f"before: a={a:.6f} b={b:.6f}")
print(f"gradient: da={da:.6f} db={db:.6f}")
a, b = a-rate*da, b-rate*db
print(f"after:  a={a:.6f} b={b:.6f}")
