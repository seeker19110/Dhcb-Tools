"""Regression tests for the panel's result recovery and MCP input boundary."""

from __future__ import annotations

import io
import json
import http.client
import sys
import tempfile
import unittest
import urllib.error
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "autocad-mcp-server"))
import panel_api
import server
from test_dhcb_mcp_server import load


class PanelTransportAuditTests(unittest.TestCase):
    def test_invalid_execute_shape_is_rejected_before_transport(self):
        for payload in (None, [], {"config": None}, {"config": "bad"}):
            with mock.patch.object(panel_api.LOOPBACK, "open") as send:
                result = panel_api.fetch_autocad("/execute", payload)
            self.assertFalse(result["success"])
            send.assert_not_called()

    def test_invalid_token_is_rejected_before_transport(self):
        with mock.patch.dict(panel_api.os.environ, {"DHCB_BRIDGE_TOKEN": "invalid-é-token"}), \
                mock.patch.object(panel_api.LOOPBACK, "open") as send:
            result = panel_api.fetch_autocad("/execute", {"config": {"dryRun": True}})
        self.assertFalse(result["success"])
        self.assertNotIn("é", result["error"])
        send.assert_not_called()

    def test_corrupt_token_file_does_not_crash_header_loading(self):
        with mock.patch.dict(panel_api.os.environ, {"APPDATA": str(ROOT)}, clear=True), \
                mock.patch.object(panel_api.Path, "read_text", side_effect=UnicodeDecodeError("utf-8", b"x", 0, 1, "bad")):
            self.assertEqual({}, panel_api.bridge_headers(False))

    def test_http_error_retains_result_and_recovery_id(self):
        for code in (500, 504):
            payload = {"success": True, "partialSuccess": True, "summary": "partial write",
                       "changedIds": ["AB"], "id": "job", "progressUrl": "/progress/job"}
            error = urllib.error.HTTPError("http://127.0.0.1", code, "failed", None,
                                           io.BytesIO(json.dumps(payload).encode()))
            with mock.patch.object(panel_api.LOOPBACK, "open", side_effect=error):
                result = panel_api.fetch_autocad("/execute", {"config": {"dryRun": True}})
            self.assertFalse(result["success"])
            self.assertEqual(["AB"], result["changedIds"])
            self.assertEqual("job", result["id"])
            self.assertEqual("/progress/job", result["progressUrl"])

    def test_bad_responses_and_disconnects_mark_unknown_writes(self):
        for body in (b"[]", b"bad JSON", b"\xff"):
            with mock.patch.object(panel_api.LOOPBACK, "open", return_value=io.BytesIO(body)):
                result = panel_api.fetch_autocad("/execute", {"config": {"dryRun": True}})
            self.assertFalse(result["success"])
            self.assertTrue(result["outcomeUnknown"])
            self.assertIn("KHÔNG gửi lại", result["summary"])
        for error in (ConnectionResetError("private detail"), http.client.RemoteDisconnected("private detail")):
            with mock.patch.object(panel_api.LOOPBACK, "open", side_effect=error):
                result = panel_api.fetch_autocad("/execute", {"config": {"dryRun": True}})
            self.assertTrue(result["outcomeUnknown"])
            self.assertNotIn("private detail", json.dumps(result))

    def test_http_error_with_unreadable_or_invalid_body_remains_failure(self):
        for raw in (b"[]", b"invalid", b'{}'):
            error = urllib.error.HTTPError("http://127.0.0.1", 500, "failed", None, io.BytesIO(raw))
            with mock.patch.object(panel_api.LOOPBACK, "open", side_effect=error):
                result = panel_api.fetch_autocad("/query", {"query": "layers"})
            self.assertFalse(result["success"])
            self.assertIn("HTTP 500", result["summary"])
        error = urllib.error.HTTPError("http://127.0.0.1", 504, "failed", None, None)
        with mock.patch.object(error, "read", side_effect=http.client.RemoteDisconnected()), \
                mock.patch.object(panel_api.LOOPBACK, "open", side_effect=error):
            result = panel_api.fetch_autocad("/execute", {"config": {"dryRun": True}})
        self.assertTrue(result["outcomeUnknown"])

    def test_csv_bare_filename_cannot_follow_link_outside_temp(self):
        with tempfile.TemporaryDirectory() as temp, tempfile.TemporaryDirectory() as outside:
            root = Path(temp)
            target = Path(outside) / "private.csv"
            target.write_text("keep", encoding="utf-8")
            (root / "export.csv").symlink_to(target)
            with mock.patch.object(panel_api.tempfile, "gettempdir", return_value=temp), self.assertRaises(ValueError):
                panel_api.prepare_bridge_payload("/execute", {
                    "command": "LayerExport", "config": {"outputPath": "export.csv"}})
            self.assertEqual("keep", target.read_text(encoding="utf-8"))

    def test_query_params_alias_cannot_bypass_page_validation(self):
        for params in ([1], {"limit": 999999}, {"offset": -1}):
            with self.assertRaises(ValueError):
                panel_api.prepare_bridge_payload("/query", {"query": "entities", "config": {"limit": 50}, "params": params})


class McpBoundaryAuditTests(unittest.TestCase):
    def test_progress_tool_reads_encoded_id_without_submitting_write(self):
        with mock.patch.object(server, "_fetch", return_value={"status": "done", "result": {"changedIds": ["AB"]}}) as fetch:
            result = json.loads(server.autocad_progress("job/123"))
            fetch.assert_called_once_with("/progress/job%2F123")
        self.assertEqual(["AB"], result["result"]["changedIds"])
        for value in ("", " ", "x" * 129, None):
            with mock.patch.object(server, "_fetch") as fetch:
                self.assertIn("job_id", server.autocad_progress(value))
                fetch.assert_not_called()

    def test_execute_tool_exposes_recovery_and_partial_change_ids(self):
        with mock.patch.object(server, "_fetch", return_value={
                "success": False, "outcomeUnknown": True, "summary": "KHÔNG gửi lại",
                "id": "job", "changedIds": ["AB"], "progressUrl": "/progress/job"}):
            result = server.autocad_execute("DrawingCleanup")
        self.assertIn("job_id: job", result)
        self.assertIn("autocad_progress", result)
        self.assertIn("AB", result)

    def test_malformed_jsonrpc_does_not_kill_stdio_server(self):
        messages = [[], 42, None, {"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": "bad"},
                    {"jsonrpc": "2.0", "id": 2, "method": "ping"}]
        with load() as (module, _), mock.patch.object(sys, "stdin", io.StringIO(
                "\n".join(json.dumps(m) for m in messages))), mock.patch.object(sys, "stdout", io.StringIO()) as out:
            module.main()
            replies = [json.loads(line) for line in out.getvalue().splitlines()]
        self.assertEqual(5, len(replies))
        self.assertEqual(-32600, replies[0]["error"]["code"])
        self.assertEqual(-32602, replies[3]["error"]["code"])
        self.assertEqual({}, replies[-1]["result"])

    def test_arguments_and_call_notification_cannot_start_untracked_write(self):
        with load() as (module, agent), mock.patch.object(sys, "stdin", io.StringIO(
                '\n'.join(json.dumps(m) for m in (
                    {"jsonrpc": "2.0", "method": "tools/call", "params": {"name": "AutoNumbering", "arguments": {"confirm": True}}},
                    {"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": {"arguments": [1]}},
                    {"jsonrpc": "2.0", "id": 2, "method": "tools/list", "params": []})))), \
                mock.patch.object(sys, "stdout", io.StringIO()) as out:
            module.main()
            replies = [json.loads(line) for line in out.getvalue().splitlines()]
            agent.send.assert_not_called()
        self.assertEqual([-32602, -32602], [r["error"]["code"] for r in replies])

    def test_corrupt_catalog_shape_is_ignored(self):
        for cached in ([], {"tools": [None]}, {"tools": [{"name": "Cleanup", "writesModel": "false"}]},
                       {"tools": [{"name": "Export", "inputSchema": {"properties": None}}]}):
            with load() as (module, agent):
                agent.request.return_value = {"success": False}
                Path(module.CATALOG_CACHE).write_text(json.dumps(cached), encoding="utf-8")
                tools = module.tool_list()
            self.assertEqual(["query", "chat"], [t["name"] for t in tools])
        with load() as (module, agent):
            agent.request.return_value = []
            self.assertEqual(["query", "chat"], [t["name"] for t in module.tool_list()])
        with load() as (module, agent):
            agent.request.return_value = {"tools": [{"name": "Export", "inputSchema": {"properties": {"path": None}}}]}
            self.assertEqual(["query", "chat"], [t["name"] for t in module.tool_list()])


if __name__ == "__main__":
    unittest.main()
