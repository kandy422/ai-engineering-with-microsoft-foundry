from collections import Counter
import re

# Synthetic evaluation set: the model always predicts billing.
tickets = [("billing", "Refund pending")] * 8 + [
    ("access", "Unlock sample@example.test"), ("technical", "Export failed")]
pattern = r"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}"
def redact(text):
    return re.sub(pattern, "[EMAIL]", text)

counts = Counter(label for label, _ in tickets)
correct = Counter(label for label, _ in tickets if label == "billing")
print("coverage:", dict(counts))
print("overall:", sum(correct.values()), "/", len(tickets))
for label, count in counts.items():
    print(label, f"{correct[label]}/{count}")
print("redacted:", tickets[8][1])