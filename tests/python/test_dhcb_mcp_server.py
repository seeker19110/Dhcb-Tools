"""Test cho scripts/dhcb_mcp_server.py — MCP server (stdio, JSON-RPC 2.0) bọc HTTP Bridge.

Module đọc sys.argv ngay lúc import (cờ --read-only / --group), nên mỗi cấu hình được nạp lại bằng
importlib với argv đã thay. dhcb_agent bị thay bằng giả lập: không có Revit/AutoCAD nào phải mở.
"""

from __future__ import annotations

import importlib
import io
import json
import sys
import tempfile
import unittest
from contextlib import contextmanager, redirect_stdout, redirect_stderr
from pathlib import Path
from unittest import mock

SCRIPTS = Path(__file__).resolve().parents[2] / "scripts"
sys.path.insert(0, str(SCRIPTS))

CATALOG = {"tools": [
    {"name": "HealthReport", "description": "Báo cáo sức khoẻ", "inputSchema": {"properties": {"outputPath": {}}}},
    {"name": "AutoNumbering", "description": "Đánh số", "writesModel": True,
     "inputSchema": {"properties": {"category": {}}}},
    {"name": "ParameterExport", "description": "Xuất tham số", "inputSchema": {"properties": {}}},
]}


@contextmanager
def load(argv=("revit",), *, cache_dir=None, agent=None):
    """Nạp lại module với argv cho trước; trả (module, dhcb_agent giả)."""
    agent = agent or mock.Mock()
    agent.request.return_value = CATALOG
    agent.send.return_value = {"success": True, "summary": "xong"}
    with tempfile.TemporaryDirectory() as folder:
        with mock.patch.object(sys, "argv", ["dhcb_mcp_server.py", *argv]), \
                mock.patch.dict(sys.modules, {"dhcb_agent": agent}), \
                mock.patch.dict(sys.modules, {k: v for k, v in sys.modules.items() if k != "dhcb_mcp_server"},
                                clear=False):
            sys.modules.pop("dhcb_mcp_server", None)
            module = importlib.import_module("dhcb_mcp_server")
            module.CATALOG_CACHE = str(Path(cache_dir or folder) / "tools-cache.json")
            module.dhcb_agent = agent
            try:
                yield module, agent
            finally:
                sys.modules.pop("dhcb_mcp_server", None)


class StartupTests(unittest.TestCase):
    def test_ung_dung_khong_hop_le_thi_dung_lai_kem_huong_dan(self) -> None:
        with redirect_stderr(io.StringIO()) as err, self.assertRaises(SystemExit) as ctx:
            with load(("sketchup",)):
                pass

        self.assertEqual(2, ctx.exception.code)
        self.assertIn("revit|autocad", err.getvalue())

    def test_mac_dinh_la_revit(self) -> None:
        with load(()) as (module, _):
            self.assertEqual("revit", module.APP)

    def test_doc_co_read_only_va_group(self) -> None:
        with load(("autocad", "--read-only", "--group", "Data")) as (module, _):
            self.assertTrue(module.READ_ONLY)
            self.assertEqual("data", module.GROUP)

    def test_group_thieu_gia_tri_thi_la_none(self) -> None:
        with load(("revit", "--group")) as (module, _):
            self.assertIsNone(module.GROUP)


class LoadCatalogTests(unittest.TestCase):
    def test_bridge_song_thi_lay_moi_va_ghi_cache(self) -> None:
        with load() as (module, _):
            catalog, live = module._load_catalog()

            self.assertTrue(live)
            self.assertEqual(CATALOG, json.loads(Path(module.CATALOG_CACHE).read_text(encoding="utf-8")))

    def test_bridge_chua_mo_thi_doc_cache(self) -> None:
        with load() as (module, agent):
            module._load_catalog()  # ghi cache khi Bridge còn sống
            agent.request.return_value = {"success": False, "summary": "Không kết nối được"}

            catalog, live = module._load_catalog()

            self.assertFalse(live)
            self.assertEqual(CATALOG, catalog)

    def test_khong_co_cache_thi_tra_nguyen_phan_hoi_loi(self) -> None:
        with load() as (module, agent):
            agent.request.return_value = {"success": False}

            catalog, live = module._load_catalog()

            self.assertFalse(live)
            self.assertEqual({"success": False}, catalog)

    def test_khong_ghi_duoc_cache_thi_bo_qua(self) -> None:
        with load() as (module, _):
            with mock.patch.object(module.os, "makedirs", side_effect=OSError("chỉ đọc")):
                _catalog, live = module._load_catalog()

            self.assertTrue(live)


class ToolListTests(unittest.TestCase):
    def test_liet_ke_du_lenh_kem_query_va_chat(self) -> None:
        with load() as (module, _):
            names = [t["name"] for t in module.tool_list()]

        self.assertEqual(["HealthReport", "AutoNumbering", "ParameterExport", "query", "chat"], names)

    def test_lenh_ghi_duoc_them_tham_so_confirm(self) -> None:
        with load() as (module, _):
            tools = {t["name"]: t for t in module.tool_list()}

        self.assertIn("confirm", tools["AutoNumbering"]["inputSchema"]["properties"])
        self.assertIn("mặc định xem trước", tools["AutoNumbering"]["description"])
        self.assertNotIn("confirm", tools["HealthReport"]["inputSchema"]["properties"])

    def test_read_only_bo_lenh_ghi(self) -> None:
        with load(("revit", "--read-only")) as (module, _):
            names = [t["name"] for t in module.tool_list()]

        self.assertNotIn("AutoNumbering", names)

    def test_group_chi_lo_lenh_trong_nhom(self) -> None:
        with load(("revit", "--group", "data")) as (module, _):
            names = [t["name"] for t in module.tool_list()]

        self.assertEqual(["ParameterExport", "query"], names)

    def test_group_query_van_co_chat(self) -> None:
        with load(("revit", "--group", "query")) as (module, _):
            names = [t["name"] for t in module.tool_list()]

        self.assertEqual(["query", "chat"], names)

    def test_bridge_chua_mo_thi_ghi_chu_ngay_trong_mo_ta(self) -> None:
        with load() as (module, agent):
            module._load_catalog()  # ghi cache khi Bridge còn sống
            agent.request.return_value = {"success": False}
            tools = module.tool_list()

        self.assertIn("Revit chưa mở", tools[0]["description"])


class CallToolTests(unittest.TestCase):
    def test_query_va_chat_di_thang_sang_bridge(self) -> None:
        with load() as (module, agent):
            module.call_tool("query", {"query": "levels", "params": {"limit": 5}})
            self.assertEqual(("revit", "POST", "/query"), agent.request.call_args[0][:3])

            module.call_tool("chat", {"text": "đánh số cửa"})
            self.assertEqual(("revit", "POST", "/chat"), agent.request.call_args[0][:3])

    def test_mac_dinh_luon_ep_xem_truoc(self) -> None:
        with load() as (module, agent):
            module.call_tool("AutoNumbering", {"category": "Doors"})

        self.assertEqual({"category": "Doors", "dryRun": True}, agent.send.call_args[0][2])

    def test_confirm_true_moi_chay_that_va_khong_gui_confirm_di(self) -> None:
        with load() as (module, agent):
            module.call_tool("AutoNumbering", {"category": "Doors", "confirm": True})

        self.assertEqual({"category": "Doors", "dryRun": False}, agent.send.call_args[0][2])

    def test_timeout_seconds_duoc_tach_khoi_config(self) -> None:
        with load() as (module, agent):
            module.call_tool("SleeveAuto", {"timeoutSeconds": 300})

        self.assertNotIn("timeoutSeconds", agent.send.call_args[0][2])
        self.assertEqual(300, agent.send.call_args[1]["timeout_seconds"])

    def test_read_only_chan_lenh_ghi(self) -> None:
        with load(("revit", "--read-only")) as (module, agent):
            result = module.call_tool("AutoNumbering", {})

        self.assertFalse(result["success"])
        self.assertIn("bị chặn", result["summary"])
        agent.send.assert_not_called()

    def test_read_only_van_cho_lenh_doc(self) -> None:
        with load(("revit", "--read-only")) as (module, agent):
            module.call_tool("HealthReport", {})

        agent.send.assert_called_once()

    def test_read_only_khong_co_danh_muc_thi_tu_choi_cho_an_toan(self) -> None:
        """Bridge trục trặc làm catalog rỗng: nếu không chặn ở đây thì lệnh ghi lọt qua --read-only."""
        with load(("revit", "--read-only")) as (module, agent):
            agent.request.return_value = {"success": False}
            result = module.call_tool("AutoNumbering", {})

        self.assertFalse(result["success"])
        self.assertIn("từ chối cho an toàn", result["summary"])


class ProtocolTests(unittest.TestCase):
    def _serve(self, module, messages) -> list:
        lines = "\n".join(json.dumps(m) if isinstance(m, dict) else m for m in messages)
        with mock.patch.object(module.sys, "stdin", io.StringIO(lines + "\n")), \
                redirect_stdout(io.StringIO()) as out:
            module.main()
        return [json.loads(line) for line in out.getvalue().splitlines() if line.strip()]

    def test_initialize_tra_phien_ban_va_ten_server(self) -> None:
        with load(("autocad", "--read-only", "--group", "data")) as (module, _):
            replies = self._serve(module, [{"jsonrpc": "2.0", "id": 1, "method": "initialize"}])

        self.assertEqual(module.PROTOCOL_VERSION, replies[0]["result"]["protocolVersion"])
        self.assertEqual("dhcb-autocad-readonly-data", replies[0]["result"]["serverInfo"]["name"])

    def test_bo_qua_dong_rong_va_json_hong_va_notification(self) -> None:
        with load() as (module, _):
            replies = self._serve(module, ["", "   ", "{khong-phai-json",
                                           {"jsonrpc": "2.0", "method": "notifications/initialized"},
                                           {"jsonrpc": "2.0", "method": "tools/list"}])

        self.assertEqual([None], [r["id"] for r in replies])

    def test_tools_list(self) -> None:
        with load() as (module, _):
            replies = self._serve(module, [{"jsonrpc": "2.0", "id": 2, "method": "tools/list"}])

        self.assertIn("HealthReport", [t["name"] for t in replies[0]["result"]["tools"]])

    def test_tools_list_no_thi_tra_error_jsonrpc(self) -> None:
        with load() as (module, _):
            with mock.patch.object(module, "tool_list", side_effect=RuntimeError("nổ")):
                replies = self._serve(module, [{"jsonrpc": "2.0", "id": 3, "method": "tools/list"}])

        self.assertEqual(-32000, replies[0]["error"]["code"])

    def test_tools_call_thanh_cong(self) -> None:
        with load() as (module, _):
            replies = self._serve(module, [{"jsonrpc": "2.0", "id": 4, "method": "tools/call",
                                            "params": {"name": "HealthReport", "arguments": {}}}])

        self.assertFalse(replies[0]["result"]["isError"])
        self.assertIn("xong", replies[0]["result"]["content"][0]["text"])

    def test_tools_call_that_bai_danh_dau_is_error(self) -> None:
        with load() as (module, agent):
            agent.send.return_value = {"success": False, "summary": "hỏng"}
            replies = self._serve(module, [{"jsonrpc": "2.0", "id": 5, "method": "tools/call",
                                            "params": {"name": "HealthReport"}}])

        self.assertTrue(replies[0]["result"]["isError"])

    def test_tools_call_no_thi_tra_error_jsonrpc(self) -> None:
        with load() as (module, _):
            with mock.patch.object(module, "call_tool", side_effect=RuntimeError("nổ")):
                replies = self._serve(module, [{"jsonrpc": "2.0", "id": 6, "method": "tools/call", "params": {}}])

        self.assertEqual("nổ", replies[0]["error"]["message"])

    def test_ping(self) -> None:
        with load() as (module, _):
            replies = self._serve(module, [{"jsonrpc": "2.0", "id": 7, "method": "ping"}])

        self.assertEqual({}, replies[0]["result"])

    def test_method_la_thi_tra_32601(self) -> None:
        with load() as (module, _):
            replies = self._serve(module, [{"jsonrpc": "2.0", "id": 8, "method": "khong-co-method-nay"}])

        self.assertEqual(-32601, replies[0]["error"]["code"])

    def test_notification_la_thi_khong_tra_loi(self) -> None:
        with load() as (module, _):
            replies = self._serve(module, [{"jsonrpc": "2.0", "method": "khong-co-method-nay"}])

        self.assertEqual([], replies)


if __name__ == "__main__":
    unittest.main()
