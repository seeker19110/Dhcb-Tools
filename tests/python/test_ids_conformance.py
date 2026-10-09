"""scripts/ids-conformance.py — đối chiếu với bộ ca IDS của buildingSMART; test không cần dotnet (giả subprocess)."""
import importlib.util
import io
import subprocess
import tempfile
import unittest
from contextlib import redirect_stdout, redirect_stderr
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("ids_conformance", ROOT / "scripts" / "ids-conformance.py")
conf = importlib.util.module_from_spec(spec)
spec.loader.exec_module(conf)


def make_suite(root: Path, cases):
    for name in cases:
        group, stem = name.split("/")
        (root / group).mkdir(parents=True, exist_ok=True)
        (root / group / (stem + ".ids")).write_text("<ids/>", encoding="utf-8")
        (root / group / (stem + ".ifc")).write_text("ISO-10303-21;", encoding="utf-8")


def fake_runner(results):
    """results: stem → mã thoát."""
    def run(cmd, **_):
        stem = Path(cmd[cmd.index("--verify-ids") + 1]).stem
        return subprocess.CompletedProcess(cmd, results[stem])
    return run


class IdsConformanceTests(unittest.TestCase):
    def test_windows_output_and_native_diagnostics_use_utf8(self):
        with mock.patch.object(conf.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)) as run:
            self.assertEqual("pass", conf.run_case("r.dll", Path("x/pass-a.ids")))
        self.assertEqual("utf-8", run.call_args.kwargs["encoding"])
        stream = mock.Mock()
        with tempfile.TemporaryDirectory() as folder, mock.patch.object(conf.sys, "stdout", stream), \
                redirect_stderr(io.StringIO()):
            self.assertEqual(1, conf.main([folder, "--runner", "r.dll"]))
        stream.reconfigure.assert_called_once_with(encoding="utf-8", errors="replace")

    def test_ket_qua_va_ma_thoat(self):
        self.assertEqual("pass", conf.expected_of(Path("pass-a.ids")))
        self.assertTrue(conf.matches("invalid", "fail"))
        self.assertTrue(conf.matches("invalid", "invalid"))
        self.assertFalse(conf.matches("pass", "fail"))
        with mock.patch.object(conf.subprocess, "run", return_value=subprocess.CompletedProcess([], 2)):
            self.assertEqual("invalid", conf.run_case("r.dll", Path("x/pass-a.ids")))

    def test_hoi_quy_do_ca_lech_da_biet_chi_nhac(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            make_suite(root, ["entity/pass-a", "entity/fail-b", "ids/invalid-c", "ids/pass-d"])
            (root / "ids" / "pass-le.ids").write_text("<ids/>", encoding="utf-8")  # không có .ifc → bỏ qua
            known = root / "known.txt"
            known.write_text("# chú thích\nentity/fail-b\nids/pass-d\n", encoding="utf-8")
            results = {"pass-a": 0, "fail-b": 0, "invalid-c": 1, "pass-d": 0}
            out, err = io.StringIO(), io.StringIO()
            with mock.patch.object(conf.subprocess, "run", side_effect=fake_runner(results)), \
                    redirect_stdout(out), redirect_stderr(err):
                self.assertEqual(0, conf.main([str(root), "--runner", "r.dll", "--known", str(known)]))
            self.assertIn("Khớp 3/4", out.getvalue())
            self.assertIn("ĐÃ KHỚP (xoá khỏi known.txt): ids/pass-d", out.getvalue())

            results["pass-a"] = 1   # ca ngoài danh sách lệch → hồi quy
            with mock.patch.object(conf.subprocess, "run", side_effect=fake_runner(results)), \
                    redirect_stdout(io.StringIO()), redirect_stderr(err):
                self.assertEqual(1, conf.main([str(root), "--runner", "r.dll", "--known", str(known)]))
            self.assertIn("HỒI QUY", err.getvalue())

    def test_thu_muc_rong_la_loi(self):
        with tempfile.TemporaryDirectory() as d, redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            self.assertEqual(1, conf.main([d, "--runner", "r.dll"]))

    def test_danh_sach_that_doc_duoc(self):
        known = conf.load_known(conf.KNOWN_GAPS)
        self.assertTrue(known)
        self.assertTrue(all("/" in case and not case.startswith("#") for case in known))


if __name__ == "__main__":
    unittest.main()
