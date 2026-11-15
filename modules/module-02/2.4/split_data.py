# Two messages for each of twelve synthetic conversations.
rows = [(f"C{i}", j, "access" if i % 2 else "billing")
        for i in range(12) for j in range(2)]

siblings = [(f"C{i}", 2, "access" if i % 2 else "billing")
            for i in range(8, 12)]

def evaluate(train, test):
    lookup = {group: label for group, _, label in train}
    shared = lookup.keys() & {group for group, _, _ in test}
    correct = sum(lookup.get(g, "billing") == y for g, _, y in test)
    print(f"Train: {len(train)} rows; test: {len(test)} rows")
    print(f"Shared conversations: {len(shared)}")
    print(f"Correct: {correct}/{len(test)}")
