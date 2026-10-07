"""Phản hồi thiếu chắc chắn, kết quả một phần và redirect phải được xử lý trung thực."""

from __future__ import annotations

import io
import json
import sys
import threading
import unittest
import urllib.error
from contextlib import redirect_stdout, redirect_stderr
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
sys.path.insert(0, str(ROOT / "tools" / "autocad-mcp-server"))
import dhcb_agent
import dhcb_ai
import panel_api


class ClientQualityTests(unittest.TestCase):
    def test_bad_response_is_reported_without_crashing(self):
        for body in (b'[]', b'not json', b'\xff'):
            with mock.patch.object(dhcb_agent, "load_token", return_value="t" * 40), \
                    mock.patch.object(dhcb_agent.LOOPBACK, "open", return_value=io.BytesIO(body)):
                self.assertFalse(dhcb_agent.request("revit", "GET", "/tools")["success"])
        error = urllib.error.HTTPError("http://127.0.0.1", 500, "error", None, io.BytesIO(b'[]'))
        with mock.patch.object(dhcb_agent, "load_token", return_value="t" * 40), \
                mock.patch.object(dhcb_agent.LOOPBACK, "open", side_effect=error):
            self.assertFalse(dhcb_agent.request("revit", "GET", "/tools")["success"])

    def test_cli_progress_cancel_id_and_exit(self):
        for command in ("progress", "cancel"):
            for error in (False, True):
                result = {"status": "abandoned"}
                if error: result["error"] = "cannot cancel"
                with mock.patch.object(sys, "argv", ["agent", "revit", command, "abc/xyz"]), \
                        mock.patch.object(dhcb_agent, "request", return_value=result) as request, \
                        redirect_stdout(io.StringIO()), self.assertRaises(SystemExit) as exit_:
                    dhcb_agent.main()
                self.assertEqual(1 if error else 0, exit_.exception.code)
                self.assertEqual("/" + command + "/abc%2Fxyz", request.call_args.args[2])
                self.assertEqual("POST" if command == "cancel" else "GET", request.call_args.args[1])
            with mock.patch.object(sys, "argv", ["agent", "revit", command]), \
                    redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as exit_:
                dhcb_agent.main()
            self.assertEqual(2, exit_.exception.code)

    def test_partial_result_cli_has_nonzero_exit(self):
        for result in ({"success": True, "partialSuccess": True}, {"success": True, "errors": ["failed"]}):
            self.assertFalse(dhcb_agent.is_complete(result))
            with mock.patch.object(sys, "argv", ["agent", "revit", "exec", "AutoNumbering"]), \
                    mock.patch.object(dhcb_agent, "run", return_value=result), redirect_stdout(io.StringIO()), \
                    self.assertRaises(SystemExit) as exit_:
                dhcb_agent.main()
            self.assertEqual(1, exit_.exception.code)

    def test_429_queue_does_not_claim_auth_lock(self):
        for payload, expected in (({"error": "locked"}, "khoá 5 phút"), ({"error": "Hàng đợi đầy"}, "Hàng đợi đầy")):
            error = urllib.error.HTTPError("http://127.0.0.1", 429, "error", None, io.BytesIO(json.dumps(payload).encode()))
            with mock.patch.object(dhcb_agent, "load_token", return_value="t" * 40), \
                    mock.patch.object(dhcb_agent.LOOPBACK, "open", side_effect=error):
                self.assertIn(expected, dhcb_agent.request("revit", "GET", "/tools")["summary"])

    def test_redirect_never_forwards_bearer(self):
        received = []

        class Receiver(BaseHTTPRequestHandler):
            def do_GET(self):
                received.append(self.headers.get("Authorization"))
                self.send_response(200); self.end_headers()

            def log_message(self, *args): pass

        target = HTTPServer(("127.0.0.1", 0), Receiver)

        class Redirect(BaseHTTPRequestHandler):
            def do_GET(self):
                self.send_response(302)
                self.send_header("Location", f"http://127.0.0.1:{target.server_port}/secret")
                self.end_headers()

            def log_message(self, *args): pass

        source = HTTPServer(("127.0.0.1", 0), Redirect)
        threads = [threading.Thread(target=s.serve_forever, daemon=True) for s in (target, source)]
        for thread in threads: thread.start()
        try:
            for module in (dhcb_agent, dhcb_ai, panel_api):
                with self.subTest(module=module.__name__):
                    request = dhcb_agent.urllib.request.Request(f"http://127.0.0.1:{source.server_port}/",
                                                                headers={"Authorization": "Bearer private-token"})
                    with self.assertRaises(urllib.error.HTTPError) as error:
                        module.LOOPBACK.open(request, timeout=2)
                    self.assertEqual(302, error.exception.code)
                    error.exception.close()
            self.assertEqual([], received)
        finally:
            for server in (source, target): server.shutdown(); server.server_close()
            for thread in threads: thread.join(timeout=2)


if __name__ == "__main__":
    unittest.main()
