"""Test cho scripts/check-coverage.py — chính cái cổng phủ dòng của CI.

Cổng mà sai thì hoặc CI đỏ oan, hoặc (tệ hơn) code không có test lọt qua mà không ai biết.
"""

from __future__ import annotations

import importlib.util
import io
import unittest
from contextlib import redirect_stdout
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "check-coverage.py"
_spec = importlib.util.spec_from_file_location("check_coverage", SCRIPT)
check_coverage = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(check_coverage)


def write_report(folder: Path, line_rate: float, missed: list[tuple[str, int]]) -> Path:
    by_file: dict[str, list[int]] = {}
    for filename, number in missed:
        by_file.setdefault(filename, []).append(number)
    classes = "".join(
        f'<class filename="{filename}"><lines>'
        + "".join(f'<line number="{n}" hits="0" />' for n in numbers)
        + '<line number="1" hits="3" /></lines></class>'
        for filename, numbers in by_file.items()
    ) or '<class filename="a.cs"><lines><line number="1" hits="3" /></lines></class>'
    report = folder / "guid" / "coverage.cobertura.xml"
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(
        f'<?xml version="1.0"?><coverage line-rate="{line_rate}"><packages><package>{classes}</package></packages></coverage>',
        encoding="utf-8")
    return report


class CheckCoverageTests(unittest.TestCase):
    def setUp(self) -> None:
        import tempfile

        self._temp = tempfile.TemporaryDirectory()
        self.addCleanup(self._temp.cleanup)
        self.folder = Path(self._temp.name)

    def _run(self, *args) -> tuple[int, str]:
        with redirect_stdout(io.StringIO()) as out:
            code = check_coverage.main(["check-coverage.py", *args])
        return code, out.getvalue()

    def test_khong_co_bao_cao_thi_ma_thoat_2(self) -> None:
        code, text = self._run(str(self.folder))

        self.assertEqual(2, code)
        self.assertIn("Không tìm thấy coverage.cobertura.xml", text)

    def test_du_nguong_thi_qua(self) -> None:
        write_report(self.folder, 1.0, [])

        code, text = self._run(str(self.folder))

        self.assertEqual(0, code)
        self.assertIn("100.00%", text)

    def test_thieu_thi_do_va_chi_thang_dong_chua_phu(self) -> None:
        write_report(self.folder, 0.9998, [("Ai/OllamaClient.cs", 377)])

        code, text = self._run(str(self.folder))

        self.assertEqual(1, code)
        self.assertIn("chưa phủ: Ai/OllamaClient.cs:377", text)
        self.assertIn("ExcludeFromCodeCoverage", text)

    def test_nhieu_dong_thi_cat_bot_va_bao_con_bao_nhieu(self) -> None:
        write_report(self.folder, 0.5, [("A.cs", n) for n in range(check_coverage.MAX_LISTED + 5)])

        code, text = self._run(str(self.folder))

        self.assertEqual(1, code)
        self.assertIn("và 5 dòng nữa", text)

    def test_nguong_tha_long_duoc_qua_tham_so(self) -> None:
        write_report(self.folder, 0.95, [("A.cs", 1)])

        self.assertEqual(0, self._run(str(self.folder), "90")[0])

    def test_mac_dinh_doc_thu_muc_coverage(self) -> None:
        """Không truyền đường dẫn thì đọc ./coverage — chạy trong thư mục tạm để không phụ thuộc cwd."""
        import os

        cwd = os.getcwd()
        os.chdir(self.folder)
        try:
            code, text = self._run()
        finally:
            os.chdir(cwd)

        self.assertEqual(2, code)
        self.assertIn("coverage", text)


if __name__ == "__main__":
    unittest.main()
