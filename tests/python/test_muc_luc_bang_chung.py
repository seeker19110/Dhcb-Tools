"""Mục lục của docs/bang-chung-test.md phải khớp tiêu đề thật (§61) — cùng vai với DocCommandTableTests."""
import importlib.util
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("muc_luc", ROOT / "scripts" / "muc-luc-bang-chung.py")
muc_luc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(muc_luc)


class MucLucTests(unittest.TestCase):
    def test_muc_luc_trong_file_that_khop(self):
        text = (ROOT / "docs" / "bang-chung-test.md").read_text(encoding="utf-8")
        self.assertIn(muc_luc.START, text)
        self.assertEqual(muc_luc.render(text), text, "chạy: python scripts/muc-luc-bang-chung.py")

    def test_render_them_moi_va_cap_nhat(self):
        doc = "# Tiêu đề\n\nmở đầu\n\n## 1. Mục A\n\nx\n\n## 2. Mục B: có dấu, và ký hiệu (§1)\n"
        once = muc_luc.render(doc)
        self.assertIn("- §1 — [Mục A](#1-mục-a)", once)
        self.assertIn("- §2 — [Mục B: có dấu, và ký hiệu (§1)](#2-mục-b-có-dấu-và-ký-hiệu-1)", once)
        self.assertEqual(once, muc_luc.render(once))          # idempotent
        added = once + "\n## 3. Mục C\n"
        self.assertIn("- §3 — [Mục C](#3-mục-c)", muc_luc.render(added))
        self.assertNotEqual(added, muc_luc.render(added))     # lệch thì --check phải đỏ

    def test_main_check_va_cap_nhat_tren_file_tam(self):
        import tempfile, os
        d = tempfile.mkdtemp()
        f = os.path.join(d, "bc.md")
        with open(f, "w", encoding="utf-8") as h:
            h.write("# T

## 1. A
")
        self.assertEqual(1, muc_luc.main(["--check"], f))     # chưa có mục lục → lệch
        self.assertEqual(0, muc_luc.main([], f))              # cập nhật
        self.assertEqual(0, muc_luc.main(["--check"], f))     # nay khớp
        self.assertEqual(0, muc_luc.main(["--check"]))        # file thật trong repo phải khớp

    def test_slug(self):
        self.assertEqual("54-đánh-giá-sâu-lần-hai", muc_luc.slug("54. Đánh giá sâu lần hai".replace(". ", "-", 1)))
