from pathlib import Path
import subprocess
import sys
import time
import urllib.request
from datetime import datetime, timezone

root = Path(__file__).resolve().parent
checkpoint = sys.argv[1]
assert checkpoint in ("live-tool-loop", "live-orchestration")
done = root / f"qa/{checkpoint}-complete.txt"
reproduce = "--repeat" in sys.argv[2:]
if done.exists() and not reproduce:
    print(done.read_text(encoding="utf-8"))
    sys.exit(0)
output_dir = root / "lab"
logs = root / "qa"
if reproduce:
    output_dir = logs / "reproduction" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    output_dir.mkdir(parents=True)
    logs = output_dir
runtime = root / ".model/runtime/llama-server.exe"
weights = root / ".model/Qwen3-0.6B-Q8_0.gguf"
with (logs / f"{checkpoint}-server.log").open("wb") as log:
    server = subprocess.Popen([runtime, "-m", weights, "--jinja", "--host", "127.0.0.1",
                               "--port", "8411", "-c", "2048", "-t", "2", "-tb", "2", "-np", "1"],
                              stdout=log, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        for _ in range(45):
            if server.poll() is not None:
                raise RuntimeError(f"Model server exited with code {server.returncode}")
            try:
                with urllib.request.urlopen("http://127.0.0.1:8411/health", timeout=1) as reply:
                    if reply.status == 200:
                        break
            except OSError:
                time.sleep(1)
        else:
            raise TimeoutError("Local model health check timed out")
        checkpoints = [checkpoint, "live-orchestration"] if checkpoint == "live-tool-loop" else [checkpoint]
        for test in checkpoints:
            with (output_dir / f"{test}.ndjson").open("wb") as output:
                subprocess.run(["dotnet","run","--project","lab","--no-build","--",test],
                               cwd=root,stdout=output,check=True)
            print("Recorded", test, flush=True)
        if not reproduce:
            done.write_text("Local model checkpoints recorded.",encoding="utf-8")
    finally:
        server.terminate()
        server.wait(timeout=10)




