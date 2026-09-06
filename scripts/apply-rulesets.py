#!/usr/bin/env python3
"""Bật auto-merge + ruleset bảo vệ nhánh mặc định cho MỌI repo của seeker19110, cùng khuôn Dhcb-Tools.

Mỗi repo:
  1. Repo settings: allow_auto_merge=true, delete_branch_on_merge=true, allow_update_branch=true.
  2. Ruleset trên nhánh mặc định (tạo mới hoặc cập nhật cái đang có): cấm xoá, cấm force-push, bắt buộc
     PR (0 reviewer — repo một người), và bắt buộc BỘ CHECK TỐI THIỂU liệt kê dưới đây.

Vì sao chọn check như dưới: chỉ bắt buộc job cổng tổng hợp (quality/ci/e2e có `needs:` gom mọi job)
khi repo có; đổi ma trận không phải sửa ruleset. Không bắt buộc job có `if:` điều kiện (CodeQL chỉ chạy
khi có mã), job chậm/không ổn định (lighthouse), job chỉ chạy khi push main (deploy, release-please).
Đối chiếu bằng check-runs trên HEAD của PR gần nhất từng repo ngày 2026-09-06.

Chạy:  python scripts/apply-rulesets.py            (cần `gh auth login`)
       python scripts/apply-rulesets.py --dry-run  (chỉ in, không gửi)
       python scripts/apply-rulesets.py xboss Xgold (chỉ vài repo)
"""

from __future__ import annotations

import json
import subprocess
import sys

OWNER = "seeker19110"

# repo → check bắt buộc (tên đúng như check-run trên PR). [] = không có CI, chỉ bắt buộc PR.
REQUIRED: dict[str, list[str]] = {
    "Dhcb-Tools": ["logic-tests"]
    + [f"{job} ({y})" for job in ("check-build", "build-wpf-windows") for y in (2023, 2024, 2025, 2026, 2027)],
    "ai-gateway": ["audit", "lint", "test (20)", "test (22)"],
    "Claude-Agents": ["quality", "metadata"],          # quality needs: [mọi job]
    "X-Agents": ["quality", "metadata"],               # quality needs: [mọi job]
    "donghanh": ["quality", "e2e", "metadata"],        # quality needs: [static, unit, build, audit]; e2e needs: [e2e-shard]
    "xboss": ["ci", "e2e", "gitleaks", "metadata"],    # ci needs: [static, test, build]; e2e needs: [e2e_shard]
    "project-template": ["quality", "e2e", "framework-lint", "docs-consistency", "copy-framework-smoke",
                         "source-hygiene", "gitleaks", "metadata"],
    "Xgold": ["quality", "e2e", "framework-lint", "docs-consistency", "copy-framework-smoke", "gitleaks"],
    # xboss-manager có job protection-guard tự đối chiếu ruleset — danh sách này phải khớp đúng
    # những gì nó đòi (gồm cả lighthouse), không thì job đó đỏ mãi.
    "xboss-manager": ["quality", "e2e", "framework-lint", "docs-consistency", "copy-framework-smoke",
                      "protection-guard", "gitleaks", "lighthouse"],
    "For-Hermes": [],
    "Plugin-4-Hermes": [],
}


def gh(*args: str, input_text: str | None = None, ok_codes: tuple[int, ...] = (0,)) -> str:
    result = subprocess.run(["gh", *args], input=input_text, capture_output=True, text=True, encoding="utf-8")
    if result.returncode not in ok_codes:
        sys.stderr.write(result.stderr)
        raise SystemExit(f"gh {' '.join(args[:4])} thất bại (mã {result.returncode}).")
    return result.stdout


def ruleset_body(name: str, checks: list[str], existing: dict | None) -> dict:
    rules = [r for r in (existing or {}).get("rules", [])
             if r["type"] not in ("required_status_checks", "pull_request", "deletion", "non_fast_forward")]
    rules += [
        {"type": "deletion"},
        {"type": "non_fast_forward"},
        {"type": "pull_request", "parameters": {
            "required_approving_review_count": 0,
            "dismiss_stale_reviews_on_push": False,
            "require_code_owner_review": False,
            "require_last_push_approval": False,
            "required_review_thread_resolution": False,
            "allowed_merge_methods": ["squash", "merge", "rebase"],
        }},
    ]
    if checks:
        rules.append({"type": "required_status_checks", "parameters": {
            "strict_required_status_checks_policy": False,
            "do_not_enforce_on_create": False,
            "required_status_checks": [{"context": c} for c in checks],
        }})
    return {
        "name": name,
        "target": "branch",
        "enforcement": "active",
        "conditions": {"ref_name": {"include": ["~DEFAULT_BRANCH"], "exclude": []}},
        "rules": rules,
        "bypass_actors": (existing or {}).get("bypass_actors", []),
    }


def apply(repo: str, checks: list[str], dry_run: bool) -> None:
    full = f"{OWNER}/{repo}"
    settings = {"allow_auto_merge": True, "delete_branch_on_merge": True, "allow_update_branch": True}
    existing_list = json.loads(gh("api", f"repos/{full}/rulesets"))
    branch_rulesets = [r for r in existing_list if r.get("target") == "branch"]
    existing = json.loads(gh("api", f"repos/{full}/rulesets/{branch_rulesets[0]['id']}")) if branch_rulesets else None
    name = existing["name"] if existing else "main"
    body = ruleset_body(name, checks, existing)

    print(f"== {repo}: ruleset {'cập nhật id=' + str(existing['id']) if existing else 'TẠO MỚI'} "
          f"— check bắt buộc: {checks or '(không có CI — chỉ bắt buộc PR)'}")
    if dry_run:
        print("   settings:", json.dumps(settings))
        print("   ruleset :", json.dumps(body, ensure_ascii=False)[:400], "…")
        return

    gh("api", "-X", "PATCH", f"repos/{full}", "--input", "-", input_text=json.dumps(settings))
    if existing:
        out = gh("api", "-X", "PUT", f"repos/{full}/rulesets/{existing['id']}", "--input", "-", input_text=json.dumps(body))
    else:
        out = gh("api", "-X", "POST", f"repos/{full}/rulesets", "--input", "-", input_text=json.dumps(body))
    updated = json.loads(out)
    print("   include:", updated["conditions"]["ref_name"]["include"], "· rules:", [r["type"] for r in updated["rules"]])


def main(argv: list[str]) -> int:
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is not None:
            reconfigure(encoding="utf-8", errors="replace")

    dry_run = "--dry-run" in argv
    wanted = [a for a in argv if not a.startswith("--")] or list(REQUIRED)
    unknown = [r for r in wanted if r not in REQUIRED]
    if unknown:
        raise SystemExit(f"Repo chưa khai trong REQUIRED: {unknown}")

    for repo in wanted:
        apply(repo, REQUIRED[repo], dry_run)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
