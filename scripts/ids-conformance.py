"""Đối chiếu bộ kiểm IDS với bộ ca chính thức của buildingSMART (IDS/Documentation/ImplementersDocumentation/TestCases).

Mỗi ca là một cặp <tên>.ids + <tên>.ifc, tên bắt đầu bằng kết quả đúng: pass- / fail- / invalid-. Script chạy
`BatchRunner --verify-ifc <ifc> --verify-ids <ids>` cho từng cặp và so mã thoát (0 = pass, 1 = fail,
khác = file IDS không dùng được; ca invalid- chấp nhận cả fail lẫn invalid).

Ca lệch đã biết nằm ở tests/ids-buildingsmart/known-gaps.txt. Mã thoát 1 khi có ca lệch NGOÀI danh sách đó
(hồi quy) — ca trong danh sách mà nay đã khớp chỉ được nhắc để xoá dòng, không làm đỏ.

Dùng:  python scripts/ids-conformance.py <thư mục TestCases> --runner <DhcbTools.BatchRunner.dll>
"""
import argparse
import collections
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
KNOWN_GAPS = ROOT / "tests" / "ids-buildingsmart" / "known-gaps.txt"
OUTCOME = {0: "pass", 1: "fail"}


def expected_of(ids: Path) -> str:
    return ids.name.split("-", 1)[0]


def matches(expected: str, got: str) -> bool:
    return got == expected or (expected == "invalid" and got == "fail")


def run_case(runner: str, ids: Path) -> str:
    completed = subprocess.run(
        ["dotnet", runner, "--verify-ifc", str(ids.with_suffix(".ifc")), "--verify-ids", str(ids)],
        capture_output=True, text=True, timeout=120, check=False)
    return OUTCOME.get(completed.returncode, "invalid")


def load_known(path: Path) -> set:
    lines = path.read_text(encoding="utf-8").splitlines()
    return {line.strip() for line in lines if line.strip() and not line.startswith("#")}


def check(testcases: Path, runner: str, known: set) -> tuple:
    """Trả (số khớp, tổng, theo nhóm, ca lệch mới, ca đã hết lệch)."""
    per_group = collections.defaultdict(lambda: [0, 0])
    regressions, fixed = [], []
    total = passed = 0
    for ids in sorted(testcases.rglob("*.ids")):
        if not ids.with_suffix(".ifc").exists():
            continue
        case = f"{ids.parent.name}/{ids.stem}"
        ok = matches(expected_of(ids), run_case(runner, ids))
        total += 1
        passed += ok
        per_group[ids.parent.name][0] += ok
        per_group[ids.parent.name][1] += 1
        if not ok and case not in known:
            regressions.append(case)
        if ok and case in known:
            fixed.append(case)
    return passed, total, dict(per_group), regressions, fixed


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("testcases", type=Path)
    parser.add_argument("--runner", required=True, help="đường dẫn DhcbTools.BatchRunner.dll")
    parser.add_argument("--known", type=Path, default=KNOWN_GAPS)
    args = parser.parse_args(argv)

    passed, total, groups, regressions, fixed = check(args.testcases, args.runner, load_known(args.known))
    print(f"Khớp {passed}/{total} ca buildingSMART.")
    for group, (ok, count) in sorted(groups.items()):
        print(f"  {group:15} {ok}/{count}")
    for case in fixed:
        print(f"ĐÃ KHỚP (xoá khỏi {args.known.name}): {case}")
    for case in regressions:
        print(f"HỒI QUY (lệch mà không có trong {args.known.name}): {case}", file=sys.stderr)
    if total == 0:
        print(f"Không thấy ca nào trong {args.testcases}.", file=sys.stderr)
        return 1
    return 1 if regressions else 0


if __name__ == "__main__":
    sys.exit(main())
