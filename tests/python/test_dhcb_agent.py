"""Test cho scripts/dhcb_agent.py — client gửi lệnh vào Bridge của Revit/AutoCAD.

Không mở kết nối thật: mọi lối ra HTTP đều đi qua urllib.request.urlopen, nên chỉ cần thay đúng
chỗ đó là kiểm được cả đường thành công lẫn mọi mã lỗi mà kỹ sư thật sự gặp (401/429/504/mất kết nối).
"""

from __future__ import annotations

import io
import json
import sys
import unittest
import urllib.error
from contextlib import redirect_stdout, redirect_stderr
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

import dhcb_agent


class FakeResponse:
    def __init__(self, payload):
        self._body = json.dumps(payload).encode("utf-8")

    def read(self):
        return self._body

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        return False


def fake_urlopen(payload):
    return mock.patch.object(dhcb_agent.urllib.request, "urlopen", return_value=FakeResponse(payload))


def http_error(code: int, body: str):
    error = urllib.error.HTTPError("http://127.0.0.1", code, "lỗi", None, None)
    error.read = lambda: body.encode("utf-8")  # type: ignore[method-assign]
    return mock.patch.object(dhcb_agent.urllib.request, "urlopen", side_effect=error)


class BaseUrlTests(unittest.TestCase):
    def test_cong_theo_ung_dung(self) -> None:
        self.assertEqual("http://127.0.0.1:8765", dhcb_agent.base_url("revit"))
        self.assertEqual("http://127.0.0.1:8766", dhcb_agent.base_url("autocad"))


class LoadTokenTests(unittest.TestCase):
    def test_bien_moi_truong_thang_file(self) -> None:
        with mock.patch.dict(dhcb_agent.os.environ, {"DHCB_BRIDGE_TOKEN": "  tu-moi-truong  "}):
            self.assertEqual("tu-moi-truong", dhcb_agent.load_token())

    def test_doc_tu_file_trong_appdata(self) -> None:
        with mock.patch.dict(dhcb_agent.os.environ, {"APPDATA": "/khong-quan-trong"}, clear=True), \
                mock.patch("builtins.open", mock.mock_open(read_data=" token-tu-file \n")):
            self.assertEqual("token-tu-file", dhcb_agent.load_token())

    def test_khong_co_file_tra_chuoi_rong(self) -> None:
        """Không có token thì vẫn gửi request (để Bridge trả 401 nói rõ), chứ không nổ tại client."""
        with mock.patch.dict(dhcb_agent.os.environ, {}, clear=True), \
                mock.patch("builtins.open", side_effect=OSError("không có file")):
            self.assertEqual("", dhcb_agent.load_token())


class RequestTests(unittest.TestCase):
    def setUp(self) -> None:
        patcher = mock.patch.dict(dhcb_agent.os.environ, {"DHCB_BRIDGE_TOKEN": "tk"})
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_get_khong_co_body(self) -> None:
        with fake_urlopen({"tools": []}) as urlopen:
            self.assertEqual({"tools": []}, dhcb_agent.request("revit", "GET", "/tools"))

        sent = urlopen.call_args[0][0]
        self.assertIsNone(sent.data)
        self.assertEqual("Bearer tk", sent.get_header("Authorization"))

    def test_post_gui_json(self) -> None:
        with fake_urlopen({"success": True}) as urlopen:
            dhcb_agent.request("revit", "POST", "/execute", {"command": "KiemTra"})

        sent = urlopen.call_args[0][0]
        self.assertEqual({"command": "KiemTra"}, json.loads(sent.data.decode("utf-8")))
        self.assertIn("application/json", sent.get_header("Content-type"))

    def test_401_giai_thich_chuyen_token(self) -> None:
        with http_error(401, json.dumps({"error": "unauthorized"})):
            result = dhcb_agent.request("revit", "GET", "/tools")

        self.assertFalse(result["success"])
        self.assertIn("bridge-token.txt", result["summary"])

    def test_429_giai_thich_chuyen_khoa(self) -> None:
        with http_error(429, json.dumps({"error": "locked"})):
            self.assertIn("khoá 5 phút", dhcb_agent.request("revit", "GET", "/tools")["summary"])

    def test_504_noi_ro_lenh_khong_chay(self) -> None:
        with http_error(504, json.dumps({"error": "timeout"})):
            self.assertIn("không chạy", dhcb_agent.request("revit", "GET", "/tools")["summary"])

    def test_ma_loi_khac_giu_nguyen_van_body(self) -> None:
        with http_error(500, json.dumps({"error": "nổ"})):
            self.assertIn("HTTP 500", dhcb_agent.request("revit", "GET", "/tools")["summary"])

    def test_body_khong_phai_json_van_doc_duoc(self) -> None:
        with http_error(502, "<html>bad gateway</html>"):
            result = dhcb_agent.request("revit", "GET", "/tools")

        self.assertEqual("<html>bad gateway</html>", result["error"])

    def test_khong_ket_noi_duoc_goi_y_mo_phan_mem(self) -> None:
        with mock.patch.object(dhcb_agent.urllib.request, "urlopen",
                               side_effect=urllib.error.URLError("connection refused")):
            result = dhcb_agent.request("autocad", "GET", "/tools")

        self.assertFalse(result["success"])
        self.assertIn("Autocad có đang mở", result["summary"])


class SendTests(unittest.TestCase):
    def test_khong_xin_them_gio_thi_khong_gui_truong_timeout(self) -> None:
        with mock.patch.object(dhcb_agent, "request", return_value={"success": True}) as request:
            dhcb_agent.send("revit", "KiemTra", {"dryRun": True})

        self.assertNotIn("timeoutSeconds", request.call_args[0][3])
        self.assertEqual(35, request.call_args[1]["timeout"])

    def test_xin_them_gio_thi_client_cho_lau_hon_server(self) -> None:
        with mock.patch.object(dhcb_agent, "request", return_value={"success": True}) as request:
            dhcb_agent.send("revit", "SleeveAuto", {}, timeout_seconds=120)

        self.assertEqual(120, request.call_args[0][3]["timeoutSeconds"])
        self.assertEqual(130, request.call_args[1]["timeout"])


class SendBackgroundTests(unittest.TestCase):
    def test_khong_nhan_duoc_id_tra_nguyen_loi(self) -> None:
        with mock.patch.object(dhcb_agent, "request", return_value={"success": False, "summary": "401"}):
            self.assertEqual({"success": False, "summary": "401"},
                             dhcb_agent.send_background("revit", "KiemTra", {}))

    def test_cho_toi_khi_xong_roi_tra_ket_qua(self) -> None:
        ticks = []
        responses = [
            {"id": "job1"},
            {"status": "running", "elapsedMs": 1500},
            {"status": "done", "result": {"success": True, "summary": "xong"}},
        ]
        with mock.patch.object(dhcb_agent, "request", side_effect=responses), \
                mock.patch.object(dhcb_agent.time, "sleep"):
            result = dhcb_agent.send_background("revit", "KiemTra", {}, on_tick=ticks.append)

        self.assertEqual({"success": True, "summary": "xong"}, result)
        self.assertEqual([1500], ticks)

    def test_job_loi_tra_summary_cua_job(self) -> None:
        with mock.patch.object(dhcb_agent, "request",
                               side_effect=[{"id": "job1"}, {"status": "error", "error": "nổ trong Revit"}]):
            result = dhcb_agent.send_background("revit", "KiemTra", {})

        self.assertEqual({"success": False, "summary": "nổ trong Revit"}, result)

    def test_progress_404_tra_nguyen_phan_hoi(self) -> None:
        with mock.patch.object(dhcb_agent, "request", side_effect=[{"id": "job1"}, {"error": "404"}]):
            self.assertEqual({"error": "404"}, dhcb_agent.send_background("revit", "KiemTra", {}))

    def test_cho_qua_han_noi_ro_lenh_van_dang_chay(self) -> None:
        """Hết kiên nhẫn KHÔNG có nghĩa là lệnh dừng — thông báo phải nói rõ và đưa lại id."""
        with mock.patch.object(dhcb_agent, "request",
                               side_effect=[{"id": "job1"}, {"status": "running", "elapsedMs": 1}]), \
                mock.patch.object(dhcb_agent.time, "time", side_effect=[0, 10_000]), \
                mock.patch.object(dhcb_agent.time, "sleep"):
            result = dhcb_agent.send_background("revit", "KiemTra", {}, max_wait_seconds=1)

        self.assertFalse(result["success"])
        self.assertIn("VẪN ĐANG CHẠY", result["summary"])
        self.assertIn("/progress/job1", result["summary"])


class RunTests(unittest.TestCase):
    def test_mac_dinh_chay_dong_bo(self) -> None:
        args = mock.Mock(background=False)
        with mock.patch.object(dhcb_agent, "send", return_value={"success": True}) as send:
            dhcb_agent.run("revit", "KiemTra", {}, args)

        send.assert_called_once()

    def test_co_background_thi_chay_nen_va_in_tien_do(self) -> None:
        args = mock.Mock(background=True)
        with mock.patch.object(dhcb_agent, "send_background", return_value={"success": True}) as send_background:
            dhcb_agent.run("revit", "KiemTra", {}, args)
            on_tick = send_background.call_args[1]["on_tick"]
            with redirect_stdout(io.StringIO()) as out:
                on_tick(3200)

        self.assertIn("đang chạy 3 s", out.getvalue())


class PrintResultTests(unittest.TestCase):
    def _print(self, result) -> str:
        with redirect_stdout(io.StringIO()) as out:
            dhcb_agent.print_result(result)
        return out.getvalue()

    def test_ket_qua_day_du(self) -> None:
        text = self._print({
            "success": True,
            "summary": "xong",
            "changedIds": [1, 2, 3],
            "messages": ["một cảnh báo"],
            "errors": ["một lỗi"],
            "affectedCount": 3,
        })

        self.assertIn("✓ xong", text)
        self.assertIn("Phần tử đã đổi: 1, 2, 3", text)
        self.assertIn("• một cảnh báo", text)
        self.assertIn("! một lỗi", text)
        self.assertIn("bị ảnh hưởng: 3", text)

    def test_that_bai_hien_dau_x(self) -> None:
        self.assertIn("✗ hỏng", self._print({"success": False, "summary": "hỏng"}))

    def test_danh_sach_dai_bi_cat_va_bao_con_bao_nhieu(self) -> None:
        text = self._print({"success": True, "summary": "xong", "changedIds": list(range(1, 26))})

        self.assertIn("… (+5)", text)

    def test_khong_phai_ket_qua_lenh_thi_in_nguyen_json(self) -> None:
        self.assertIn('"levels"', self._print({"levels": ["Tầng 1"]}))


class BuildConfigTests(unittest.TestCase):
    @staticmethod
    def _args(**kwargs):
        base = dict(command="", output=None, categories=None, params=None, filter=None, input=None,
                    create_missing=False, category=None, param=None, prefix="", pad=0, start=1,
                    level=None, block=None, attr=None)
        base.update(kwargs)
        return mock.Mock(**base)

    def test_export_mac_dinh_ghi_ra_public(self) -> None:
        config = dhcb_agent.build_config(self._args(command="ParameterExport"), "revit", True)

        self.assertEqual("C:/Users/Public/dhcb_revit_export.csv", config["outputPath"])

    def test_export_lay_het_bo_loc_duoc_khai(self) -> None:
        config = dhcb_agent.build_config(
            self._args(command="LayerExport", output="C:/a.csv", categories=["Doors"],
                       params=["Mark"], filter="M-"),
            "autocad", True)

        self.assertEqual(
            {"outputPath": "C:/a.csv", "categories": ["Doors"], "parameterNames": ["Mark"], "filterNameContains": "M-"},
            config)

    def test_import_giu_co_tao_layer_thieu(self) -> None:
        config = dhcb_agent.build_config(
            self._args(command="LayerImport", input="C:/a.csv", create_missing=True), "autocad", False)

        self.assertEqual({"inputPath": "C:/a.csv", "dryRun": False, "createMissing": True}, config)

    def test_cleanup_chi_co_dry_run(self) -> None:
        self.assertEqual({"dryRun": True}, dhcb_agent.build_config(self._args(command="Cleanup"), "revit", True))

    def test_danh_so_revit_va_autocad_dung_truong_khac_nhau(self) -> None:
        revit = dhcb_agent.build_config(
            self._args(command="AutoNumbering", category="Doors", param="Mark", prefix="D-", pad=3, level="Tầng 3"),
            "revit", True)
        autocad = dhcb_agent.build_config(
            self._args(command="AutoNumber", block="TITLE", attr="MARK"), "autocad", True)

        self.assertEqual("Doors", revit["category"])
        self.assertEqual("Tầng 3", revit["levelName"])
        self.assertEqual("TITLE", autocad["blockName"])
        self.assertEqual("MARK", autocad["attributeTag"])
        self.assertNotIn("levelName", autocad)

    def test_lenh_khac_chi_truyen_dry_run(self) -> None:
        self.assertEqual({"dryRun": True}, dhcb_agent.build_config(self._args(command="HealthReport"), "revit", True))
        self.assertEqual({}, dhcb_agent.build_config(self._args(command="HealthReport"), "revit", None))


class MainTests(unittest.TestCase):
    def _run(self, argv, **patches):
        out, err = io.StringIO(), io.StringIO()
        with mock.patch.object(sys, "argv", ["dhcb_agent.py", *argv]), \
                redirect_stdout(out), redirect_stderr(err):
            stack = [mock.patch.object(dhcb_agent, name, **kwargs) for name, kwargs in patches.items()]
            for patcher in stack:
                patcher.start()
            try:
                with self.assertRaises(SystemExit) as ctx:
                    dhcb_agent.main()
            finally:
                for patcher in reversed(stack):
                    patcher.stop()
        return ctx.exception.code, out.getvalue(), err.getvalue()

    def test_tools_liet_ke_lenh_kem_dau_ghi_mo_hinh(self) -> None:
        catalog = {"tools": [
            {"name": "HealthReport", "description": "Báo cáo sức khoẻ",
             "inputSchema": {"properties": {"outputPath": {}}}},
            {"name": "AutoNumbering", "description": "Đánh số", "writesModel": True,
             "inputSchema": {"properties": {"category": {}}}},
        ]}
        code, out, _ = self._run(["revit", "tools"], request=dict(return_value=catalog))

        self.assertEqual(0, code)
        self.assertIn("○ HealthReport", out)
        self.assertIn("✎ AutoNumbering", out)
        self.assertIn("[outputPath]", out)

    def test_tools_khi_bridge_chua_mo_thi_bao_loi(self) -> None:
        code, out, _ = self._run(["revit", "tools"],
                                 request=dict(return_value={"success": False, "summary": "Không kết nối được"}))

        self.assertEqual(1, code)
        self.assertIn("Không kết nối được", out)

    def test_chat_thieu_cau_lenh(self) -> None:
        code, _, err = self._run(["revit", "chat"])

        self.assertEqual(1, code)
        self.assertIn("Cần câu lệnh tiếng Việt", err)

    def test_chat_tra_de_xuat(self) -> None:
        code, out, _ = self._run(["revit", "chat", "đánh số cửa"],
                                 request=dict(return_value={"command": "AutoNumbering"}))

        self.assertEqual(0, code)
        self.assertIn("AutoNumbering", out)

    def test_chat_khong_nhan_ra_lenh_thi_ma_thoat_1(self) -> None:
        code, _, _ = self._run(["revit", "chat", "abcxyz"], request=dict(return_value={"command": None}))

        self.assertEqual(1, code)

    def test_query_tach_cap_key_value(self) -> None:
        code, out, _ = self._run(["revit", "query", "levels", "--params", "limit=10", "co-dau-bang="],
                                 request=dict(return_value={"levels": []}))
        with mock.patch.object(sys, "argv", ["x", "revit", "query", "levels", "--params", "limit=10"]), \
                mock.patch.object(dhcb_agent, "request", return_value={"levels": []}) as request, \
                redirect_stdout(io.StringIO()):
            with self.assertRaises(SystemExit):
                dhcb_agent.main()

        self.assertEqual(0, code)
        self.assertEqual({"limit": "10"}, request.call_args[0][3]["params"])

    def test_query_loi_thi_ma_thoat_1(self) -> None:
        code, _, _ = self._run(["revit", "query", "levels"], request=dict(return_value={"error": "hỏng"}))

        self.assertEqual(1, code)

    def test_raw_thieu_json(self) -> None:
        code, _, err = self._run(["revit", "raw"])

        self.assertEqual(1, code)
        self.assertIn("Cần JSON thô", err)

    def test_raw_json_hong(self) -> None:
        code, _, err = self._run(["revit", "raw", "{khong-phai-json"])

        self.assertEqual(2, code)
        self.assertIn("không hợp lệ", err)

    def test_raw_thieu_truong_command(self) -> None:
        code, _, err = self._run(["revit", "raw", '{"config":{}}'])

        self.assertEqual(2, code)
        self.assertIn('"command"', err)

    def test_raw_config_khong_phai_object(self) -> None:
        code, _, err = self._run(["revit", "raw", '{"command":"KiemTra","config":[1,2]}'])

        self.assertEqual(2, code)
        self.assertIn("phải là object", err)

    def test_raw_chay_lenh(self) -> None:
        code, out, _ = self._run(["revit", "raw", '{"command":"KiemTra","config":{"dryRun":true}}'],
                                 run=dict(return_value={"success": True, "summary": "xong"}))

        self.assertEqual(0, code)
        self.assertIn("✓ xong", out)

    def test_exec_thieu_ten_lenh(self) -> None:
        code, _, err = self._run(["revit", "exec"])

        self.assertEqual(1, code)
        self.assertIn("Cần tên lệnh", err)

    def test_exec_config_file_khong_doc_duoc(self) -> None:
        code, _, err = self._run(["revit", "exec", "KiemTra", "--config-file", "/khong/co/file.json"])

        self.assertEqual(2, code)
        self.assertIn("Không đọc được", err)

    def test_exec_config_file_json_hong(self) -> None:
        with mock.patch("builtins.open", mock.mock_open(read_data="{khong-phai-json")):
            code, _, err = self._run(["revit", "exec", "KiemTra", "--config-file", "a.json"])

        self.assertEqual(2, code)
        self.assertIn("không phải JSON hợp lệ", err)

    def test_exec_config_inline_hong(self) -> None:
        code, _, err = self._run(["revit", "exec", "KiemTra", "--config", "{khong-phai-json"])

        self.assertEqual(2, code)
        self.assertIn("PowerShell", err)

    def test_exec_config_inline_khong_phai_object(self) -> None:
        code, _, err = self._run(["revit", "exec", "KiemTra", "--config", "[1,2]"])

        self.assertEqual(2, code)
        self.assertIn("phải là JSON object", err)

    def test_exec_config_file_khong_phai_object(self) -> None:
        with mock.patch("builtins.open", mock.mock_open(read_data="[1,2]")):
            code, _, err = self._run(["revit", "exec", "KiemTra", "--config-file", "a.json"])

        self.assertEqual(2, code)
        self.assertIn("phải là JSON object", err)

    def test_exec_gop_config_file_va_inline_va_mac_dinh_dry_run(self) -> None:
        with mock.patch("builtins.open", mock.mock_open(read_data='{"a":1,"b":2}')), \
                mock.patch.object(sys, "argv", ["x", "revit", "exec", "KiemTra", "--config-file", "a.json",
                                                "--config", '{"b":3}']), \
                mock.patch.object(dhcb_agent, "run", return_value={"success": True}) as run, \
                redirect_stdout(io.StringIO()):
            with self.assertRaises(SystemExit):
                dhcb_agent.main()

        self.assertEqual({"a": 1, "b": 3, "dryRun": True}, run.call_args[0][2])

    def test_exec_no_dry_run_ep_ghi_that(self) -> None:
        with mock.patch.object(sys, "argv", ["x", "revit", "exec", "KiemTra", "--config", '{"dryRun":true}',
                                             "--no-dry-run"]), \
                mock.patch.object(dhcb_agent, "run", return_value={"success": True}) as run, \
                redirect_stdout(io.StringIO()):
            with self.assertRaises(SystemExit):
                dhcb_agent.main()

        self.assertIs(False, run.call_args[0][2]["dryRun"])

    def test_goi_thang_ten_lenh(self) -> None:
        with mock.patch.object(sys, "argv", ["x", "autocad", "LayerExport", "--output", "C:/a.csv"]), \
                mock.patch.object(dhcb_agent, "run", return_value={"success": True, "summary": "xong"}) as run, \
                redirect_stdout(io.StringIO()):
            with self.assertRaises(SystemExit) as ctx:
                dhcb_agent.main()

        self.assertEqual(0, ctx.exception.code)
        self.assertEqual("C:/a.csv", run.call_args[0][2]["outputPath"])


if __name__ == "__main__":
    unittest.main()
