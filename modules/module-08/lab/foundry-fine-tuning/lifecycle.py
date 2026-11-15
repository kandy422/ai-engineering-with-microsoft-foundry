"""Offline dataset checks and an explicitly enabled, paired Foundry evaluation."""

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import time
from urllib.error import HTTPError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener

ROOT = Path(__file__).resolve().parent
LABELS = {"BILLING", "DELIVERY", "RETURNS"}
SIZES = {"training": 24, "validation": 6, "heldout": 12}
MODEL = "gpt-4.1-mini"
VERSION = "2025-04-14"


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def read_splits():
    splits = {}
    seen = set()
    system = None
    for name, size in SIZES.items():
        raw = (ROOT / f"{name}.jsonl").read_bytes()
        rows = [json.loads(line) for line in raw.decode("utf-8").splitlines()]
        if len(rows) != size:
            raise ValueError(f"{name}: expected {size} rows")
        labels = Counter()
        case_ids = set()
        for row in rows:
            messages = row["messages"]
            roles = ["system", "user"] if name == "heldout" else ["system", "user", "assistant"]
            if [message["role"] for message in messages] != roles:
                raise ValueError(f"{name}: unexpected message roles")
            if any(not message["content"].strip() for message in messages):
                raise ValueError(f"{name}: empty message")
            prefix, prompt = messages[0]["content"], messages[1]["content"]
            system = system or prefix
            key = " ".join(prompt.casefold().split())
            if system != prefix or key in seen:
                raise ValueError(f"{name}: inconsistent instruction or repeated ticket")
            seen.add(key)
            if name == "heldout":
                case = row["case"]
                if case not in {f"test-{n:02}" for n in range(1, size + 1)} or case in case_ids:
                    raise ValueError(f"Unexpected or duplicate case ID: {case}")
                case_ids.add(case)
            labels[row["expected"] if name == "heldout" else messages[2]["content"]] += 1
        if labels != Counter({label: size // 3 for label in LABELS}):
            raise ValueError(f"{name}: invalid label distribution")
        splits[name] = (rows, hashlib.sha256(raw).hexdigest())
    return splits


def validate(splits):
    import tiktoken

    encoding = tiktoken.get_encoding("o200k_base")
    for name, (rows, digest) in splits.items():
        # Approximate chat framing: three per message and three per conversation.
        tokens = [
            3 + sum(3 + len(encoding.encode(m["role"])) + len(encoding.encode(m["content"]))
                    for m in row["messages"])
            for row in rows
        ]
        print(json.dumps({"split": name, "rows": len(rows), "sha256": digest,
                          "approxTokens": sum(tokens), "maxTokensPerRow": max(tokens)}))


def deployment(receipt_path, required_sku):
    receipt = json.loads(receipt_path.read_text(encoding="utf-8-sig"))
    if receipt["properties"]["provisioningState"] != "Succeeded" or receipt["sku"]["name"] != required_sku:
        raise ValueError(f"Unexpected deployment state or SKU in {receipt_path}")
    return receipt


def evaluate(args, heldout, heldout_hash):
    endpoint = urlsplit(args.endpoint)
    if (endpoint.scheme != "https" or not endpoint.hostname
            or not endpoint.hostname.endswith(".openai.azure.com")
            or endpoint.netloc != endpoint.hostname or endpoint.path not in ("", "/")
            or endpoint.query or endpoint.fragment):
        raise ValueError("Use the approved Azure OpenAI resource endpoint")
    base = deployment(args.base_receipt, "GlobalStandard")
    adapted = deployment(args.adapted_receipt, "DeveloperTier")
    job = json.loads(args.job_receipt.read_text(encoding="utf-8-sig"))
    base_model = base["properties"]["model"]
    if (base["name"] == adapted["name"] or
            base["id"].lower().rsplit("/deployments/", 1)[0] != adapted["id"].lower().rsplit("/deployments/", 1)[0] or
            endpoint.hostname.split(".")[0] != base["id"].lower().rsplit("/deployments/", 1)[0].split("/")[-1] or
            (base_model["name"], base_model["version"]) != (MODEL, VERSION) or
            job["status"] != "succeeded" or job["model"] not in (MODEL, f"{MODEL}-{VERSION}") or
            not job["fine_tuned_model"] or adapted["properties"]["model"]["name"] != job["fine_tuned_model"]):
        raise ValueError("Deployment receipts and successful job do not establish the planned pair")
    token = subprocess.check_output([
        shutil.which("az") or "az", "account", "get-access-token",
        "--resource", "https://cognitiveservices.azure.com", "--query", "accessToken", "-o", "tsv",
    ], text=True).strip()
    if not token:
        raise RuntimeError("Azure CLI returned no access token")
    args.output.mkdir(parents=True, exist_ok=False)
    client = build_opener(NoRedirect())
    last_started = {}
    scores = Counter()
    differences = Counter()
    for n, case in enumerate(heldout):
        pair = {}
        keys = ("base", "adapted") if n % 2 == 0 else ("adapted", "base")
        for kind in keys:
            target = (base if kind == "base" else adapted)["name"]
            payload = {"model": target, "messages": case["messages"], "temperature": 0,
                       "max_completion_tokens": 16}
            request = Request(
                f"https://{endpoint.hostname}/openai/v1/chat/completions",
                data=json.dumps(payload).encode(), headers={
                    "Authorization": f"Bearer {token}", "Content-Type": "application/json",
                },
            )
            if kind in last_started:
                time.sleep(max(0, last_started[kind] + args.min_interval_seconds - time.monotonic()))
            last_started[kind] = started = time.monotonic()
            receipt_path = args.output / f'{case["case"]}-{kind}.json'
            try:
                with client.open(request, timeout=30) as response:
                    result = json.load(response)
                    receipt = {"httpStatus": response.status, "requestId": response.headers.get("apim-request-id")}
            except HTTPError as error:
                receipt_path.write_text(json.dumps({
                    "case": case["case"], "kind": kind, "request": payload, "httpStatus": error.code,
                    "requestId": error.headers.get("apim-request-id"), "response": error.read().decode(errors="replace"),
                }, indent=2), encoding="utf-8")
                raise
            choice = result["choices"][0]
            content = choice["message"]["content"]
            correct = isinstance(content, str) and content.strip() == case["expected"] and choice["finish_reason"] == "stop"
            scores[kind] += correct
            pair[kind] = correct
            receipt.update(kind=kind, case=case["case"], request=payload, expected=case["expected"],
                           exactMatch=correct, elapsedMs=round((time.monotonic() - started) * 1000, 2),
                           response=result)
            receipt_path.write_text(json.dumps(receipt, indent=2), encoding="utf-8")
        if pair["base"] != pair["adapted"]:
            differences["adaptedOnly" if pair["adapted"] else "baseOnly"] += 1
    summary = {"jobId": job["id"], "heldoutSha256": heldout_hash, "cases": len(heldout),
               "base": scores["base"], "adapted": scores["adapted"],
               "adaptedOnly": differences["adaptedOnly"], "baseOnly": differences["baseOnly"]}
    (args.output / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(json.dumps(summary))


def main():
    parser = argparse.ArgumentParser()
    modes = parser.add_subparsers(dest="mode", required=True)
    modes.add_parser("validate")
    paired = modes.add_parser("evaluate")
    for arg in ("base-receipt", "adapted-receipt", "job-receipt", "output"):
        paired.add_argument(f"--{arg}", required=True, type=Path)
    paired.add_argument("--endpoint", required=True)
    paired.add_argument("--min-interval-seconds", type=int, choices=range(0, 66), metavar="0..65", default=65)
    paired.add_argument("--approved-live-run", required=True, action="store_true")
    args = parser.parse_args()
    splits = read_splits()
    if args.mode == "validate":
        validate(splits)
    else:
        evaluate(args, *splits["heldout"])


if __name__ == "__main__":
    main()
