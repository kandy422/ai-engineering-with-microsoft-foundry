import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
ENV = {**os.environ, "COURSE_PYTHON": sys.executable, "OMP_NUM_THREADS": "2"}


def run(*args, status=0, parse=True):
    result = subprocess.run(args, cwd=ROOT, env=ENV, capture_output=True,
                            encoding="utf-8", timeout=120)
    assert result.returncode == status, (args, result.returncode, result.stdout, result.stderr)
    return json.loads(result.stdout) if parse else result.stdout + result.stderr


def experiment(task):
    return run(sys.executable, "lab/experiments.py", task)


def exercise():
    path = ROOT / "lab/baseline_exercise.py"
    assert path.is_file(), "Create lab/baseline_exercise.py from lab/baseline.py first."
    reference = run(sys.executable, "lab/baseline.py")
    attempt = run(sys.executable, str(path))
    expected_rows = [(r["text"], r["actual"]) for r in reference["keywords"]["rows"]]
    assert attempt["majority"] == reference["majority"]
    rows = attempt["keywords"]["rows"]
    assert [(r["text"], r["actual"]) for r in rows] == expected_rows
    assert all(r["predicted"] in {"routine", "urgent"} for r in rows)
    correct = sum(r["predicted"] == r["actual"] for r in rows)
    caught = sum(r["actual"] == r["predicted"] == "urgent" for r in rows)
    false_alarms = sum(r["actual"] == "routine" and r["predicted"] == "urgent" for r in rows)
    assert caught == 4 and correct >= 12 and false_alarms <= 1
    print(json.dumps({"exercise": "passed", "correct": correct, "urgentCaught": caught,
                      "falseAlarms": false_alarms}))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--exercise", action="store_true")
    if parser.parse_args().exercise:
        exercise()
        return
    baseline = run(sys.executable, "lab/baseline.py")
    keys = ("correct", "total", "urgentCaught", "urgentTotal")
    assert [tuple(baseline[name][k] for k in keys) for name in ("majority", "keywords")] == [
        (10, 14, 0, 4), (11, 14, 2, 4)]
    fitted = experiment("fit")
    assert tuple(fitted[k] for k in keys) == (12, 14, 2, 4)
    assert (ROOT / "lab/router.joblib").is_file()
    metrics = experiment("metrics")
    assert [metrics[k] for k in ("tn", "fp", "fn", "tp")] == [10, 0, 2, 2]
    assert metrics["recall"] == 0.5 and math.isclose(metrics["f1"], 2 / 3)
    depth = experiment("complexity")
    assert depth["selectedDepth"] == 2 and depth["validation"][1] == depth["validation"][3] == .713
    assert (depth["train"][-1], depth["validation"][-1]) == (1.0, .669)
    regression = experiment("regression")
    assert regression["baseline"] == {"mae": 15.0, "rmse": 16.54}
    assert regression["regression"] == {"mae": 4.8, "rmse": 6.59}
    assert [5.0, 52.0, 39.39, 12.61] in regression["rows"]
    original = experiment("cluster")
    assert sorted(len(g["tickets"]) for g in original["groups"]) == [3, 3, 4]
    failed = run(sys.executable, "lab/cluster-k2-before.py", "cluster", status=1, parse=False)
    assert "IndexError: index 2 is out of bounds for axis 0 with size 2" in failed
    k2 = run(sys.executable, "lab/cluster-k2-after.py", "cluster")
    k3 = run(sys.executable, "lab/cluster-k3-after.py", "cluster")
    assert sorted(len(g["tickets"]) for g in k2["groups"]) == [4, 6]
    assert k3 == original
    run("dotnet", "build", "app", "-c", "Release", parse=False)
    app = run(sys.executable, "lab/app_check.py")
    assert [x["result"]["status"] for x in app] == [
        "invalid", "invalid", "invalid", "review", "review", "review", "routed", "unavailable"]
    assert app[6]["result"]["queue"] == "urgent"
    final = experiment("final")
    assert final["coverage"] == [5, 8] and final["correctAmongRouted"] == [5, 5]
    print(json.dumps({"status": "passed", "lessons": 7, "python": sys.version.split()[0],
                      "dotnet": run("dotnet", "--version", parse=False).strip(),
                      "scikitLearn": fitted["scikitLearn"], "appCases": len(app),
                      "clusterExpectedFailure": "IndexError", "finalCoverage": final["coverage"],
                      "finalCorrectAmongRouted": final["correctAmongRouted"],
                      "dataSha256": hashlib.sha256((ROOT / "lab/data.json").read_bytes()).hexdigest()},
                     indent=2))


if __name__ == "__main__":
    main()
