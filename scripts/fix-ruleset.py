#!/usr/bin/env python3
"""Sửa ruleset `main` của repo GitHub: chỉ áp lên nhánh mặc định + bắt buộc 11 check CI xanh.

Vì sao có file này: ruleset tạo tay ngày 2026-09-06 include cả "~ALL" nên áp lên MỌI nhánh —
không đẩy được commit lên nhánh PR, không xoá được nhánh sau merge. Và thiếu required_status_checks
nên `gh pr merge --auto` vẫn merge ngay không chờ CI.

Chạy:  python scripts/fix-ruleset.py            (cần `gh auth login` sẵn; in ra cấu hình sau khi sửa)
       python scripts/fix-ruleset.py --dry-run  (chỉ in body sẽ gửi, không gửi)
"""

from __future__ import annotations

import json
import subprocess
import sys

REPO = "seeker19110/Dhcb-Tools"

# Tên check đúng như trong .github/workflows/tests.yml (job + ma trận).
REQUIRED_CHECKS = ["logic-tests"] + [
    f"{job} ({year})"
    for job in ("check-build", "build-wpf-windows")
    for year in (2023, 2024, 2025, 2026, 2027)
]


def gh(*args: str, input_text: str | None = None) -> str:
    result = subprocess.run(
        ["gh", *args], input=input_text, capture_output=True, text=True, encoding="utf-8")
    if result.returncode != 0:
        sys.stderr.write(result.stderr)
        sys.exit(f"gh {' '.join(args[:3])} thất bại (mã {result.returncode}).")
    return result.stdout


def main(argv: list[str]) -> int:
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is not None:
            reconfigure(encoding="utf-8", errors="replace")

    dry_run = "--dry-run" in argv

    rulesets = json.loads(gh("api", f"repos/{REPO}/rulesets"))
    branch_rulesets = [r for r in rulesets if r.get("target") == "branch"]
    if not branch_rulesets:
        print("Repo chưa có ruleset nào cho nhánh — tạo mới tên 'main'.")
        current = {"name": "main", "target": "branch", "enforcement": "active",
                   "conditions": {}, "rules": [], "bypass_actors": []}
        ruleset_id = None
    else:
        ruleset_id = branch_rulesets[0]["id"]
        current = json.loads(gh("api", f"repos/{REPO}/rulesets/{ruleset_id}"))
        print(f"Ruleset hiện có: id={ruleset_id} name={current['name']} "
              f"include={current['conditions']['ref_name']['include']} "
              f"rules={[r['type'] for r in current['rules']]}")

    rules = [r for r in current.get("rules", []) if r["type"] != "required_status_checks"]
    if not any(r["type"] == "pull_request" for r in rules):
        rules.append({"type": "pull_request", "parameters": {
            "required_approving_review_count": 0,
            "dismiss_stale_reviews_on_push": False,
            "require_code_owner_review": False,
            "require_last_push_approval": False,
            "required_review_thread_resolution": False,
            "allowed_merge_methods": ["squash", "merge", "rebase"],
        }})
    for kind in ("deletion", "non_fast_forward"):
        if not any(r["type"] == kind for r in rules):
            rules.append({"type": kind})
    rules.append({"type": "required_status_checks", "parameters": {
        "strict_required_status_checks_policy": False,
        "do_not_enforce_on_create": False,
        "required_status_checks": [{"context": c} for c in REQUIRED_CHECKS],
    }})

    body = {
        "name": current.get("name", "main"),
        "target": "branch",
        "enforcement": current.get("enforcement", "active"),
        # CHỈ nhánh mặc định. "~ALL" là nguyên nhân chặn cả nhánh PR.
        "conditions": {"ref_name": {"include": ["~DEFAULT_BRANCH"], "exclude": []}},
        "rules": rules,
        "bypass_actors": current.get("bypass_actors", []),
    }

    if dry_run:
        print(json.dumps(body, ensure_ascii=False, indent=2))
        return 0

    if ruleset_id is None:
        result = gh("api", "-X", "POST", f"repos/{REPO}/rulesets", "--input", "-",
                    input_text=json.dumps(body))
    else:
        result = gh("api", "-X", "PUT", f"repos/{REPO}/rulesets/{ruleset_id}", "--input", "-",
                    input_text=json.dumps(body))

    updated = json.loads(result)
    print("Đã sửa.")
    print("  include:", updated["conditions"]["ref_name"]["include"])
    print("  rules:  ", [r["type"] for r in updated["rules"]])
    checks = next((r for r in updated["rules"] if r["type"] == "required_status_checks"), None)
    if checks:
        print("  check bắt buộc:", [c["context"] for c in checks["parameters"]["required_status_checks"]])
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
