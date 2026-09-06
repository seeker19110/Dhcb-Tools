"""Test cho scripts/fix-ruleset.py và scripts/apply-rulesets.py — hai script sửa ruleset GitHub.

`gh` được giả bằng cách thay `subprocess.run`: ghi lại mọi lệnh gọi, trả JSON theo kịch bản. Không
chạm mạng. Mục tiêu: mọi nhánh (tạo mới / cập nhật, dry-run / thật, gh lỗi, repo lạ) đều có assert,
vì đây là script sửa cấu hình bảo vệ nhánh — chạy sai một lần là mọi repo mất cổng CI.
"""

from __future__ import annotations

import importlib.util
import io
import json
import subprocess
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from types import SimpleNamespace

SCRIPTS = Path(__file__).resolve().parents[2] / "scripts"


def load(name: str):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), SCRIPTS / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


fix_ruleset = load("fix-ruleset")
apply_rulesets = load("apply-rulesets")


class FakeGh:
    """Giả `subprocess.run(["gh", ...])`: trả lời theo (method, path), ghi lại body đã gửi."""

    def __init__(self, responses: dict[tuple[str, str], object], fail: tuple[str, str] | None = None) -> None:
        self.responses = responses
        self.fail = fail
        self.calls: list[tuple[str, str, object]] = []

    def __call__(self, cmd, input=None, capture_output=False, text=False, encoding=None):
        assert cmd[0] == "gh" and cmd[1] == "api"
        method = "GET"
        rest = cmd[2:]
        if rest[0] == "-X":
            method, rest = rest[1], rest[2:]
        path = rest[0]
        body = json.loads(input) if input else None
        self.calls.append((method, path, body))
        if self.fail == (method, path):
            return SimpleNamespace(returncode=1, stdout="", stderr="gh: lỗi giả\n")
        payload = self.responses[(method, path)]
        if callable(payload):
            payload = payload(body)
        return SimpleNamespace(returncode=0, stdout=json.dumps(payload), stderr="")


def echo_ruleset(body):
    """PUT/POST trả về đúng body kèm id — như GitHub làm."""
    return {"id": 99, **body}


EXISTING = {
    "id": 22338598, "name": "main", "target": "branch", "enforcement": "active",
    "conditions": {"ref_name": {"include": ["~DEFAULT_BRANCH", "~ALL"], "exclude": []}},
    "rules": [
        {"type": "deletion"}, {"type": "non_fast_forward"},
        {"type": "pull_request", "parameters": {"required_approving_review_count": 0}},
        {"type": "copilot_code_review", "parameters": {"review_on_push": False}},
        {"type": "required_status_checks", "parameters": {"required_status_checks": [{"context": "cũ"}]}},
    ],
    "bypass_actors": [{"actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "always"}],
}


class ConsoleBuffer(io.TextIOWrapper):
    """stdout giả CÓ reconfigure() — như console thật — để nhánh đặt lại encoding UTF-8 chạy được
    (StringIO không có reconfigure nên chỉ đi nhánh bỏ qua)."""

    def __init__(self) -> None:
        super().__init__(io.BytesIO(), encoding="cp1252", errors="strict", write_through=True)

    def getvalue(self) -> str:
        self.flush()
        return self.buffer.getvalue().decode("utf-8", errors="replace")


def run(module, argv: list[str], gh: FakeGh, console: bool = False) -> tuple[int, str, str]:
    out = ConsoleBuffer() if console else io.StringIO()
    err = io.StringIO()
    original = subprocess.run
    subprocess.run = gh
    try:
        with redirect_stdout(out), redirect_stderr(err):
            try:
                code = module.main(argv)
            except SystemExit as exc:
                code = exc.code
    finally:
        subprocess.run = original
    return code, out.getvalue(), err.getvalue()


class FixRulesetTests(unittest.TestCase):
    REPO = fix_ruleset.REPO

    def test_dry_run_chi_in_body_khong_gui(self) -> None:
        gh = FakeGh({("GET", f"repos/{self.REPO}/rulesets"): [EXISTING],
                     ("GET", f"repos/{self.REPO}/rulesets/22338598"): EXISTING})

        code, out, _ = run(fix_ruleset, ["--dry-run"], gh)

        self.assertEqual(0, code)
        self.assertNotIn("PUT", [c[0] for c in gh.calls])
        body = json.loads(out[out.index("{"):])
        self.assertEqual(["~DEFAULT_BRANCH"], body["conditions"]["ref_name"]["include"])   # bỏ ~ALL
        checks = [r for r in body["rules"] if r["type"] == "required_status_checks"][0]
        self.assertEqual(11, len(checks["parameters"]["required_status_checks"]))
        self.assertIn("copilot_code_review", [r["type"] for r in body["rules"]])   # rule lạ giữ nguyên
        self.assertEqual(EXISTING["bypass_actors"], body["bypass_actors"])

    def test_cap_nhat_ruleset_dang_co_bang_put(self) -> None:
        gh = FakeGh({("GET", f"repos/{self.REPO}/rulesets"): [EXISTING],
                     ("GET", f"repos/{self.REPO}/rulesets/22338598"): EXISTING,
                     ("PUT", f"repos/{self.REPO}/rulesets/22338598"): echo_ruleset})

        code, out, _ = run(fix_ruleset, [], gh)

        self.assertEqual(0, code)
        self.assertIn(("PUT", f"repos/{self.REPO}/rulesets/22338598"), [c[:2] for c in gh.calls])
        self.assertIn("Đã sửa.", out)
        self.assertIn("logic-tests", out)

    def test_chua_co_ruleset_thi_tao_moi_bang_post_va_du_ba_rule_nen(self) -> None:
        gh = FakeGh({("GET", f"repos/{self.REPO}/rulesets"): [],
                     ("POST", f"repos/{self.REPO}/rulesets"): echo_ruleset})

        code, out, _ = run(fix_ruleset, [], gh)

        self.assertEqual(0, code)
        _, _, body = [c for c in gh.calls if c[0] == "POST"][0]
        types = [r["type"] for r in body["rules"]]
        for kind in ("pull_request", "deletion", "non_fast_forward", "required_status_checks"):
            self.assertIn(kind, types)
        self.assertIn("tạo mới", out)

    def test_gh_loi_thi_dung_va_in_stderr(self) -> None:
        gh = FakeGh({}, fail=("GET", f"repos/{self.REPO}/rulesets"))

        code, _, err = run(fix_ruleset, [], gh)

        self.assertIn("thất bại", str(code))
        self.assertIn("lỗi giả", err)

    def test_console_cp1252_van_in_duoc_tieng_viet(self) -> None:
        gh = FakeGh({("GET", f"repos/{self.REPO}/rulesets"): []})

        code, out, _ = run(fix_ruleset, ["--dry-run"], gh, console=True)

        self.assertEqual(0, code)
        self.assertIn("tạo mới", out)

    def test_required_checks_dung_11_ten(self) -> None:
        self.assertEqual(11, len(fix_ruleset.REQUIRED_CHECKS))
        self.assertIn("build-wpf-windows (2027)", fix_ruleset.REQUIRED_CHECKS)


class ApplyRulesetsTests(unittest.TestCase):
    OWNER = apply_rulesets.OWNER

    def _gh(self, repo: str, existing: dict | None, extra: dict | None = None) -> FakeGh:
        base = f"repos/{self.OWNER}/{repo}"
        responses: dict = {
            ("GET", f"{base}/rulesets"): [existing] if existing else [],
            ("PATCH", base): {},
            ("POST", f"{base}/rulesets"): echo_ruleset,
        }
        if existing:
            responses[("GET", f"{base}/rulesets/{existing['id']}")] = existing
            responses[("PUT", f"{base}/rulesets/{existing['id']}")] = echo_ruleset
        responses.update(extra or {})
        return FakeGh(responses)

    def test_ruleset_body_giu_rule_la_va_bypass_thay_bon_rule_nen(self) -> None:
        body = apply_rulesets.ruleset_body("main", ["a", "b"], EXISTING)

        types = [r["type"] for r in body["rules"]]
        self.assertEqual(1, types.count("pull_request"))
        self.assertEqual(1, types.count("required_status_checks"))
        self.assertIn("copilot_code_review", types)
        self.assertEqual(["~DEFAULT_BRANCH"], body["conditions"]["ref_name"]["include"])
        self.assertEqual(EXISTING["bypass_actors"], body["bypass_actors"])

    def test_ruleset_body_khong_co_check_thi_khong_co_rule_status(self) -> None:
        body = apply_rulesets.ruleset_body("main", [], None)

        self.assertNotIn("required_status_checks", [r["type"] for r in body["rules"]])
        self.assertEqual([], body["bypass_actors"])

    def test_dry_run_mot_repo_khong_gui_gi(self) -> None:
        gh = self._gh("Xgold", None)

        code, out, _ = run(apply_rulesets, ["--dry-run", "Xgold"], gh)

        self.assertEqual(0, code)
        self.assertEqual({"GET"}, {c[0] for c in gh.calls})
        self.assertIn("TẠO MỚI", out)
        self.assertIn("allow_auto_merge", out)

    def test_tao_moi_patch_settings_roi_post(self) -> None:
        gh = self._gh("xboss", None)

        code, out, _ = run(apply_rulesets, ["xboss"], gh)

        self.assertEqual(0, code)
        methods = [c[0] for c in gh.calls]
        self.assertIn("PATCH", methods)
        self.assertIn("POST", methods)
        _, _, settings = [c for c in gh.calls if c[0] == "PATCH"][0]
        self.assertTrue(settings["allow_auto_merge"])
        self.assertIn("required_status_checks", out)

    def test_cap_nhat_ruleset_co_san_bang_put_giu_ten(self) -> None:
        existing = {**EXISTING, "id": 7, "name": "main — bảo vệ nhánh chính"}
        gh = self._gh("Claude-Agents", existing)

        code, out, _ = run(apply_rulesets, ["Claude-Agents"], gh)

        self.assertEqual(0, code)
        _, _, body = [c for c in gh.calls if c[0] == "PUT"][0]
        self.assertEqual("main — bảo vệ nhánh chính", body["name"])
        self.assertIn("cập nhật id=7", out)

    def test_repo_khong_co_ci_chi_bat_buoc_pr(self) -> None:
        gh = self._gh("For-Hermes", None)

        code, out, _ = run(apply_rulesets, ["For-Hermes"], gh)

        self.assertEqual(0, code)
        _, _, body = [c for c in gh.calls if c[0] == "POST"][0]
        self.assertNotIn("required_status_checks", [r["type"] for r in body["rules"]])
        self.assertIn("không có CI", out)

    def test_repo_la_thi_tu_choi(self) -> None:
        code, _, _ = run(apply_rulesets, ["repo-khong-ton-tai"], FakeGh({}))

        self.assertIn("chưa khai", str(code))

    def test_khong_truyen_repo_thi_di_het_danh_sach_dry_run(self) -> None:
        responses: dict = {}
        for repo in apply_rulesets.REQUIRED:
            responses[("GET", f"repos/{self.OWNER}/{repo}/rulesets")] = []
        gh = FakeGh(responses)

        code, out, _ = run(apply_rulesets, ["--dry-run"], gh)

        self.assertEqual(0, code)
        self.assertEqual(len(apply_rulesets.REQUIRED), out.count("== "))

    def test_console_cp1252_van_in_duoc_tieng_viet(self) -> None:
        gh = self._gh("Xgold", None)

        code, out, _ = run(apply_rulesets, ["--dry-run", "Xgold"], gh, console=True)

        self.assertEqual(0, code)
        self.assertIn("TẠO MỚI", out)

    def test_gh_loi_thi_dung(self) -> None:
        gh = FakeGh({}, fail=("GET", f"repos/{self.OWNER}/xboss/rulesets"))

        code, _, err = run(apply_rulesets, ["xboss"], gh)

        self.assertIn("thất bại", str(code))
        self.assertIn("lỗi giả", err)

    def test_danh_sach_check_khong_co_ten_trung_va_khong_rong_ngoai_hai_repo_khong_ci(self) -> None:
        for repo, checks in apply_rulesets.REQUIRED.items():
            self.assertEqual(len(checks), len(set(checks)), repo)
            if repo not in ("For-Hermes", "Plugin-4-Hermes"):
                self.assertTrue(checks, repo)


if __name__ == "__main__":
    unittest.main()
