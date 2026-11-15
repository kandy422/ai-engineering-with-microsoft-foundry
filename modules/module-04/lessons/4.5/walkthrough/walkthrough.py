import sys
import json
from pathlib import Path

embedding = {'reset': (2,1,0), 'password': (0,2,1), 'account': (0,2,2)}
position = [(0,0,0),(1,0,-1)]
def encode(tokens):
    return [tuple(a+b for a,b in zip(embedding[token],position[i]))
            for i,token in enumerate(tokens)]
tokens = sys.argv[1:]
vectors = encode(tokens)
for index, (token, vector) in enumerate(zip(tokens, vectors)):
    print(f"slot {index+1}: {token} {list(vector)}")
