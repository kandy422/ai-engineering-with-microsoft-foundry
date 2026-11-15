from collections import Counter
from math import dist

# Each ticket vector is (billing words, access words).
train = [((3, 0), "billing"), ((2, 0), "billing"),
         ((0, 3), "access"), ((0, 2), "access")]
query = (0, 2)
nearest = min(train, key=lambda pair: dist(pair[0], query))
print("nearest:", nearest, "prediction:", nearest[1])

points = [point for point, _ in train]
centers = [(3, 0), (0, 3)]
for _ in range(2):
    groups = [[p for p in points if
               min(range(2), key=lambda j: dist(p, centers[j])) == i]
              for i in range(2)]
    centers = [tuple(sum(axis)/len(g) for axis in zip(*g)) for g in groups]
print("clusters:", groups, "centers:", centers)

# Explore each action once, then exploit the observed mean.
# The simulator returns a reward for the chosen action only.
reward_table = {"ask": 1, "escalate": -1}
totals, counts = Counter(), Counter()
for action in ("ask", "escalate"):
    reward = reward_table[action]
    totals[action] += reward
    counts[action] += 1
action = max(counts, key=lambda a: totals[a]/counts[a])
print("reward means:", {a: totals[a]/counts[a] for a in counts}, "next:", action)