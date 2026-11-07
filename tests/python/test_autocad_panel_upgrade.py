"""AutoCAD desktop workflow boundaries: large queries, guarded writes and job recovery."""

import json
import sys
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "autocad-mcp-server"))
import panel_api
import server
from test_panel_gateway import FakeHandler, body_of
from test_dhcb_mcp_server import load


class DesktopQueryTests(unittest.TestCase):
    def test_stdio_query_forwards_optional_document_id_without_changing_params(self):
        with load(("autocad",)) as (module, agent):
            module.call_tool("query", {"query": "text", "params": {"textContains": "ống"}, "documentId": "doc-A"})
            agent.request.assert_called_once_with("autocad", "POST", "/query", {
                "query": "text", "params": {"textContains": "ống"}, "documentId": "doc-A"})
        for value in (None, False, " ", "x" * 4097):
            with self.subTest(value=value), load(("autocad",)) as (module, agent):
                self.assertFalse(module.call_tool("query", {"query": "text", "documentId": value})["success"])
                agent.request.assert_not_called()

    def test_full_query_parameters_preserve_document_and_pagination(self):
        params = {"limit": 10000, "offset": 2000, "layerContains": "MEP", "textContains": "ống"}
        result = {"count": 10000, "hasMore": True, "nextOffset": 12000, "texts": []}
        with mock.patch.object(server, "_fetch", return_value=result) as fetch:
            self.assertEqual(result, json.loads(server.autocad_query("text", params=params, document_id="doc-A", as_json=True)))
        self.assertEqual({"query": "text", "config": params, "documentId": "doc-A"}, fetch.call_args.args[1])
        self.assertEqual(2000, params["offset"])
        with mock.patch.object(server, "_fetch", return_value={"geometry": []}) as fetch:
            server.autocad_query("entity_geometry", params={"handles": ["A12", "ab"]})
        self.assertEqual({"handles": ["A12", "ab"]}, fetch.call_args.args[1]["config"])
        with mock.patch.object(server, "_fetch") as fetch:
            self.assertIn("params", server.autocad_query("text", params=[]))
            self.assertIn("không hợp lệ", server.autocad_query([]))
            fetch.assert_not_called()

    def test_query_validation_accepts_host_limits_and_typed_filters(self):
        for limit in (0, 1, 10000):
            panel_api.validate_proxy_payload("/query", {"query": "text", "params": {
                "limit": limit, "offset": 0, "layerContains": "", "textContains": "điều hòa",
                "textLayer": "MEP", "entityType": "Line", "blockName": "A", "blockNameContains": "B",
                "handles": ["A", "0000abcd"], "imageWidth": 4000, "source": "thumbnail"}})

    def test_malformed_query_inputs_are_rejected_before_bridge(self):
        bad = [{"limit": value} for value in (-1, True, "50", 10001)]
        bad += [{name: value} for name in ("textContains", "layerContains", "textLayer", "entityType", "blockName", "blockNameContains")
                for value in ([], "x" * 4097)]
        bad += [{"handles": value} for value in (None, "A", ["A"] * 201, [""], ["G"], ["a" * 17], [1])]
        bad += [{"imageWidth": value} for value in (True, 199, 4001, "200")]
        bad += [{"source": value} for value in (None, [], "remote")]
        for params in bad:
            with self.subTest(params=params), self.assertRaises(ValueError):
                panel_api.validate_proxy_payload("/query", {"query": "text", "params": params})
        for field in ("documentId", "previewToken"):
            for value in (None, False, " ", "x" * 4097):
                with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                    panel_api.validate_proxy_payload("/query", {"query": "stats", field: value})
        for query in ([], {}, None):
            with self.assertRaises(ValueError):
                panel_api.validate_proxy_payload("/query", {"query": query})


class DesktopJobTests(unittest.TestCase):
    def test_background_execute_preserves_context_and_confirmation_gate(self):
        accepted = {"id": "job-A", "status": "running", "progressUrl": "/progress/job-A"}
        with mock.patch.object(server, "_fetch", return_value=accepted) as fetch:
            result = server.autocad_execute("DrawingCleanup", dry_run=False, confirm="DELETE_UNUSED",
                document_id="doc-A", preview_token="preview-A", background=True)
        self.assertEqual(accepted, json.loads(result))
        payload = fetch.call_args.args[1]
        self.assertTrue(payload["async"])
        self.assertEqual("doc-A", payload["documentId"])
        self.assertEqual("preview-A", payload["previewToken"])
        self.assertNotIn("confirmation", payload)
        with mock.patch.object(server, "_fetch") as fetch:
            self.assertIn("DELETE_UNUSED", server.autocad_execute("DrawingCleanup", dry_run=False, background=True))
            self.assertIn("boolean", server.autocad_execute("DrawingCleanup", background="true"))
            fetch.assert_not_called()
        with mock.patch.object(server, "_fetch", return_value={"success": False, "summary": "queue full"}):
            self.assertIn("queue full", server.autocad_execute("DrawingCleanup", background=True))

    def test_async_and_timeout_types_are_validated(self):
        payload = {"command": "DrawingCleanup", "config": {"dryRun": True, "purgeUnused": True, "auditErrors": True}}
        for value in (True, False):
            panel_api.validate_proxy_payload("/execute", {**payload, "async": value, "timeoutSeconds": 600})
        for value in (None, 1, "true"):
            with self.assertRaisesRegex(ValueError, "async"):
                panel_api.validate_proxy_payload("/execute", {**payload, "async": value})
        for value in (0, 601, True, "30"):
            with self.assertRaisesRegex(ValueError, "timeoutSeconds"):
                panel_api.validate_proxy_payload("/execute", {**payload, "timeoutSeconds": value})
        with self.assertRaisesRegex(ValueError, "command"):
            panel_api.validate_proxy_payload("/execute", {"command": [], "config": {}})

    def test_job_paths_are_encoded_and_bounded(self):
        self.assertEqual("/progress/job%2Fa", panel_api.job_path("progress", "job/a"))
        for operation, value in (("delete", "id"), ("progress", None), ("cancel", ""), ("cancel", " "),
                                 ("progress", "x" * 129), ("progress", "x\ny")):
            with self.assertRaises(ValueError):
                panel_api.job_path(operation, value)
        with mock.patch.object(server, "_fetch", return_value={"status": "abandoned"}) as fetch:
            self.assertEqual("abandoned", json.loads(server.autocad_cancel("job/a"))["status"])
        fetch.assert_called_once_with("/cancel/job%2Fa", {})
        with mock.patch.object(server, "_fetch") as fetch:
            self.assertIn("job_id", server.autocad_cancel(""))
            fetch.assert_not_called()

    def test_gateway_progress_and_cancel_are_token_protected(self):
        for path, method, payload in (("/progress/job%2Fa", "do_GET", None), ("/cancel/job%2Fa", "do_POST", {})):
            for token in (None, panel_api.PANEL_TOKEN):
                handler = FakeHandler(path, token=token, body=payload)
                with mock.patch.object(panel_api, "fetch_autocad", return_value={"status": "running"}) as fetch:
                    getattr(handler, method)()
                self.assertEqual(200 if token else 403, handler.status)
                if token:
                    self.assertEqual("/" + ("progress" if method == "do_GET" else "cancel") + "/job%2Fa", fetch.call_args.args[0])
                else:
                    fetch.assert_not_called()
        for path, method, payload in (("/progress/", "do_GET", None), ("/progress/x%0Ay", "do_GET", None),
                                     ("/cancel/", "do_POST", {}), ("/cancel/id?unsafe=yes", "do_POST", {})):
            handler = FakeHandler(path, token=panel_api.PANEL_TOKEN, body=payload)
            with mock.patch.object(panel_api, "fetch_autocad") as fetch:
                getattr(handler, method)()
            self.assertEqual(400, handler.status, body_of(handler))
            fetch.assert_not_called()
