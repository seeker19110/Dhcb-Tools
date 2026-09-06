"""Kiểm `scripts/don-ket-qua.ps1` trên thư mục giả: chính sách giữ/xoá và mặc định xem trước.

Chạy pwsh thật làm tiến trình con (CI ubuntu có pwsh); không có pwsh thì bỏ qua.
"""
import os
import shutil
import subprocess
import tempfile
import unittest
from datetime import datetime, timedelta
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "don-ket-qua.ps1"
PWSH = shutil.which("pwsh") or shutil.which("powershell")


def _run(*args):
    return subprocess.run(
        [PWSH, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(SCRIPT), *args],
        capture_output=True, text=True, encoding="utf-8", errors="replace",
    )


def _make_run(root: Path, suite: str, when: datetime, big=True):
    d = root / f"{suite}-{when:%Y-%m-%d_%H-%M-%S}"
    (d / "logs").mkdir(parents=True)
    (d / "logs" / "run.jsonl").write_text("{}\n")
    if big:
        (d / "ban-chep").mkdir()
        (d / "ban-chep" / "model.rvt").write_bytes(b"x" * 4096)
    return d


@unittest.skipIf(PWSH is None, "không có pwsh/powershell")
class DonKetQuaTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="dhcb-don-"))
        now = datetime.now()
        self.new1 = _make_run(self.tmp, "mep", now)
        self.new2 = _make_run(self.tmp, "mep", now - timedelta(hours=1))
        self.mid = _make_run(self.tmp, "mep", now - timedelta(days=3))
        self.old = _make_run(self.tmp, "mep", now - timedelta(days=30))
        self.other_suite = _make_run(self.tmp, "write-plumbing", now - timedelta(days=30))
        self.keep_dir = self.tmp / "ban-giao-A"
        (self.keep_dir).mkdir()
        (self.keep_dir / "job.json").write_text("{}")

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_mac_dinh_chi_xem_truoc_khong_xoa(self):
        r = _run("-Root", str(self.tmp))
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("Xem trước", r.stdout)
        self.assertTrue((self.old).exists())
        self.assertTrue((self.mid / "ban-chep").exists())

    def test_apply_giu_moi_nhat_xoa_ban_chep_luot_giua_xoa_ca_luot_qua_cu(self):
        r = _run("-Root", str(self.tmp), "-Apply")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        # lượt mới nhất của bộ mep: nguyên vẹn kể cả bản chép
        self.assertTrue((self.new1 / "ban-chep" / "model.rvt").exists())
        # lượt thứ hai và lượt 3 ngày: mất ban-chep, còn log
        for d in (self.new2, self.mid):
            self.assertFalse((d / "ban-chep").exists(), d)
            self.assertTrue((d / "logs" / "run.jsonl").exists(), d)
        # lượt 30 ngày: xoá cả thư mục
        self.assertFalse(self.old.exists())
        # bộ khác chỉ có một lượt → là lượt mới nhất của bộ đó, giữ dù đã 30 ngày
        self.assertTrue((self.other_suite / "ban-chep").exists())
        # thư mục không theo mẫu: không đụng
        self.assertTrue((self.keep_dir / "job.json").exists())

    def test_khong_co_thu_muc_thi_ma_thoat_2(self):
        r = _run("-Root", str(self.tmp / "khong-co"))
        self.assertEqual(r.returncode, 2)


if __name__ == "__main__":
    unittest.main()
