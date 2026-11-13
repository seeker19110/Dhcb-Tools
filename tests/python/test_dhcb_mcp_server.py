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
    {"name": "AutoRoute", "description": "Tìm tuyến", "writesModel": True,
     "inputSchema": {"properties": {
         "startMm": {"type": "string", "jsonEncoded": True},
         "endMm": {"type": "string", "jsonEncoded": True},
         "lineStyleName": {"type": "string"},
     }}},
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
    def test_commit_metadata_is_forwarded_separately_from_config(self):
        with load() as (module, agent):
            module.call_tool("AutoNumbering", {"confirm": True, "documentId": "A",
                                               "previewToken": "token-A", "prefix": "D-"})
            self.assertEqual({"prefix": "D-", "dryRun": False}, agent.send.call_args.args[2])
            self.assertEqual("A", agent.send.call_args.kwargs["document_id"])
            self.assertEqual("token-A", agent.send.call_args.kwargs["preview_token"])

    def test_liet_ke_du_lenh_kem_query_va_chat(self) -> None:
        with load() as (module, _):
            names = [t["name"] for t in module.tool_list()]

        self.assertEqual(["HealthReport", "AutoNumbering", "AutoRoute", "ParameterExport", "query", "chat"], names)

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
    def test_confirmation_rejects_non_boolean(self) -> None:
        for value in ("false", "true", 0, 1, None, [], {}):
            with self.subTest(value=value), load() as (module, agent):
                result = module.call_tool("AutoNumbering", {"confirm": value})
                self.assertFalse(result["success"])
                agent.send.assert_not_called()

    def test_false_confirmation_preserves_preview(self) -> None:
        with load() as (module, agent):
            module.call_tool("AutoNumbering", {"confirm": False, "dryRun": False})
            self.assertTrue(agent.send.call_args[0][2]["dryRun"])

    def test_readonly_rejects_command_missing_from_cached_catalog(self) -> None:
        with load(("revit", "--read-only")) as (module, agent):
            module._load_catalog()
            agent.request.return_value = {"success": False}
            result = module.call_tool("FutureWriteCommand", {"confirm": True})
            self.assertFalse(result["success"])
            agent.send.assert_not_called()

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

    def test_truong_json_dang_chuoi_duoc_parse_lai_thanh_object(self) -> None:
        with load() as (module, agent):
            module.call_tool("AutoRoute", {"startMm": '{"x": 1, "y": 2, "z": 3}', "endMm": "[1, 2]"})

        config = agent.send.call_args[0][2]
        self.assertEqual({"x": 1, "y": 2, "z": 3}, config["startMm"])
        self.assertEqual([1, 2], config["endMm"])

    def test_truong_json_hong_thi_giu_nguyen_chuoi(self) -> None:
        with load() as (module, agent):
            module.call_tool("AutoRoute", {"startMm": "{khong-phai-json"})

        self.assertEqual("{khong-phai-json", agent.send.call_args[0][2]["startMm"])

    def test_chuoi_json_o_truong_text_khong_bi_doi_kieu(self) -> None:
        with load() as (module, agent):
            module.call_tool("AutoNumbering", {"category": '{"literal": true}'})

        self.assertEqual('{"literal": true}', agent.send.call_args[0][2]["category"])

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

    def test_tools_call_partial_or_errors_are_not_complete(self) -> None:
        for result in ({"success": True, "partialSuccess": True}, {"success": True, "errors": ["hỏng"]}):
            with self.subTest(result=result), load() as (module, agent):
                agent.send.return_value = result
                replies = self._serve(module, [{"jsonrpc": "2.0", "id": 6, "method": "tools/call",
                                                "params": {"name": "HealthReport"}}])
            self.assertTrue(replies[0]["result"]["isError"])

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


class StdinEncodingTests(unittest.TestCase):
    """Windows: stdin nối ống mang code page ANSI, MCP client gửi UTF-8 thô (audit 2026-10-01).

    Giả lập đúng tình huống đó bằng TextIOWrapper cp1252 bọc byte UTF-8: bản cũ đọc thẳng sys.stdin nên
    'Đ' (0x90) ném UnicodeDecodeError — server sập ngay câu tiếng Việt đầu tiên.
    """

    @staticmethod
    def _windows_stdin(*messages: str) -> io.TextIOWrapper:
        raw = b"".join(m.encode("utf-8") + b"\n" for m in messages)
        return io.TextIOWrapper(io.BytesIO(raw), encoding="cp1252")

    def test_tieng_viet_utf8_toi_tool_nguyen_ven(self) -> None:
        call = {"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                "params": {"name": "chat", "arguments": {"text": "Đánh số cửa tầng 3 — người, giờ"}}}
        with load() as (module, agent):
            agent.request.return_value = {"command": "AutoNumbering"}
            with mock.patch.object(module.sys, "stdin", self._windows_stdin(json.dumps(call, ensure_ascii=False))), \
                    redirect_stdout(io.StringIO()) as out:
                module.main()

        self.assertEqual("Đánh số cửa tầng 3 — người, giờ", agent.request.call_args[0][3]["text"])
        self.assertEqual(1, json.loads(out.getvalue().splitlines()[0])["id"])

    def test_dong_khong_phai_utf8_bi_bo_qua_server_van_song(self) -> None:
        raw = b"\xff\xfe{hong}\n" + json.dumps({"jsonrpc": "2.0", "id": 2, "method": "ping"}).encode() + b"\n"
        with load() as (module, _):
            with mock.patch.object(module.sys, "stdin", io.TextIOWrapper(io.BytesIO(raw), encoding="cp1252")), \
                    redirect_stdout(io.StringIO()) as out:
                module.main()

        self.assertEqual([2], [json.loads(line)["id"] for line in out.getvalue().splitlines()])


class OfflineQueryTests(unittest.TestCase):
    """query + dwgPath (AutoCAD) chạy accoreconsole trên bản sao DWG. subprocess.run được giả lập."""

    def setUp(self) -> None:
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.root = Path(folder.name)
        self.dwg = self.root / "goc.dwg"
        self.dwg.write_bytes(b"AC1032")
        self.console = self.root / "accoreconsole.exe"
        self.console.write_bytes(b"")
        self.plugin = self.root / "DhcbTools.AutoCAD.Core.dll"
        self.plugin.write_bytes(b"")
        env = mock.patch.dict("os.environ", {"DHCB_ACCORECONSOLE": str(self.console),
                                             "DHCB_ACAD_CORE_DLL": str(self.plugin)})
        env.start()
        self.addCleanup(env.stop)

    @staticmethod
    def _fake_run(result=None, stdout=b""):
        """Giả accoreconsole: đọc .scr, kiểm truy vấn, ghi result.json (nếu có) vào dòng sau DHCB_QUERY."""
        seen = {}

        def run(args, **kwargs):
            lines = Path(args[args.index("/s") + 1]).read_text(encoding="utf-8").splitlines()
            query_path, out_path = lines[lines.index("DHCB_QUERY") + 1:lines.index("DHCB_QUERY") + 3]
            seen["query"] = json.loads(Path(query_path).read_text(encoding="utf-8"))
            seen["args"] = args
            seen["script"] = lines
            if result is not None:
                Path(out_path).write_text(json.dumps(result), encoding="utf-8")
            return mock.Mock(stdout=stdout, stderr=b"")

        return run, seen

    def test_autocad_query_co_dwgPath_tra_ket_qua_tu_ban_sao(self) -> None:
        run, seen = self._fake_run({"layers": [{"name": "0"}]})
        with load(("autocad",)) as (module, agent), mock.patch.object(module.subprocess, "run", side_effect=run):
            result = module.call_tool("query", {"query": "layers", "dwgPath": str(self.dwg), "params": {"limit": 2}})
            agent.request.assert_not_called()

        self.assertEqual({"layers": [{"name": "0"}], "source": "accoreconsole"}, result)
        self.assertEqual({"query": "layers", "params": {"limit": 2}}, seen["query"])
        self.assertNotEqual(str(self.dwg), seen["args"][seen["args"].index("/i") + 1])  # chỉ mở bản sao
        self.assertIn(f'NETLOAD "{self.plugin}"', seen["script"])

    def test_ket_qua_khong_phai_object_tra_nguyen(self) -> None:
        run, _seen = self._fake_run([1, 2])
        with load(("autocad",)) as (module, _), mock.patch.object(module.subprocess, "run", side_effect=run):
            self.assertEqual([1, 2], module.call_tool("query", {"query": "stats", "dwgPath": str(self.dwg)}))

    def test_tham_so_sai_kieu_bi_tu_choi(self) -> None:
        with load(("autocad",)) as (module, _):
            for args in ({"dwgPath": 5}, {"dwgPath": str(self.dwg), "params": "x"}):
                with self.subTest(args=args):
                    self.assertFalse(module.call_tool("query", {"query": "layers", **args})["success"])

    def test_revit_bo_qua_dwgPath_va_hoi_bridge(self) -> None:
        with load(("revit",)) as (module, agent):
            module.call_tool("query", {"query": "levels", "dwgPath": str(self.dwg)})
            self.assertEqual(("revit", "POST", "/query"), agent.request.call_args[0][:3])

    def test_thieu_file_dwg(self) -> None:
        with load(("autocad",)) as (module, _):
            result = module._offline_query(str(self.root / "khong-co.dwg"), "layers", {})
        self.assertIn("Không thấy file DWG", result["summary"])

    def test_thieu_accoreconsole_hoac_dll(self) -> None:
        with load(("autocad",)) as (module, _), \
                mock.patch.dict("os.environ", {"DHCB_ACCORECONSOLE": str(self.root / "khong.exe")}):
            result = module._offline_query(str(self.dwg), "layers", {})
        self.assertIn("Không tìm thấy accoreconsole", result["summary"])

    def test_tim_host_theo_nam_trong_program_files_va_bundle(self) -> None:
        console = self.root / "pf" / "Autodesk" / "AutoCAD 2026" / "accoreconsole.exe"
        plugin = self.root / "ad" / "Autodesk" / "ApplicationPlugins" / "DhcbTools.bundle" / "Contents" / "2026" \
            / "DhcbTools.AutoCAD.Core.dll"
        for path in (console, plugin):
            path.parent.mkdir(parents=True)
            path.write_bytes(b"")
        env = {"ProgramFiles": str(self.root / "pf"), "APPDATA": str(self.root / "ad")}
        with load(("autocad",)) as (module, _), mock.patch.dict("os.environ", env):
            del module.os.environ["DHCB_ACCORECONSOLE"], module.os.environ["DHCB_ACAD_CORE_DLL"]
            self.assertEqual((str(console), str(plugin)), module._offline_host())

    def test_qua_thoi_gian(self) -> None:
        with load(("autocad",)) as (module, _), mock.patch.object(
                module.subprocess, "run", side_effect=module.subprocess.TimeoutExpired("acc", 1)):
            result = module._offline_query(str(self.dwg), "layers", {})
        self.assertIn("quá", result["summary"])

    def test_khong_co_ket_qua_thi_giai_thich_tu_output_console(self) -> None:
        cases = {
            'Command: DHCB_QUERY Unknown command "DHCB_QUERY".': "bản cũ",
            "Unable to load the assembly.": "không NETLOAD",
            "Command: QUIT": "không tạo file kết quả",
        }
        for output, expected in cases.items():
            run, _seen = self._fake_run(None, output.encode("utf-16-le"))
            with self.subTest(output=output), load(("autocad",)) as (module, _), \
                    mock.patch.object(module.subprocess, "run", side_effect=run):
                result = module._offline_query(str(self.dwg), "layers", {})
                self.assertIn(expected, result["summary"])
                self.assertIn(output.split()[0], result["consoleTail"])

    def test_mo_ta_query_rieng_tung_ung_dung(self) -> None:
        with load(("autocad",)) as (module, _):
            acad = [t for t in module.tool_list() if t["name"] == "query"][0]
        with load(("revit",)) as (module, _):
            revit = [t for t in module.tool_list() if t["name"] == "query"][0]

        self.assertIn("dwgPath", acad["inputSchema"]["properties"])
        self.assertIn("block_contents", acad["description"])
        self.assertNotIn("dwgPath", revit["inputSchema"]["properties"])
        self.assertNotIn("AutoCAD", revit["description"])


class SpeedTests(OfflineQueryTests):
    """Nâng cấp tốc độ: gộp truy vấn, nhiều file song song, nhớ kết quả, tools/call song song."""

    @staticmethod
    def _fake_console(answer, calls):
        """Giả accoreconsole cho script nhiều DHCB_QUERY: answer(query) -> kết quả, None = không ghi file."""

        def run(args, **kwargs):
            lines = Path(args[args.index("/s") + 1]).read_text(encoding="utf-8").splitlines()
            queries = []
            for index, line in enumerate(lines):
                if line != "DHCB_QUERY":
                    continue
                request = json.loads(Path(lines[index + 1]).read_text(encoding="utf-8"))
                queries.append(request["query"])
                result = answer(request["query"])
                if result is not None:
                    Path(lines[index + 2]).write_text(json.dumps(result), encoding="utf-8")
            calls.append(queries)
            return mock.Mock(stdout=b"Command: QUIT", stderr=b"")

        return run

    def test_queries_gop_vao_mot_phien_va_lan_sau_lay_tu_bo_nho(self) -> None:
        calls = []
        run = self._fake_console(lambda q: {"q": q} if q != "list" else [1], calls)
        with load(("autocad",)) as (module, _), mock.patch.object(module.subprocess, "run", side_effect=run):
            args = {"dwgPath": str(self.dwg), "queries": ["stats", {"query": "layers", "params": {"limit": 1}}, "list"]}
            first = module.call_tool("query", args)
            second = module.call_tool("query", args)
            single = module.call_tool("query", {"dwgPath": str(self.dwg), "query": "stats"})
            self.dwg.write_bytes(b"AC1032-doi-noi-dung")
            changed = module.call_tool("query", {"dwgPath": str(self.dwg), "query": "stats"})

        self.assertEqual([["stats", "layers", "list"], ["stats"]], calls)  # một phiên cho ba truy vấn; file đổi mới chạy lại
        self.assertEqual(["stats", "layers", "list"], [r["query"] for r in first["results"]])
        self.assertNotIn("cached", first["results"][0]["result"])
        self.assertTrue(second["results"][0]["result"]["cached"])
        self.assertEqual([1], second["results"][2]["result"])
        self.assertTrue(single["cached"])
        self.assertNotIn("cached", changed)

    def test_truy_van_hong_khong_duoc_nho_va_duoc_dem(self) -> None:
        calls = []
        run = self._fake_console(lambda q: {"q": q} if q == "stats" else None, calls)
        with load(("autocad",)) as (module, _), mock.patch.object(module.subprocess, "run", side_effect=run):
            args = {"dwgPath": str(self.dwg), "queries": ["stats", "hong", "hong2"]}
            first = module.call_tool("query", args)
            module.call_tool("query", args)
            all_bad = module.call_tool("query", {"dwgPath": str(self.dwg), "queries": ["hong"]})

        self.assertEqual(2, first["failed"])
        self.assertNotIn("success", first)
        self.assertEqual("accoreconsole không tạo file kết quả.", first["results"][2]["result"]["summary"])
        self.assertEqual(["hong", "hong2"], calls[1])  # chỉ chạy lại phần hỏng
        self.assertFalse(all_bad["success"])

    def test_qua_thoi_gian_thi_moi_truy_van_deu_bao_loi(self) -> None:
        with load(("autocad",)) as (module, _), mock.patch.object(
                module.subprocess, "run", side_effect=module.subprocess.TimeoutExpired("acc", 1)):
            result = module.call_tool("query", {"dwgPath": str(self.dwg), "queries": ["a", "b"]})
        self.assertEqual(2, result["failed"])

    def test_dwgPaths_doc_nhieu_file(self) -> None:
        other = self.root / "khac.dwg"
        other.write_bytes(b"AC1032")
        calls = []
        run = self._fake_console(lambda q: {"q": q}, calls)
        with load(("autocad",)) as (module, _), mock.patch.object(module.subprocess, "run", side_effect=run):
            one = module.call_tool("query", {"query": "stats", "dwgPaths": [str(self.dwg), str(other)]})
            many = module.call_tool("query", {"queries": ["stats", "layers"], "dwgPaths": [str(self.dwg), str(self.root / "khong-co.dwg")]})
            bad = module.call_tool("query", {"query": "stats", "dwgPaths": "khong-phai-mang"})

        self.assertEqual([str(self.dwg), str(other)], [f["dwgPath"] for f in one["files"]])
        self.assertEqual("stats", one["files"][1]["result"]["q"])
        self.assertEqual(2, many["failed"])
        self.assertEqual(2, len(many["files"][0]["results"]))
        self.assertFalse(bad["success"])

    def test_queries_sai_dang_bi_tu_choi(self) -> None:
        with load(("autocad",)) as (module, agent):
            for queries in ("stats", [], [5], [{"query": "a", "params": "x"}], ["a"] * 33):
                with self.subTest(queries=queries):
                    self.assertFalse(module.call_tool("query", {"queries": queries})["success"])
            agent.request.assert_not_called()

    def test_queries_qua_bridge_chay_lan_luot_giu_documentId(self) -> None:
        with load(("autocad",)) as (module, agent):
            agent.request.side_effect = [{"n": 1}, {"error": "hỏng"}]
            result = module.call_tool("query", {"queries": ["stats", "layers"], "documentId": "abc"})
            bad_id = module.call_tool("query", {"query": "stats", "documentId": ""})

        self.assertEqual({"query": "layers", "params": {}, "documentId": "abc"}, agent.request.call_args[0][3])
        self.assertEqual(1, result["failed"])
        self.assertFalse(bad_id["success"])

    def test_bo_nho_co_tran_va_bo_muc_cu(self) -> None:
        with load(("autocad",)) as (module, _):
            module.CACHE_MAX_BYTES = 100
            module._cache_put("a", {"v": "x" * 10})
            module._cache_put("a", {"v": "y" * 10})          # ghi đè: không cộng dồn byte
            module._cache_put("b", {"v": "z" * 15})
            module._cache_put("to", {"v": "x" * 60})          # quá 1/4 trần: không nhớ
            module._cache_put(None, {"v": 1})
            for name in "cdef":
                module._cache_put(name, {"v": name * 15})
            self.assertIsNone(module._cache_get("a"))         # bị đẩy ra vì lâu chưa dùng
            self.assertIsNone(module._cache_get("to"))
            self.assertIsNone(module._cache_get(None))
            self.assertEqual("f" * 15, module._cache_get("f")["v"])
            self.assertLessEqual(module._cache_bytes, 100)
            self.assertIsNone(module._cache_key(str(self.root / "khong-co.dwg"), str(self.plugin), "q", {}))

    def test_so_tien_trinh_song_song(self) -> None:
        with load(("autocad",)) as (module, _):
            with mock.patch.dict("os.environ", {"DHCB_OFFLINE_PARALLEL": "3"}):
                self.assertEqual(3, module._parallel_default())
            with mock.patch.dict("os.environ", {"DHCB_OFFLINE_PARALLEL": "nhieu"}), \
                    mock.patch.object(module.os, "cpu_count", return_value=None):
                self.assertEqual(2, module._parallel_default())

    def test_danh_muc_duoc_nho_ngan_han(self) -> None:
        with load(("autocad",)) as (module, agent):
            module.tool_list()
            module.tool_list()
            self.assertEqual(1, agent.request.call_count)
            module._catalog_memo = None
            module.tool_list()
            self.assertEqual(2, agent.request.call_count)

    def test_doc_ngam_song_song_con_lenh_bridge_giu_thu_tu(self) -> None:
        import threading
        gate = threading.Barrier(2, timeout=10)
        order = []

        def call(name, arguments):
            if name == "query":
                gate.wait()          # chỉ qua được khi HAI truy vấn đọc ngầm cùng đang chạy
                return {"ok": True}
            order.append(name)
            return {"success": True, "summary": name}

        messages = [{"jsonrpc": "2.0", "id": i, "method": "tools/call", "params": {"name": n, "arguments": a}}
                    for i, (n, a) in enumerate([("query", {"dwgPath": "a.dwg"}), ("query", {"dwgPaths": ["b.dwg"]}),
                                                ("Lenh1", {}), ("Lenh2", {}), ("Lenh3", {})])]
        with load(("autocad",)) as (module, _), mock.patch.object(module, "call_tool", side_effect=call), \
                mock.patch.object(module.sys, "stdin", io.StringIO("\n".join(json.dumps(m) for m in messages) + "\n")), \
                redirect_stdout(io.StringIO()) as out:
            module.main()

        replies = [json.loads(line) for line in out.getvalue().splitlines()]
        self.assertEqual([0, 1, 2, 3, 4], sorted(r["id"] for r in replies))
        self.assertEqual(["Lenh1", "Lenh2", "Lenh3"], order)
        self.assertTrue(all("\n" not in r["result"]["content"][0]["text"] for r in replies))  # JSON gọn, không thụt lề


class ImageContentTests(unittest.TestCase):
    """Snapshot đi thành khối MCP ``image`` để mô hình NHÌN được ảnh, không phải đọc chuỗi base64."""

    def test_anh_trong_ket_qua_tach_thanh_khoi_image(self) -> None:
        with load(("autocad",)) as (module, _):
            result = {"source": "plot", "path": "C:/t/a.png", "mimeType": "image/png", "base64": "QUJD", "width": 10}

            content = module.tool_content(result)

            self.assertEqual(["text", "image"], [c["type"] for c in content])
            self.assertEqual({"type": "image", "data": "QUJD", "mimeType": "image/png"}, content[1])
            meta = json.loads(content[0]["text"])
            self.assertIsNone(meta["base64"])
            self.assertEqual(1, meta["imageBlock"])
            self.assertEqual("C:/t/a.png", meta["path"])
            self.assertEqual("QUJD", result["base64"])  # không sửa object gốc

    def test_anh_long_trong_fallback_va_gioi_han_so_khoi(self) -> None:
        with load(("autocad",)) as (module, _):
            images = [{"mimeType": "image/png", "base64": str(i)} for i in range(6)]
            nested = {"fallbackFrom": "plot", "reasons": ["x"], "result": {"items": images}}

            content = module.tool_content(nested)

            self.assertEqual(module.MAX_IMAGE_BLOCKS, sum(c["type"] == "image" for c in content))
            items = json.loads(content[0]["text"])["result"]["items"]
            self.assertEqual("5", items[5]["base64"])  # quá trần thì giữ nguyên trong text

    def test_khong_phai_anh_thi_giu_nguyen(self) -> None:
        with load(("autocad",)) as (module, _):
            for result in ({"success": True, "summary": "xong"}, {"mimeType": "text/csv", "base64": "eA=="},
                           {"mimeType": "image/png", "base64": ""}):
                content = module.tool_content(result)
                self.assertEqual(1, len(content))
                self.assertEqual(result, json.loads(content[0]["text"]))


if __name__ == "__main__":
    unittest.main()
