"""Test cho phần cổng HTTP của panel_api.py — handler, token bridge, và đường gọi Hermes.

Bổ sung cho test_panel_api.py (vốn kiểm phần kiểm tra payload). Không mở socket: handler được dựng
trực tiếp với rfile/wfile giả, nên mọi mã trạng thái đều kiểm được mà không phụ thuộc cổng trống.
"""

from __future__ import annotations

import io
import json
import subprocess
import sys
import tempfile
import unittest
import urllib.error
from email.message import Message
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).parent))

import panel_api

GATEWAY_HOST = f"{panel_api.HOST}:{panel_api.PORT}"
GATEWAY_ORIGIN = f"http://{GATEWAY_HOST}"


class FakeHandler:
    """Handler thật, chỉ thay lớp socket bằng bộ đệm trong bộ nhớ."""

    def __new__(cls, path="/", *, origin=GATEWAY_ORIGIN, host=GATEWAY_HOST, token=None, body=None):
        handler = object.__new__(panel_api.Handler)
        headers = Message()
        if origin is not None:
            headers["Origin"] = origin
        if host is not None:
            headers["Host"] = host
        if token is not None:
            headers["X-Panel-Token"] = token
        raw = b"" if body is None else json.dumps(body).encode("utf-8")
        if body is not None:
            headers["Content-Length"] = str(len(raw))
        handler.headers = headers
        handler.path = path
        handler.rfile = io.BytesIO(raw)
        handler.wfile = io.BytesIO()
        handler.status = None
        handler.sent_headers = []
        handler.send_response = lambda code, *rest: setattr(handler, "status", code)
        handler.send_header = lambda key, value: handler.sent_headers.append((key, value))
        handler.end_headers = lambda: None
        return handler


def body_of(handler) -> dict:
    return json.loads(handler.wfile.getvalue().decode("utf-8"))


class BridgeHeadersTests(unittest.TestCase):
    def test_uu_tien_bien_moi_truong(self) -> None:
        with mock.patch.dict(panel_api.os.environ, {"DHCB_BRIDGE_TOKEN": " tk "}):
            headers = panel_api.bridge_headers(has_body=True)

        self.assertEqual("Bearer tk", headers["Authorization"])
        self.assertEqual("application/json", headers["Content-Type"])

    def test_doc_token_tu_appdata(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            token_path = Path(folder) / "DHCB" / "bridge-token.txt"
            token_path.parent.mkdir(parents=True)
            token_path.write_text(" tu-file \n", encoding="utf-8")

            with mock.patch.dict(panel_api.os.environ, {"APPDATA": folder}, clear=True):
                headers = panel_api.bridge_headers(has_body=False)

        self.assertEqual({"Authorization": "Bearer tu-file"}, headers)

    def test_khong_co_appdata_thi_doi_trong_home(self) -> None:
        with mock.patch.dict(panel_api.os.environ, {}, clear=True), \
                mock.patch.object(panel_api.Path, "home", return_value=Path("/khong-co")):
            self.assertEqual({}, panel_api.bridge_headers(has_body=False))

    def test_khong_doc_duoc_file_thi_gui_khong_kem_token(self) -> None:
        with mock.patch.dict(panel_api.os.environ, {"APPDATA": "/khong-co"}, clear=True):
            self.assertEqual({}, panel_api.bridge_headers(has_body=False))


class FetchAutocadTests(unittest.TestCase):
    @staticmethod
    def _response(payload):
        response = mock.MagicMock()
        response.__enter__.return_value = response
        response.read.return_value = json.dumps(payload).encode("utf-8")
        return response

    def test_get_khi_khong_co_body(self) -> None:
        with mock.patch.object(panel_api.urllib.request, "urlopen",
                               return_value=self._response({"status": "ok"})) as urlopen:
            self.assertEqual({"status": "ok"}, panel_api.fetch_autocad("/health"))

        self.assertEqual("GET", urlopen.call_args[0][0].method)

    def test_post_khi_co_body(self) -> None:
        with mock.patch.object(panel_api.urllib.request, "urlopen",
                               return_value=self._response({"rows": []})) as urlopen:
            panel_api.fetch_autocad("/query", {"query": "layers"})

        self.assertEqual("POST", urlopen.call_args[0][0].method)

    def test_bridge_khong_chay_thi_bao_mat_ket_noi(self) -> None:
        with mock.patch.object(panel_api.urllib.request, "urlopen",
                               side_effect=urllib.error.URLError("refused")):
            result = panel_api.fetch_autocad("/health")

        self.assertFalse(result["connected"])


class RunHermesTests(unittest.TestCase):
    def test_prompt_qua_dai_bi_chan_truoc_khi_goi(self) -> None:
        with self.assertRaises(ValueError):
            panel_api.run_hermes("x" * (panel_api.MAX_PROMPT_CHARS + 1))

    def test_prompt_di_qua_stdin_khong_qua_argv(self) -> None:
        """Dòng lệnh của tiến trình ai cũng đọc được — nội dung bản vẽ không được nằm ở đó."""
        completed = mock.Mock(returncode=0, stdout=" trả lời \n", stderr="")
        with mock.patch.object(panel_api.subprocess, "run", return_value=completed) as run:
            self.assertEqual("trả lời", panel_api.run_hermes("nội dung bản vẽ"))

        self.assertEqual("nội dung bản vẽ", run.call_args[1]["input"])
        self.assertNotIn("nội dung bản vẽ", run.call_args[0][0])
        self.assertEqual(panel_api.HERMES_TOOLSETS, run.call_args[0][0][3])

    def test_hermes_loi_thi_nem_kem_stderr(self) -> None:
        completed = mock.Mock(returncode=1, stdout="", stderr="không tìm thấy model")
        with mock.patch.object(panel_api.subprocess, "run", return_value=completed), \
                self.assertRaises(RuntimeError) as ctx:
            panel_api.run_hermes("hỏi gì đó")

        self.assertIn("không tìm thấy model", str(ctx.exception))

    def test_hermes_tra_rong_thi_nem(self) -> None:
        completed = mock.Mock(returncode=0, stdout="   ", stderr="")
        with mock.patch.object(panel_api.subprocess, "run", return_value=completed), \
                self.assertRaises(RuntimeError):
            panel_api.run_hermes("hỏi gì đó")


class ExtractJsonTests(unittest.TestCase):
    def test_json_tran(self) -> None:
        self.assertEqual({"a": 1}, panel_api.extract_json('{"a": 1}'))

    def test_json_boc_trong_hang_rao_markdown(self) -> None:
        self.assertEqual({"a": 1}, panel_api.extract_json('```json\n{"a": 1}\n```'))

    def test_khong_co_object_thi_nem(self) -> None:
        with self.assertRaises(ValueError):
            panel_api.extract_json("chỉ là câu chữ")

    def test_nhieu_object_lien_tiep_khong_doc_duoc(self) -> None:
        with self.assertRaises(ValueError):
            panel_api.extract_json('{"a":1}, {"b":2}')


class AiChatTests(unittest.TestCase):
    HEALTH = {"status": "ok"}

    def _chat(self, payload, hermes, query_result=None):
        fetch = mock.Mock(side_effect=[self.HEALTH, query_result or {"rows": []}])
        with mock.patch.object(panel_api, "fetch_autocad", fetch), \
                mock.patch.object(panel_api, "run_hermes", side_effect=hermes):
            return panel_api.ai_chat(payload)

    def test_message_rong_bi_tu_choi(self) -> None:
        self.assertFalse(panel_api.ai_chat({"message": "   "})["ok"])
        self.assertFalse(panel_api.ai_chat({"message": 5})["ok"])

    def test_history_khong_phai_danh_sach_thi_bo_qua(self) -> None:
        result = self._chat({"message": "chào", "history": "không phải list"},
                            ['{"reply":"chào bạn","query":null}'])

        self.assertEqual("chào bạn", result["reply"])

    def test_ai_khong_tra_reply_thi_dung_cau_mac_dinh(self) -> None:
        result = self._chat({"message": "chào"}, ['{"query":null}'])

        self.assertEqual("Tôi đã nhận yêu cầu.", result["reply"])

    def test_query_ngoai_danh_sach_bi_tu_choi(self) -> None:
        result = self._chat({"message": "xoá hết"}, ['{"reply":"ok","query":{"type":"rm -rf"}}'])

        self.assertFalse(result["ok"])

    def test_query_hop_le_thi_hoi_bridge_roi_tom_tat(self) -> None:
        result = self._chat({"message": "bao nhiêu layer"},
                            ['{"reply":"đang đọc","query":{"type":"layers","limit":10}}', "Có 12 layer."],
                            {"layers": ["0", "M-DUCT"]})

        self.assertEqual("Có 12 layer.", result["reply"])
        self.assertEqual("layers", result["queryType"])

    def test_limit_bi_kep_trong_khoang_1_200(self) -> None:
        fetch = mock.Mock(side_effect=[self.HEALTH, {"rows": []}])
        with mock.patch.object(panel_api, "fetch_autocad", fetch), \
                mock.patch.object(panel_api, "run_hermes",
                                  side_effect=['{"reply":"ok","query":{"type":"entities","limit":9999}}', "xong"]):
            panel_api.ai_chat({"message": "liệt kê"})

        self.assertEqual(200, fetch.call_args_list[1][0][1]["config"]["limit"])

    def test_limit_kieu_la_thi_ve_mac_dinh(self) -> None:
        fetch = mock.Mock(side_effect=[self.HEALTH, {"rows": []}])
        with mock.patch.object(panel_api, "fetch_autocad", fetch), \
                mock.patch.object(panel_api, "run_hermes",
                                  side_effect=['{"reply":"ok","query":{"type":"entities","limit":"nhiều"}}', "xong"]):
            panel_api.ai_chat({"message": "liệt kê"})

        self.assertEqual(50, fetch.call_args_list[1][0][1]["config"]["limit"])

    def test_mat_ket_noi_giua_chung_thi_noi_ro(self) -> None:
        result = self._chat({"message": "bao nhiêu layer"},
                            ['{"reply":"đang đọc","query":{"type":"layers"}}'],
                            {"connected": False, "error": "refused"})

        self.assertFalse(result["autocadConnected"])
        self.assertIn("mất kết nối", result["reply"])


class PromptTests(unittest.TestCase):
    def test_prompt_dai_bi_cat_nhung_van_trong_tran(self) -> None:
        prompt = panel_api.build_answer_prompt("hỏi", "text", {"rows": ["x" * 60_000]})

        self.assertLessEqual(len(prompt), panel_api.MAX_PROMPT_CHARS)
        self.assertIn("…[cắt bớt]", prompt)

    def test_lich_su_chi_giu_8_luot_gan_nhat_va_dung_dinh_dang(self) -> None:
        history = [{"role": "user", "content": f"câu {i}"} for i in range(12)]
        history.append({"role": 5, "content": "sai định dạng"})

        prompt = panel_api.build_planner_prompt("mới", history, {"status": "ok"})

        self.assertNotIn("câu 0", prompt)
        self.assertIn("câu 11", prompt)
        self.assertNotIn("sai định dạng", prompt)


class HandlerTests(unittest.TestCase):
    def test_log_message_co_tien_to_panel_api(self) -> None:
        handler = FakeHandler()
        handler.address_string = lambda: "127.0.0.1"  # type: ignore[method-assign]
        with mock.patch("builtins.print") as printed:
            handler.log_message("%s %s", "GET", "/health")

        self.assertIn("[panel-api] 127.0.0.1 GET /health", printed.call_args[0][0])

    def test_host_la_thi_421(self) -> None:
        handler = FakeHandler("/health", host="evil.example")
        handler.do_GET()

        self.assertEqual(421, handler.status)

    def test_origin_la_thi_403(self) -> None:
        for method in ("do_GET", "do_POST", "do_OPTIONS"):
            handler = FakeHandler("/health", origin="http://evil.example", body={})
            getattr(handler, method)()

            self.assertEqual(403, handler.status, method)

    def test_options_tra_204_kem_header_cors(self) -> None:
        handler = FakeHandler("/query")
        handler.do_OPTIONS()

        self.assertEqual(204, handler.status)
        self.assertIn(("Access-Control-Allow-Origin", GATEWAY_ORIGIN), handler.sent_headers)

    def test_options_host_la_thi_421(self) -> None:
        handler = FakeHandler("/query", host="evil.example")
        handler.do_OPTIONS()

        self.assertEqual(421, handler.status)

    def test_panel_tra_html_kem_token_va_csp(self) -> None:
        handler = FakeHandler("/panel")
        with mock.patch.object(panel_api, "PANEL_HTML",
                               mock.Mock(read_text=mock.Mock(return_value="<b>__PANEL_TOKEN__</b>"))):
            handler.do_GET()

        self.assertEqual(200, handler.status)
        self.assertIn(panel_api.PANEL_TOKEN.encode(), handler.wfile.getvalue())
        self.assertTrue(any(k == "Content-Security-Policy" for k, _ in handler.sent_headers))

    def test_khong_doc_duoc_panel_html_thi_500(self) -> None:
        handler = FakeHandler("/panel")
        with mock.patch.object(panel_api, "PANEL_HTML",
                               mock.Mock(read_text=mock.Mock(side_effect=OSError("mất file")))):
            handler.do_GET()

        self.assertEqual(500, handler.status)

    def test_alive_khong_can_token(self) -> None:
        handler = FakeHandler("/alive")
        handler.do_GET()

        self.assertEqual({"panelApi": "ok"}, body_of(handler))

    def test_get_khong_co_token_thi_403(self) -> None:
        handler = FakeHandler("/health")
        handler.do_GET()

        self.assertEqual(403, handler.status)

    def test_health_gop_trang_thai_gateway(self) -> None:
        handler = FakeHandler("/health", token=panel_api.PANEL_TOKEN)
        with mock.patch.object(panel_api, "fetch_autocad", return_value={"status": "ok"}):
            handler.do_GET()

        self.assertEqual({"status": "ok", "panelApi": "ok"}, body_of(handler))

    def test_ai_health_khong_goi_model(self) -> None:
        handler = FakeHandler("/ai/health", token=panel_api.PANEL_TOKEN)
        with mock.patch.object(panel_api, "run_hermes") as hermes:
            handler.do_GET()

        hermes.assert_not_called()
        self.assertEqual("Hermes", body_of(handler)["provider"])

    def test_get_duong_dan_la_thi_404(self) -> None:
        handler = FakeHandler("/khong-co", token=panel_api.PANEL_TOKEN)
        handler.do_GET()

        self.assertEqual(404, handler.status)

    def test_post_khong_co_token_thi_403(self) -> None:
        handler = FakeHandler("/query", body={"query": "layers"})
        handler.do_POST()

        self.assertEqual(403, handler.status)

    def test_post_query_di_qua_bridge(self) -> None:
        handler = FakeHandler("/query", token=panel_api.PANEL_TOKEN, body={"query": "layers"})
        with mock.patch.object(panel_api, "fetch_autocad", return_value={"layers": []}) as fetch:
            handler.do_POST()

        self.assertEqual(200, handler.status)
        self.assertEqual("/query", fetch.call_args[0][0])

    def test_post_ai_chat(self) -> None:
        handler = FakeHandler("/ai/chat", token=panel_api.PANEL_TOKEN, body={"message": "chào"})
        with mock.patch.object(panel_api, "ai_chat", return_value={"ok": True, "reply": "chào"}):
            handler.do_POST()

        self.assertEqual({"ok": True, "reply": "chào"}, body_of(handler))

    def test_post_duong_dan_la_thi_404(self) -> None:
        handler = FakeHandler("/khong-co", token=panel_api.PANEL_TOKEN, body={})
        handler.do_POST()

        self.assertEqual(404, handler.status)

    def test_post_host_la_thi_421(self) -> None:
        handler = FakeHandler("/query", host="evil.example", body={"query": "layers"})
        handler.do_POST()

        self.assertEqual(421, handler.status)

    def test_body_rong_hoac_qua_lon_bi_tu_choi(self) -> None:
        for length in ("0", str(panel_api.MAX_BODY_BYTES + 1)):
            handler = FakeHandler("/query", token=panel_api.PANEL_TOKEN)
            handler.headers["Content-Length"] = length
            handler.do_POST()

            self.assertEqual(400, handler.status, length)

    def test_body_khong_phai_object_bi_tu_choi(self) -> None:
        handler = FakeHandler("/query", token=panel_api.PANEL_TOKEN, body=[1, 2])
        handler.do_POST()

        self.assertEqual(400, handler.status)
        self.assertIn("JSON object", body_of(handler)["error"])

    def test_hermes_qua_gio_tra_504(self) -> None:
        handler = FakeHandler("/ai/chat", token=panel_api.PANEL_TOKEN, body={"message": "chào"})
        with mock.patch.object(panel_api, "ai_chat", side_effect=subprocess.TimeoutExpired("hermes", 150)):
            handler.do_POST()

        self.assertEqual(504, handler.status)

    def test_loi_ngoai_du_kien_tra_500_khong_kem_stack_trace(self) -> None:
        handler = FakeHandler("/ai/chat", token=panel_api.PANEL_TOKEN, body={"message": "chào"})
        with mock.patch.object(panel_api, "ai_chat", side_effect=RuntimeError("nổ")):
            handler.do_POST()

        self.assertEqual(500, handler.status)
        self.assertEqual("Lỗi gateway: nổ", body_of(handler)["error"])

    def test_khong_co_origin_van_duoc_phuc_vu(self) -> None:
        """curl/panel dạng file không gửi Origin — vẫn phục vụ, chốt chặn là Host + token."""
        handler = FakeHandler("/alive", origin=None)
        handler.do_GET()

        self.assertEqual(200, handler.status)
        self.assertNotIn("Access-Control-Allow-Origin", [k for k, _ in handler.sent_headers])


class MainTests(unittest.TestCase):
    def test_main_mo_cong_va_phuc_vu(self) -> None:
        server = mock.Mock()
        with mock.patch.object(panel_api, "ThreadingHTTPServer", return_value=server) as make_server, \
                mock.patch("builtins.print"):
            panel_api.main()

        self.assertEqual(((panel_api.HOST, panel_api.PORT), panel_api.Handler), make_server.call_args[0])
        server.serve_forever.assert_called_once()


if __name__ == "__main__":
    unittest.main()


class ValidationErrorTests(unittest.TestCase):
    """Các nhánh từ chối còn lại của validate_proxy_payload — mỗi thông báo nói rõ trường nào sai."""

    def _reject(self, path, payload, needle):
        with self.assertRaises(ValueError) as ctx:
            panel_api.validate_proxy_payload(path, payload)
        self.assertIn(needle, str(ctx.exception))

    def test_endpoint_ngoai_query_va_execute(self) -> None:
        self._reject("/chat", {}, "endpoint proxy không hợp lệ")

    def test_config_cua_query_phai_la_object(self) -> None:
        self._reject("/query", {"query": "layers", "config": [1, 2]}, "config phải là JSON object")

    def test_duong_dan_csv_sai_dinh_dang(self) -> None:
        self._reject("/execute", {"command": "LayerExport", "config": {"outputPath": "a.txt"}},
                     "phải là đường dẫn CSV")
        self._reject("/execute", {"command": "LayerExport", "config": {"outputPath": 5}},
                     "phải là đường dẫn CSV")

    def test_autonumbering_truong_chuoi_sai_kieu(self) -> None:
        self._reject("/execute", {"command": "AutoNumbering", "config": {
            "dryRun": True, "blockName": 5, "attributeTag": "MARK", "prefix": "D-",
            "startNumber": 1, "step": 1, "padWidth": 3}}, "blockName phải là chuỗi")

    def test_autonumbering_truong_so_sai_kieu(self) -> None:
        self._reject("/execute", {"command": "AutoNumbering", "config": {
            "dryRun": True, "blockName": "TITLE", "attributeTag": "MARK", "prefix": "D-",
            "startNumber": "một", "step": 1, "padWidth": 3}}, "startNumber phải là số nguyên")
