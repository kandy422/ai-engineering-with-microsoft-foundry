# C8 through C11 are the four fixed test conversations.
from split_data import rows, siblings, evaluate

test = rows[16:]
train = rows[:16]
evaluate(train, test)
