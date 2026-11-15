# Module 04 · Neural networks and transformers

AI Engineering with Microsoft Foundry

Run small Python experiments that trace a neuron, train a network, and test what attention can see. Each lesson starts with a baseline command and ends with a controlled change you can reproduce.

## Setup

Use CPython 3.12.10 to reproduce the captured results. On Windows, install the 64-bit installer from the [Python 3.12.10 release page](https://www.python.org/downloads/release/python-31210/) with the Python launcher enabled. Open a fresh PowerShell session in the extracted package. The labs use only the Python standard library and synthetic inputs defined in the scripts. They run locally without a Foundry account.

```powershell
Set-Location modules/module-04
py -3.12 --version
py -3.12 lessons/4.1/walkthrough/walkthrough.py 1
```

The version should read `Python 3.12.10`. If you're already in `module-04`, start at the version check. If your system uses `python3.12` instead of the Windows launcher, replace `py -3.12` with `python3.12` and check the version first. The packaged commands were tested on Windows.

## Walkthrough checkpoints

In each lesson folder, open `walkthrough/walkthrough.py` in a text editor. Run the starter command first and predict what the changed argument will do. Then run the completed checkpoint. Both commands use the same script so you can isolate the effect of the change. The corresponding `walkthrough/capture.json` contains the full expected console output. Lesson 4.3 also has a third run.

Run these commands from the `module-04` directory.

| Lesson | Starter command | Completed checkpoint | Expected result |
|---|---|---|---|
| 4.1 Neuron | `py -3.12 lessons/4.1/walkthrough/walkthrough.py 1` | `py -3.12 lessons/4.1/walkthrough/walkthrough.py 3` | With both flags at 1, the score goes from 0 to 1.75. The bias and repeat-contact weight are fixed. |
| 4.2 Hidden layers | `py -3.12 lessons/4.2/walkthrough/walkthrough.py 0` | `py -3.12 lessons/4.2/walkthrough/walkthrough.py 4` | The direct model gets 2/4 and the model with four hidden units gets 4/4 on the same training points. Compare the printed predictions to find the two misses. |
| 4.3 Gradient descent | `py -3.12 lessons/4.3/walkthrough/walkthrough.py 0.05` | `py -3.12 lessons/4.3/walkthrough/walkthrough.py 0.5` | Loss after eight updates falls from 1.9806 to 0.0001 across the two runs. Run `py -3.12 lessons/4.3/walkthrough/walkthrough.py 2.1` to see the overshoot and final loss of 20.6774. |
| 4.4 Training loop | `py -3.12 lessons/4.4/walkthrough/walkthrough.py 0` | `py -3.12 lessons/4.4/walkthrough/walkthrough.py 0.03` | Both gradients are -2.1875 in each run. Both weights stay at 0.500000 at rate zero and reach 0.565625 at rate 0.03. |
| 4.5 Embeddings | `py -3.12 lessons/4.5/walkthrough/walkthrough.py reset password` | `py -3.12 lessons/4.5/walkthrough/walkthrough.py password reset` | The combined vector for `reset` changes from `[2, 1, 0]` in slot 1 to `[3, 1, -1]` in slot 2. |
| 4.6 Attention | `py -3.12 lessons/4.6/walkthrough/walkthrough.py 2` | `py -3.12 lessons/4.6/walkthrough/walkthrough.py 0` | The weights are `[0.178, 0.088, 0.734]` in both runs. Changing the first coordinate of the last value from 2 to 0 changes the mixture from `[1.646, 1.555]` to `[0.178, 1.555]`. |
| 4.7 Causal masking | `py -3.12 lessons/4.7/walkthrough/walkthrough.py 0` | `py -3.12 lessons/4.7/walkthrough/walkthrough.py 1` | Without masking, the two future inputs produce different earlier outputs. With masking, both produce `[3.57, 3.642]`, and both future weights are zero. |

## Run the full lab scripts

These scripts generate the data behind the plots and diagrams. Each writes a new `result.json` next to itself, so run from a writable copy. The packaged `expected-result.json` in each lesson folder is the reference output. The 4.4 result includes the training and held-out loss at each of 12 epochs. The 4.2 result includes both decision grids, and the 4.7 result includes traces from the first block.

```powershell
1..7 | ForEach-Object {
    py -3.12 "lessons/4.$_/lab.py"
    if ($LASTEXITCODE -ne 0) { throw "Lab 4.$_ failed" }
}
```

If the launcher cannot find Python 3.12, check installation with `py -0p`. If a script path cannot be found, check that `Get-Location` ends in `module-04` and that the `lessons` folder is beside this README. If writing `result.json` fails, extract the package to a writable folder before running.

## Independent exercise · Predict an attention output

Use lesson 4.6 to predict the mixture when the first coordinate of the last value is 1. Keep the query, keys and other value coordinates fixed. Explain why changing that value cannot affect the softmax weights, then check your prediction.

```powershell
py -3.12 lessons/4.6/walkthrough/walkthrough.py 1
```

Acceptance: stdout shows `last value: (1, 2)`, `weights: [0.178, 0.088, 0.734]`, and `mixture: [0.912, 1.555]`. Your explanation should connect the unchanged weights to the fixed query and keys, and the changed mixture to the value used in the weighted sum.

The support-ticket examples build on earlier course modules. All inputs are included in Module 04. There are no imports, saved-model dependencies, or interface changes across modules.
