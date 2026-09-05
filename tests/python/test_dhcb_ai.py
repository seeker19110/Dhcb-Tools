"""Test cho scripts/dhcb_ai.py — lớp AI offline chạy từ terminal.

Không gọi ra ngoài máy: pdftotext, BatchRunner và Ollama đều được thay bằng giả lập, đúng tinh thần
"mọi thứ chạy trên máy" của chính script.
"""

from __future__ import annotations

import io
import json
import subprocess
import sys
import tempfile
import unittest
import urllib.error
from contextlib import redirect_stdout, redirect_stderr
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

import dhcb_ai


class AppdataTests(unittest.TestCase):
    def test_theo_bien_appdata_neu_co(self) -> None:
        with mock.patch.dict(dhcb_ai.os.environ, {"APPDATA": "/roaming"}):
            self.assertEqual("/roaming/DHCB", dhcb_ai.appdata_dhcb().replace("\\", "/"))

    def test_khong_co_appdata_thi_dung_config_trong_home(self) -> None:
        with mock.patch.dict(dhcb_ai.os.environ, {}, clear=True), \
                mock.patch.object(dhcb_ai.os.path, "expanduser", return_value="/home/kysu"):
            self.assertEqual("/home/kysu/.config/DHCB", dhcb_ai.appdata_dhcb().replace("\\", "/"))


class PdfToTextTests(unittest.TestCase):
    def test_dung_pdftotext_neu_co(self) -> None:
        with mock.patch.object(dhcb_ai.shutil, "which", return_value="/usr/bin/pdftotext"), \
                mock.patch.object(dhcb_ai.subprocess, "run") as run:
            self.assertTrue(dhcb_ai.pdf_to_text("a.pdf", "a.txt"))

        self.assertEqual(["pdftotext", "-layout", "a.pdf", "a.txt"], run.call_args[0][0])

    def test_pdftotext_loi_thi_goi_y_duong_du_phong(self) -> None:
        with mock.patch.object(dhcb_ai.shutil, "which", return_value="/usr/bin/pdftotext"), \
                mock.patch.object(dhcb_ai.subprocess, "run",
                                  side_effect=subprocess.CalledProcessError(1, "pdftotext")), \
                redirect_stderr(io.StringIO()) as err:
            self.assertFalse(dhcb_ai.pdf_to_text("a.pdf", "a.txt"))

        self.assertIn("pip install pypdf", err.getvalue())

    def test_khong_co_pdftotext_lan_pypdf_thi_noi_ro_can_cai_gi(self) -> None:
        with mock.patch.object(dhcb_ai.shutil, "which", return_value=None), \
                mock.patch.dict(sys.modules, {"pypdf": None}), \
                redirect_stderr(io.StringIO()) as err:
            self.assertFalse(dhcb_ai.pdf_to_text("a.pdf", "a.txt"))

        self.assertIn("poppler", err.getvalue())

    def test_duong_du_phong_pypdf(self) -> None:
        page = mock.Mock()
        page.extract_text.return_value = "Tầng 2: +3.600"
        trong = mock.Mock()
        trong.extract_text.return_value = None
        pypdf = mock.Mock()
        pypdf.PdfReader.return_value = mock.Mock(pages=[page, trong])

        with tempfile.TemporaryDirectory() as folder:
            out = str(Path(folder) / "a.txt")
            with mock.patch.object(dhcb_ai.shutil, "which", return_value=None), \
                    mock.patch.dict(sys.modules, {"pypdf": pypdf}):
                self.assertTrue(dhcb_ai.pdf_to_text("a.pdf", out))

            self.assertEqual("Tầng 2: +3.600\n\n", Path(out).read_text(encoding="utf-8"))


class IsLoopbackTests(unittest.TestCase):
    def test_chap_nhan_dia_chi_loopback(self) -> None:
        for endpoint in ("http://127.0.0.1:11434", "http://localhost:11434", "https://[::1]:11434"):
            self.assertTrue(dhcb_ai.is_loopback(endpoint), endpoint)

    def test_tu_choi_dia_chi_ra_ngoai_va_ten_gia_dang(self) -> None:
        """"http://127.0.0.1.evil.example" khớp tiền tố chuỗi nhưng KHÔNG phải loopback."""
        for endpoint in ("http://127.0.0.1.evil.example", "http://api.openai.com", "ftp://127.0.0.1", "khong-phai-uri"):
            self.assertFalse(dhcb_ai.is_loopback(endpoint), endpoint)

    def test_uri_hong_khong_lam_no(self) -> None:
        with mock.patch.object(dhcb_ai.urllib.parse, "urlsplit", side_effect=ValueError("hỏng")):
            self.assertFalse(dhcb_ai.is_loopback("http://127.0.0.1"))


class CmdSpecTests(unittest.TestCase):
    def setUp(self) -> None:
        self.agent = mock.Mock()
        self.agent.send.return_value = {"success": True, "summary": "xong"}
        patcher = mock.patch.dict(sys.modules, {"dhcb_agent": self.agent})
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_doi_pdf_that_bai_thi_dung_lai(self) -> None:
        args = mock.Mock(pdf="a.pdf", out="a.txt", text=None, config_out=None)
        with mock.patch.object(dhcb_ai, "pdf_to_text", return_value=False), \
                self.assertRaises(SystemExit) as ctx:
            dhcb_ai.cmd_spec(args)

        self.assertEqual(2, ctx.exception.code)

    def test_thieu_ca_pdf_lan_text(self) -> None:
        args = mock.Mock(pdf=None, out="a.txt", text=None, config_out=None)
        with redirect_stderr(io.StringIO()) as err, self.assertRaises(SystemExit) as ctx:
            dhcb_ai.cmd_spec(args)

        self.assertEqual(2, ctx.exception.code)
        self.assertIn("Cần --pdf hoặc --text", err.getvalue())

    def test_gui_cho_revit_va_bao_thanh_cong(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            text = Path(folder) / "spec.txt"
            text.write_text("Tầng 2: +3.600", encoding="utf-8")
            args = mock.Mock(pdf=None, out=None, text=str(text), config_out=str(Path(folder) / "ra.json"))

            with redirect_stdout(io.StringIO()):
                dhcb_ai.cmd_spec(args)

        self.assertEqual("SpecToConfig", self.agent.send.call_args[0][1])

    def test_doi_pdf_thanh_cong_roi_gui(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            out = Path(folder) / "spec.txt"
            out.write_text("Tầng 2: +3.600", encoding="utf-8")
            args = mock.Mock(pdf="a.pdf", out=str(out), text=None, config_out=str(Path(folder) / "ra.json"))

            with mock.patch.object(dhcb_ai, "pdf_to_text", return_value=True), \
                    redirect_stdout(io.StringIO()) as stdout:
                dhcb_ai.cmd_spec(args)

        self.assertIn("Đã đổi PDF", stdout.getvalue())

    def test_mac_dinh_ghi_config_vao_appdata(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            text = Path(folder) / "spec.txt"
            text.write_text("Tầng 2", encoding="utf-8")
            args = mock.Mock(pdf=None, out=None, text=str(text), config_out=None)

            with mock.patch.object(dhcb_ai, "appdata_dhcb", return_value=folder), redirect_stdout(io.StringIO()):
                dhcb_ai.cmd_spec(args)

        self.assertTrue(self.agent.send.call_args[0][2]["outputPath"].endswith("project-init-from-spec.json"))

    def test_khong_gui_duoc_thi_bao_mo_revit(self) -> None:
        self.agent.send.side_effect = OSError("mất kết nối")
        with tempfile.TemporaryDirectory() as folder:
            text = Path(folder) / "spec.txt"
            text.write_text("Tầng 2", encoding="utf-8")
            args = mock.Mock(pdf=None, out=None, text=str(text), config_out=str(Path(folder) / "ra.json"))

            with redirect_stderr(io.StringIO()) as err, self.assertRaises(SystemExit) as ctx:
                dhcb_ai.cmd_spec(args)

        self.assertEqual(1, ctx.exception.code)
        self.assertIn("Mở Revit", err.getvalue())

    def test_revit_tra_that_bai_thi_ma_thoat_1(self) -> None:
        self.agent.send.return_value = {"success": False, "summary": "hỏng"}
        with tempfile.TemporaryDirectory() as folder:
            text = Path(folder) / "spec.txt"
            text.write_text("Tầng 2", encoding="utf-8")
            args = mock.Mock(pdf=None, out=None, text=str(text), config_out=str(Path(folder) / "ra.json"))

            with redirect_stdout(io.StringIO()), self.assertRaises(SystemExit) as ctx:
                dhcb_ai.cmd_spec(args)

        self.assertEqual(1, ctx.exception.code)

    def test_thieu_dhcb_agent_thi_noi_ro(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            text = Path(folder) / "spec.txt"
            text.write_text("Tầng 2", encoding="utf-8")
            args = mock.Mock(pdf=None, out=None, text=str(text), config_out=str(Path(folder) / "ra.json"))

            with mock.patch.dict(sys.modules, {"dhcb_agent": None}), \
                    redirect_stderr(io.StringIO()) as err, self.assertRaises(SystemExit) as ctx:
                dhcb_ai.cmd_spec(args)

        self.assertEqual(1, ctx.exception.code)
        self.assertIn("Thiếu dhcb_agent.py", err.getvalue())


class CmdWarningsTests(unittest.TestCase):
    LOG = "\n".join([
        json.dumps({"file": "a.rvt", "success": True, "messages": ["Connector hở tại 3 chỗ"]}),
        json.dumps({"file": "b.rvt", "success": True, "messages": ["Va chạm giữa duct và dầm"]}),
        json.dumps({"file": "b.rvt", "success": False, "summary": "Không mở được file"}),
        json.dumps({"file": "c.rvt", "success": True, "errors": ["chuyện gì đó lạ"]}),
        "{khong-phai-json",
    ])

    def test_goi_batch_runner_neu_co(self) -> None:
        args = mock.Mock(job="job.json", log="logs/2026-09-01/run.jsonl")
        with mock.patch.object(dhcb_ai.shutil, "which", return_value="/bin/DhcbTools.BatchRunner"), \
                mock.patch.object(dhcb_ai.subprocess, "call", return_value=3) as call, \
                self.assertRaises(SystemExit) as ctx:
            dhcb_ai.cmd_warnings(args)

        self.assertEqual(3, ctx.exception.code)
        self.assertIn("--analyze", call.call_args[0][0])

    def test_duong_du_phong_gom_theo_nguyen_nhan(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            log = Path(folder) / "run.jsonl"
            log.write_text(self.LOG, encoding="utf-8")
            args = mock.Mock(job=None, log=str(log))

            with mock.patch.object(dhcb_ai.shutil, "which", return_value=None), \
                    redirect_stdout(io.StringIO()) as out:
                dhcb_ai.cmd_warnings(args)

        text = out.getvalue()
        self.assertIn("**Connector MEP hở**: 1 dòng, 1 file", text)
        self.assertIn("**Va chạm**: 1 dòng, 1 file", text)
        self.assertIn("**Không mở được file**: 1 dòng, 1 file", text)
        self.assertIn("**Khác**: 1 dòng, 1 file", text)

    def test_co_runner_nhung_khong_co_job_thi_van_gom_bang_python(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            log = Path(folder) / "run.jsonl"
            log.write_text(self.LOG, encoding="utf-8")
            args = mock.Mock(job=None, log=str(log))

            with mock.patch.object(dhcb_ai.shutil, "which", return_value="/bin/DhcbTools.BatchRunner"), \
                    redirect_stdout(io.StringIO()) as out:
                dhcb_ai.cmd_warnings(args)

        self.assertIn("Tóm tắt cảnh báo", out.getvalue())


class CmdOllamaCheckTests(unittest.TestCase):
    def _run(self, settings=None, urlopen=None):
        out = io.StringIO()
        with tempfile.TemporaryDirectory() as folder:
            if settings is not None:
                (Path(folder) / "ai.json").write_text(json.dumps(settings), encoding="utf-8")
            patches = [mock.patch.object(dhcb_ai, "appdata_dhcb", return_value=folder)]
            if urlopen is not None:
                patches.append(mock.patch.object(dhcb_ai.urllib.request, "urlopen", **urlopen))
            for patcher in patches:
                patcher.start()
            try:
                with redirect_stdout(out):
                    try:
                        dhcb_ai.cmd_ollama_check(None)
                        code = 0
                    except SystemExit as ex:
                        code = ex.code
            finally:
                for patcher in reversed(patches):
                    patcher.stop()
        return code, out.getvalue()

    @staticmethod
    def _tags(names):
        response = mock.MagicMock()
        response.__enter__.return_value = response
        response.read.return_value = json.dumps({"models": [{"name": n} for n in names]}).encode("utf-8")
        return {"return_value": response}

    def test_endpoint_khong_loopback_bi_tu_choi(self) -> None:
        code, out = self._run({"endpoint": "http://api.openai.com"})

        self.assertEqual(1, code)
        self.assertIn("không phải loopback", out)

    def test_khong_co_ai_json_van_dung_mac_dinh(self) -> None:
        code, out = self._run(None, self._tags([dhcb_ai.DEFAULT_MODEL]))

        self.assertEqual(0, code)
        self.assertIn(dhcb_ai.DEFAULT_MODEL, out)

    def test_ollama_chua_pull_model_nao(self) -> None:
        code, out = self._run({"model": "qwen3:8b"}, self._tags([]))

        self.assertEqual(0, code)
        self.assertIn("(chưa pull model nào)", out)
        self.assertIn("ollama pull qwen3:8b", out)

    def test_model_khac_tag_nhung_cung_ho_thi_khong_canh_bao(self) -> None:
        code, out = self._run({"model": "qwen3:8b"}, self._tags(["qwen3:14b"]))

        self.assertEqual(0, code)
        self.assertNotIn("chưa có", out)

    def test_khong_ket_noi_duoc_thi_noi_ro_khong_bat_buoc(self) -> None:
        code, out = self._run({}, {"side_effect": urllib.error.URLError("connection refused")})

        self.assertEqual(1, code)
        self.assertIn("heuristic offline", out)


class MainTests(unittest.TestCase):
    def test_dieu_huong_toi_dung_lenh_con(self) -> None:
        with mock.patch.object(sys, "argv", ["dhcb_ai.py", "ollama-check"]), \
                mock.patch.object(dhcb_ai, "cmd_ollama_check") as cmd:
            dhcb_ai.main()

        cmd.assert_called_once()

    def test_thieu_lenh_con_thi_argparse_bao_loi(self) -> None:
        with mock.patch.object(sys, "argv", ["dhcb_ai.py"]), redirect_stderr(io.StringIO()), \
                self.assertRaises(SystemExit):
            dhcb_ai.main()


if __name__ == "__main__":
    unittest.main()
