"""Test cho các tool MCP trong server.py — định dạng phản hồi và vòng đời gateway.

Bridge AutoCAD được thay bằng giả lập: không cần AutoCAD nào mở, và không tiến trình nào bị spawn.
"""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).parent))

import server


class FetchTests(unittest.TestCase):
    @staticmethod
    def _response(payload):
        response = mock.MagicMock()
        response.__enter__.return_value = response
        response.read.return_value = json.dumps(payload).encode("utf-8")
        return response

    def test_get_khi_khong_co_body(self) -> None:
        import urllib.request

        with mock.patch.object(urllib.request, "urlopen", return_value=self._response({"status": "ok"})) as urlopen:
            self.assertEqual({"status": "ok"}, server._fetch("/health"))

        self.assertIsNone(urlopen.call_args[0][0].data)

    def test_post_khi_co_body(self) -> None:
        import urllib.request

        with mock.patch.object(urllib.request, "urlopen", return_value=self._response({"ok": 1})) as urlopen:
            server._fetch("/query", {"query": "layers"})

        self.assertEqual("POST", urlopen.call_args[0][0].method)

    def test_bridge_khong_chay_thi_bao_khong_ket_noi(self) -> None:
        import urllib.error
        import urllib.request

        with mock.patch.object(urllib.request, "urlopen", side_effect=urllib.error.URLError("refused")):
            self.assertFalse(server._fetch("/health")["connected"])


class ProbePanelApiTests(unittest.TestCase):
    @staticmethod
    def _response(payload):
        response = mock.MagicMock()
        response.__enter__.return_value = response
        response.read.return_value = json.dumps(payload).encode("utf-8")
        return response

    def test_gateway_cua_minh_dang_chay(self) -> None:
        import urllib.request

        with mock.patch.object(urllib.request, "urlopen", return_value=self._response({"panelApi": "ok"})):
            self.assertEqual("ours", server._probe_panel_api())

    def test_co_server_khac_tra_loi_json_la(self) -> None:
        import urllib.request

        with mock.patch.object(urllib.request, "urlopen", return_value=self._response({"hello": "world"})):
            self.assertEqual("foreign", server._probe_panel_api())

    def test_co_server_khac_tra_ma_loi_http(self) -> None:
        import urllib.error
        import urllib.request

        error = urllib.error.HTTPError("http://127.0.0.1", 404, "not found", None, None)
        with mock.patch.object(urllib.request, "urlopen", side_effect=error):
            self.assertEqual("foreign", server._probe_panel_api())

    def test_khong_ai_nghe_thi_port_trong(self) -> None:
        import urllib.error
        import urllib.request

        with mock.patch.object(urllib.request, "urlopen", side_effect=urllib.error.URLError("refused")):
            self.assertEqual("free", server._probe_panel_api())


class StopGatewayTests(unittest.TestCase):
    def tearDown(self) -> None:
        server._gateway_process = None

    def test_chua_khoi_dong_thi_khong_lam_gi(self) -> None:
        server._gateway_process = None
        server._stop_gateway()

    def test_da_thoat_roi_thi_khong_lam_gi(self) -> None:
        proc = mock.Mock(poll=mock.Mock(return_value=0))
        server._gateway_process = proc
        server._stop_gateway()

        proc.terminate.assert_not_called()

    def test_dung_tu_te_truoc(self) -> None:
        proc = mock.Mock(poll=mock.Mock(return_value=None))
        server._gateway_process = proc
        server._stop_gateway()

        proc.terminate.assert_called_once()
        proc.kill.assert_not_called()

    def test_khong_chiu_thoat_thi_giet(self) -> None:
        proc = mock.Mock(poll=mock.Mock(return_value=None),
                         wait=mock.Mock(side_effect=subprocess.TimeoutExpired("panel_api", 3)))
        server._gateway_process = proc
        server._stop_gateway()

        proc.kill.assert_called_once()


class EnsurePanelApiTests(unittest.TestCase):
    def tearDown(self) -> None:
        server._gateway_process = None

    def test_port_bi_chiem_thi_khong_spawn_them(self) -> None:
        with mock.patch.object(server, "_probe_panel_api", return_value="foreign"), \
                mock.patch.object(server.subprocess, "Popen") as popen:
            problem = server._ensure_panel_api()

        popen.assert_not_called()
        self.assertIn("đang bị một chương trình khác chiếm", problem)

    def test_tren_windows_spawn_khong_kem_cua_so_console(self) -> None:
        """Cờ CREATE_NO_WINDOW chỉ có trên Windows; getattr(..., 0) giữ đường Linux chạy được."""
        with mock.patch.object(server.sys, "platform", "win32"), \
                mock.patch.object(server.subprocess, "CREATE_NEW_PROCESS_GROUP", 0x200, create=True), \
                mock.patch.object(server.subprocess, "CREATE_NO_WINDOW", 0x8000000, create=True), \
                mock.patch.object(server, "_probe_panel_api", side_effect=["free", "ours"]), \
                mock.patch.object(server.subprocess, "Popen") as popen, \
                mock.patch.object(server.atexit, "register"), \
                mock.patch("time.sleep"):
            self.assertIsNone(server._ensure_panel_api())

        self.assertEqual(0x200 | 0x8000000, popen.call_args[1]["creationflags"])

    def test_khong_len_duoc_trong_5_giay_thi_bao_chay_tay(self) -> None:
        with mock.patch.object(server, "_probe_panel_api", return_value="free"), \
                mock.patch.object(server.subprocess, "Popen"), \
                mock.patch.object(server.atexit, "register"), \
                mock.patch("time.sleep"):
            problem = server._ensure_panel_api()

        self.assertIn("chạy tay", problem)


class HealthToolTests(unittest.TestCase):
    def test_bridge_song(self) -> None:
        with mock.patch.object(server, "_fetch", return_value={"status": "ok", "app": "AutoCAD", "port": 8766}):
            self.assertIn("✅", server.autocad_health())

    def test_bridge_chua_mo_thi_huong_dan_netload(self) -> None:
        with mock.patch.object(server, "_fetch", return_value={"error": "refused", "connected": False}):
            text = server.autocad_health()

        self.assertIn("NETLOAD", text)

    def test_phan_hoi_la(self) -> None:
        with mock.patch.object(server, "_fetch", return_value={"status": "gì đó"}):
            self.assertIn("⚠️", server.autocad_health())


class OpenPanelToolTests(unittest.TestCase):
    def test_thieu_panel_html_thi_noi_ro(self) -> None:
        with mock.patch.object(server, "PANEL_HTML", "/khong-co/panel.html"):
            self.assertIn("Không tìm thấy panel.html", server.autocad_open_panel())

    def test_gateway_khong_len_duoc_thi_bao_loi(self) -> None:
        with mock.patch.object(server, "_ensure_panel_api", return_value="Port bị chiếm"):
            self.assertEqual("❌ Port bị chiếm", server.autocad_open_panel())

    def test_tra_directive_preview_cho_hermes(self) -> None:
        with mock.patch.object(server, "_ensure_panel_api", return_value=None):
            text = server.autocad_open_panel()

        self.assertTrue(text.startswith("::preview{file="))
        self.assertIn("panel.html", text)


class QueryToolTests(unittest.TestCase):
    def test_query_type_la_bi_tu_choi_truoc_khi_goi_bridge(self) -> None:
        with mock.patch.object(server, "_fetch") as fetch:
            text = server.autocad_query("rm -rf")

        fetch.assert_not_called()
        self.assertIn("không hợp lệ", text)

    def test_limit_ngoai_khoang_bi_chan_boi_bo_validate_cua_gateway(self) -> None:
        with mock.patch.object(server, "_fetch") as fetch:
            text = server.autocad_query("entities", limit=9999)

        fetch.assert_not_called()
        self.assertIn("limit", text)

    def test_bridge_bao_loi(self) -> None:
        with mock.patch.object(server, "_fetch", return_value={"error": "refused"}):
            self.assertIn("❌ Lỗi", server.autocad_query("stats"))

    def test_stats_dinh_dang_gon(self) -> None:
        result = {"totalEntities": 1234, "byType": [{"type": "LINE", "count": 900}]}
        with mock.patch.object(server, "_fetch", return_value=result):
            text = server.autocad_query("stats")

        self.assertIn("1,234 entities", text)
        self.assertIn("LINE", text)

    def test_drawing_info_dinh_dang_gon(self) -> None:
        result = {"filename": "a.dwg", "dwgVersion": "2018", "unitsName": "mm",
                  "layerCount": 12, "entityCount": 34}
        with mock.patch.object(server, "_fetch", return_value=result):
            text = server.autocad_query("drawing_info")

        self.assertIn("a.dwg", text)
        self.assertIn("Layers: 12", text)

    def test_layers_chi_hien_5_dong_dau_va_dem_phan_con_lai(self) -> None:
        layers = [{"name": f"L{i}", "colorIndex": i, "isOff": i == 0, "isFrozen": i == 1, "isLocked": i == 2}
                  for i in range(8)]
        with mock.patch.object(server, "_fetch", return_value={"layers": layers, "count": 8}):
            text = server.autocad_query("layers")

        self.assertIn("8 layers", text)
        self.assertIn("OFF", text)
        self.assertIn("FRZ", text)
        self.assertIn("LCK", text)
        self.assertIn("và 3 layer khác", text)
        self.assertNotIn("L7", text)

    def test_layouts_liet_ke_ten(self) -> None:
        with mock.patch.object(server, "_fetch",
                               return_value={"layouts": [{"name": "Model"}, {"name": "A1"}], "count": 2}):
            self.assertIn("Model, A1", server.autocad_query("layouts"))

    def test_loai_khac_tra_json_nguyen_van(self) -> None:
        with mock.patch.object(server, "_fetch", return_value={"count": 2, "xrefs": ["a", "b"]}):
            self.assertIn('"xrefs"', server.autocad_query("xrefs"))


class ExecuteToolTests(unittest.TestCase):
    def test_dry_run_co_ghi_chu_chua_ghi_that(self) -> None:
        result = {"success": True, "summary": "sẽ đánh số 12 block", "affectedCount": 12}
        with mock.patch.object(server, "_fetch", return_value=result):
            text = server.autocad_execute("AutoNumbering", block_name="B", attribute_tag="T")

        self.assertIn("DRY RUN", text)
        self.assertIn("Affected: 12", text)

    def test_layer_export_khong_gan_ghi_chu_dry_run(self) -> None:
        with mock.patch.object(server, "_fetch", return_value={"success": True, "affectedCount": 3}):
            self.assertNotIn("DRY RUN", server.autocad_execute("LayerExport"))

    def test_cat_bot_messages_va_errors_dai(self) -> None:
        result = {"success": False, "summary": "hỏng",
                  "messages": [f"dòng {i}" for i in range(15)],
                  "errors": [f"lỗi {i}" for i in range(7)]}
        with mock.patch.object(server, "_fetch", return_value=result):
            text = server.autocad_execute("DrawingCleanup")

        self.assertIn("và 5 dòng khác", text)
        self.assertIn("✗ lỗi 0", text)
        self.assertNotIn("lỗi 5", text)


class ExportLayersToolTests(unittest.TestCase):
    def test_duong_dan_ngoai_thu_muc_tam_bi_tu_choi(self) -> None:
        with mock.patch.object(server, "_fetch") as fetch:
            text = server.autocad_export_layers("/etc/passwd.csv")

        fetch.assert_not_called()
        self.assertIn("thư mục tạm", text)

    def test_xuat_thanh_cong_bao_duong_dan(self) -> None:
        with mock.patch.object(server, "_fetch", return_value={"success": True, "affectedCount": 12}):
            text = server.autocad_export_layers()

        self.assertIn("Xuất 12 layers", text)
        self.assertIn(str(Path(tempfile.gettempdir()) / "dhcb_layers_export.csv"), text)

    def test_xuat_that_bai_bao_summary(self) -> None:
        with mock.patch.object(server, "_fetch", return_value={"success": False, "summary": "không ghi được"}):
            self.assertIn("không ghi được", server.autocad_export_layers())

    def test_that_bai_khong_co_summary_thi_in_nguyen_phan_hoi(self) -> None:
        with mock.patch.object(server, "_fetch", return_value={"connected": False}):
            self.assertIn("connected", server.autocad_export_layers())


class BuildExecutePayloadTests(unittest.TestCase):
    def test_layer_import_giu_co_tao_layer_thieu(self) -> None:
        payload = server.build_execute_payload("LayerImport", input_path="layers.csv", create_missing=True)

        self.assertTrue(payload["config"]["createMissing"])


if __name__ == "__main__":
    unittest.main()
