# Module 02 — Data and how models learn

AI Engineering with Microsoft Foundry

Install Python 3.12 and check it with `python --version`. On Windows, use `py -3.12` in place of `python` if needed. These labs use the standard library and run locally. Open the folder for the lesson you are watching in an editor, then open a terminal in that same folder.

Each folder contains the runnable baseline plus the exact source snapshots used in the videos. Run the baseline first. Make the edit in the working file named in the table and rerun the command. Use the `after-` snapshot to check your edit. To reset, copy the matching `before-` snapshot over the working file.

| Lesson folder | Run from that folder | Edit and checkpoint |
| --- | --- | --- |
| `2.1` | `python -X utf8 features.py` | In `features.py`, remove `route_confirmation` from the feature tuple. The baseline prints the leaking field and fails an assertion. The corrected run exits successfully with `subject` and `channel` as inputs and `routed_queue` as the separate target. Try another CSV row and verify the same two feature keys. |
| `2.2` | `python -X utf8 learning.py` | Change `query = (2, 1)` to `query = (0, 2)`. The prediction changes from billing to access. Cluster centres and simulated reward means stay fixed. Predict the nearest example for another query before running it. |
| `2.3` | `python -X utf8 clean.py` | Read `messy.csv` and `labeling-rule.md`, then remove `and False` from the M03 condition. M03 moves from billing to access. M01 still deduplicates and M02 and M05 still go to review. Apply the rule to a new ambiguous ticket before assigning a label. |
| `2.4` | `python -X utf8 fixed_split.py` | Replace `train = rows[:12] + siblings` with `train = rows[:16]`. The same eight test messages are used in both runs. The number of shared conversations falls from four to zero and the number correct drops from 8/8 to 4/8. Inspect the group IDs before trusting a split. |
| `2.5` | `python -X utf8 coverage.py` | Wrap `tickets[8][1]` in `redact(...)` in the final print call. The sample address becomes `[EMAIL]`. Accuracy stays 8/10 overall, with 0/1 **correct** in each minority queue. Try a different fictional address. |
| `2.6` | `python -X utf8 pipeline.py before-output` | The baseline rejects a conversation spanning cutoffs. Change ticket P06's conversation from C01 to C05 in `pipeline-input.csv`. Run `python -X utf8 pipeline.py after-output`, then again with `repeat-output`. Use unused directory names for each successful run. Expect split counts of 4/1/1, vocabulary `chat` and `email`, and `[UNK]` for held-out channels. Compare the five JSON files by SHA-256 and open the manifest to check the split IDs. |

The tickets are synthetic. The small experiments illustrate data and evaluation behavior. The toy scores are specific to these fixtures.
