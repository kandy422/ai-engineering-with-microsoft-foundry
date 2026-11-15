import argparse
import json
from pathlib import Path

MATRIX = json.loads(Path(__file__).with_name("cases.json").read_text(encoding="utf8"))
CASES = MATRIX["cases"]


def check(receipt):
    failures = []
    def require(condition, name):
        if not condition:
            failures.append(name)

    require(receipt.get("mode") == "hosted", "Hosted evidence required")
    for field in ("revision", "indexVersion", "storeAlias", "runId"):
        require(bool(receipt.get(field)), f"Missing {field}")
    rows = receipt.get("cases", [])
    by_id = {row["id"]: row for row in rows}
    require(len(rows) == len(by_id) and set(by_id) == {c["id"] for c in CASES},
            "Missing, duplicate or unexpected cases")
    for spec in CASES:
        case = spec["id"]
        row = by_id.get(case, {})
        local_contract = case in {"scope-field-schema", "untrusted-evidence"}
        require(row.get("mode") == ("local-contract" if local_contract else "hosted"),
                f"{case}: evidence mode")
        code = row.get("http")
        require(code in spec["http"], f"{case}: HTTP outcome")
        if code == 200 and spec["status"]:
            require(row.get("status") in spec["status"], f"{case}: domain outcome")
        require(row.get("writes") == spec["writes"], f"{case}: observed write delta")
        require(bool(row.get("evidenceRef")), f"{case}: evidence reference")
        if "identity" in spec:
            identity = MATRIX["identityAliases"][spec["identity"]]
            proof = row.get("identityEvidence") or {}
            require(proof.get("kind") == identity["kind"], f"{case}: caller type")
            require(proof.get("tokenSource") == "entra-live" and
                    proof.get("signatureIssuerAudienceValidated") is True and
                    bool(proof.get("validationRef")), f"{case}: live token validation evidence")
            service = identity["kind"] == "service-principal"
            if service:
                require(proof.get("credentialMethod") == "certificate" and
                        bool(proof.get("credentialEvidenceRef")), f"{case}: certificate evidence")
            denial = identity.get("denial")
            if denial:
                require(proof.get("authorizationOutcome") == denial and
                        proof.get("resolvedCustomerScope") is None and
                        row.get("sensitiveDataReturned") is False, f"{case}: authorization boundary")
                for field, failure in (
                    ("dedicatedRolePresent", "required-role-missing"),
                    ("subjectMapped", "subject-unmapped"),
                    ("clientAllowlisted", "client-unlisted"),
                ):
                    require(proof.get(field) is (denial != failure), f"{case}: isolated {field} check")
            else:
                require(proof.get("clientAlias") == identity["client"] and
                        proof.get("subjectAlias") == identity["subject"] and
                        proof.get("resolvedCustomerScope") == identity["customerScope"] and
                        proof.get("clientAllowlisted") is True and
                        proof.get("subjectMapped") is True, f"{case}: trusted identity mapping")
                field = "dedicatedRolePresent" if service else "delegatedScopePresent"
                require(proof.get(field) is True, f"{case}: required authorization claim")
        if case in {"missing-identity", "missing-scope", "cross-customer", "unknown-order",
                    "scope-field-schema", "untrusted-evidence", "wrong-approver"}:
            require(row.get("sensitiveDataReturned") is False, f"{case}: disclosure check")
    cross = by_id.get("cross-customer", {})
    unknown = by_id.get("unknown-order", {})
    require((cross.get("http"), cross.get("status")) == (unknown.get("http"), unknown.get("status")),
            "Cross-scope and absent orders must have equivalent public outcomes")

    own = by_id.get("owned-evidence", {})
    scoped = by_id.get("scoped-evidence", {})
    a_id = receipt.get("seedCitationA")
    b_id = receipt.get("seedCitationB")
    require(bool(a_id and b_id and a_id != b_id), "Distinct seeded citations")
    for row, citation in ((own, b_id), (scoped, a_id)):
        require(bool(citation) and row.get("citation") == citation, "Scope-specific citation")
        require(bool(citation) and row.get("contextCitationIds") == [citation],
                "Only the applicable seed may enter model context")
    evidence = by_id.get("untrusted-evidence", {})
    require(evidence.get("toolDispatchCount") == 0, "Untrusted evidence must dispatch no tool")
    require(evidence.get("sourceWasPreclassified") is True, "Use the defensive boundary fixture")

    sequence = [by_id.get(case,{}) for case in
                ("propose", "wrong-approver", "approve", "duplicate", "after-restart")]
    proposal = sequence[0].get("proposalRef")
    require(bool(proposal) and all(row.get("proposalRef") == proposal for row in sequence),
            "Stable proposal across approval and replay cases")
    before, after = receipt.get("instanceBefore"), receipt.get("instanceAfter")
    require(bool(before and after and before != after and receipt.get("restartEvidenceRef")),
            "Evidence of distinct instances and a restart")
    require(receipt.get("durableWriteCount") == 1 and bool(receipt.get("durableEvidenceRef")),
            "Exactly one observed persistent side effect")
    return failures


def main():
    parser = argparse.ArgumentParser(description="Verify sanitized capstone evidence locally")
    parser.add_argument("receipt", type=Path)
    args = parser.parse_args()
    receipt = json.loads(args.receipt.read_text(encoding="utf-8-sig"))
    failures = check(receipt)
    print(json.dumps({"state": "failed" if failures else "receipt-assertions-passed",
                      "failures": failures}, indent=2))
    return bool(failures)


if __name__ == "__main__":
    raise SystemExit(main())
