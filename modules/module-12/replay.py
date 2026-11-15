"""Replay actual Module 12 receipts without contacting Azure."""

import argparse
from hashlib import sha256
import json
from pathlib import Path
import wave

root = Path(__file__).resolve().parent
captures = root / "learner-recordings"


def read(file):
    return json.loads(file.read_text(encoding="utf-8-sig"))


def verify(directory, request, saved, status=200):
    file = directory / saved
    receipt = read(directory / f"{request}.http.json")
    assert receipt["status"] == status
    assert sha256(file.read_bytes()).hexdigest() == receipt["responseSha256"].lower()
    return file


def text(directory, request):
    file = verify(directory, request, f"{request}.response.json")
    response = read(file)
    assert response["RecognitionStatus"] == "Success"
    return response.get("DisplayText") or response["NBest"][0]["Display"]


def document(directory, request):
    assert read(directory / f"{request}.http.json")["status"] == 202
    polls = directory.glob(f"{request}.poll-*.http.json")
    poll = max(polls, key=lambda file: int(file.name.split("poll-")[1].split(".")[0]))
    final = verify(directory, poll.name.removesuffix(".http.json"), f"{request}.result.response.json")
    result = read(final)
    assert result["status"] == "succeeded"
    return result["analyzeResult"]


def replay(lesson):
    directory = captures / lesson
    if lesson == "12.2":
        result = {}
        for case in ("thumbnail", "detail"):
            model = read(verify(directory, case, f"{case}.response.json"))
            choice = model["choices"][0]
            result[case] = {"model": model["model"], "response": choice["message"]["content"],
                            "finish": choice["finish_reason"], "inputTokens": model["usage"]["prompt_tokens"],
                            "outputTokens": model["usage"]["completion_tokens"]}
        assert "headset" in result["thumbnail"]["response"] and "apex" in result["detail"]["response"]
        return result
    if lesson == "12.3":
        cases = {}
        for case in ("receipt", "mismatch"):
            fields = document(directory, case)["documents"][0]["fields"]
            subtotal, tax, total = (fields[name]["valueCurrency"]["amount"]
                                    for name in ("Subtotal", "TotalTax", "Total"))
            cases[case] = {"date": fields["TransactionDate"]["valueDate"], "total": total,
                           "totalConfidence": fields["Total"]["confidence"],
                           "reconciles": round(subtotal + tax, 2) == total}
        layout = document(directory, "layout")
        assert cases["receipt"]["reconciles"] and not cases["mismatch"]["reconciles"]
        return {"receipts": cases, "layoutTables": len(layout.get("tables", []))}
    if lesson == "12.4":
        clean, noisy = text(directory, "clean"), text(directory, "noisy")
        assert "048271" in clean and "048271" in noisy and "newspaper" in noisy
        return {"clean": clean, "noisy": noisy}
    if lesson == "12.5":
        failed = directory / "first-attempt"
        delivered = directory / "synthesis-retry"
        heard = text(failed, "prerecorded-fictional")
        verify(failed, "synthesis", "synthesis.response.json", 400)
        wav = verify(delivered, "synthesis", "reply.wav")
        with wave.open(str(wav), "rb") as audio:
            seconds = audio.getnframes() / audio.getframerate()
        start = read(delivered / "playback.json")
        stop = read(delivered / "interruption.json")
        fallback = read(delivered / "fallback.json")
        assert seconds == start["sourceSeconds"] and start["startCallReturned"] and stop["stopCallReturned"]
        assert start["stopAfterMs"] <= stop["stopRequestedMs"] < seconds * 1000 and fallback["exactFixtureMatch"]
        return {"heard": heard, "firstSynthesisHttp": 400, "retrySynthesisHttp": 200, "wavSeconds": seconds,
                "stopRequestedMs": stop["stopRequestedMs"], "stopCallReturned": True,
                "fixtureTextMatched": True, "audioScope": "recorded source WAV and Windows API metrics"}
    if lesson == "12.7":
        receipt = document(directory, "receipt")
        assert "ORDER 048271" in receipt["content"].splitlines()
        agreement, conflict = text(directory, "agreement"), text(directory, "conflict")
        decisions = read(directory / "decisions.json")
        assert "048271" in agreement and "048272" in conflict
        assert decisions["agreement"] == "eligible-for-policy-reply" and decisions["conflict"] == "clarify"
        return {"voiceAgreement": agreement, "voiceConflict": conflict, "receiptReference": "048271",
                "localDecisions": decisions}
    raise ValueError(lesson)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("lesson", choices=("12.2", "12.3", "12.4", "12.5", "12.7"))
    params = parser.parse_args()
    print(json.dumps(replay(params.lesson), indent=2, ensure_ascii=False))
