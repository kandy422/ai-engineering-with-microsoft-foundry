import sys
import json, math
from pathlib import Path

tokens = ['reset','my','password']
query = (1,1)
keys = [(1,0),(0,0),(2,1)]
values = [(1,0),(0,1),(2,2)]
values[-1] = (int(sys.argv[1]), 2)
scores = [sum(q*k for q,k in zip(query,key))/math.sqrt(len(query)) for key in keys]
exp = [math.exp(score-max(scores)) for score in scores]
weights = [value/sum(exp) for value in exp]
mixed = [sum(w*v for w,v in zip(weights,dim)) for dim in zip(*values)]
print("last value:", values[-1])
print("weights:", [round(w,3) for w in weights])
print("mixture:", [round(v,3) for v in mixed])
