"""Tiny, dependency-free training and evaluation examples for lessons 1.2–1.4."""
from collections import Counter, defaultdict
import json
import random
import re
from pathlib import Path

root = Path(__file__).parent
tickets = json.loads((root / "tickets.json").read_text())
training = {
    "billing": ["charged twice today", "need refund please", "duplicate charge", "refund payment"],
    "account": ["cannot sign in today", "reset my password", "sign in failed", "cannot access account"],
    "shipping": ["package arrived late", "track my package", "order never arrived", "delivery missing"],
}
draft_corpus = [
    "I can check the charge.",
    "I can check your order.",
    "I can check the delivery.",
    "Please share your order number.",
    "Please confirm your email address.",
]

def words(text):
    return re.findall(r"[a-z]+", text.lower())

def rule(text):
    return "billing" if "refund" in words(text) else "review"

def train_classifier():
    return {label: Counter(w for example in rows for w in words(example))
            for label, rows in training.items()}

def classify(text, learned):
    scores = {label: sum(weights[w] for w in set(words(text)))
              for label, weights in learned.items()}
    ranked = sorted(scores, key=scores.get, reverse=True)
    first, second = ranked[:2]
    return first, scores, first if scores[first] - scores[second] >= 2 else "review"

def train_generator():
    transitions = defaultdict(list)
    for line in draft_corpus:
        tokens = ["<start>", *line.split(), "<end>"]
        for previous, following in zip(tokens, tokens[1:]):
            transitions[previous].append(following)
    return transitions

def draft(prefix, model):
    # Sample observed next words; no order lookup or factual guarantee.
    rng = random.Random(11)
    tokens = prefix.split()
    for _ in range(15):
        token = rng.choice(model.get(tokens[-1], ["<end>"]))
        if token == "<end>":
            break
        tokens.append(token)
    return " ".join(tokens)

def main():
    learned, generator = train_classifier(), train_generator()
    results = []
    for row in tickets:
        top, scores, guarded = classify(row["text"], learned)
        results.append({**row, "rule": rule(row["text"]), "classifier": top,
                        "scores": scores, "withReview": guarded,
                        "draft": draft("I can" if top == "billing" else "Please", generator)})
    total = len(results)
    evaluation = {method: sum(r[method] == r["expected"] for r in results)
                  for method in ("rule", "classifier", "withReview")}
    print(json.dumps({"results": results, "agreement": evaluation, "total": total,
                      "transitions": generator["check"]}, indent=2))

if __name__ == "__main__":
    main()

