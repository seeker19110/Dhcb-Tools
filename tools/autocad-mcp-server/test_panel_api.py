"""Deterministic unit tests for the AutoCAD panel gateway."""

from __future__ import annotations

import sys
import unittest
from email.message import Message
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).parent))

import panel_api


class PayloadValidationTests(unittest.TestCase):
    def test_allows_known_query_with_object_config(self) -> None:
        panel_api.validate_proxy_payload(
            "/query", {"query": "entities", "config": {"limit": 10}}
        )

    def test_rejects_unknown_query(self) -> None:
        with self.assertRaisesRegex(ValueError, "query không hợp lệ"):
            panel_api.validate_proxy_payload("/query", {"query": "delete_all"})

    def test_allows_confirmed_real_cleanup_command(self) -> None:
        panel_api.validate_proxy_payload(
            "/execute",
            {
                "command": "DrawingCleanup",
                "config": {"purgeUnused": True, "auditErrors": True, "dryRun": False},
                "confirmation": "DELETE_UNUSED",
            },
        )

    def test_rejects_unconfirmed_real_cleanup_command(self) -> None:
        with self.assertRaisesRegex(ValueError, "DELETE_UNUSED"):
            panel_api.validate_proxy_payload(
                "/execute",
                {
                    "command": "DrawingCleanup",
                    "config": {
                        "dryRun": False,
                        "purgeUnused": True,
                        "auditErrors": True,
                    },
                },
            )

    def test_rejects_false_like_non_boolean_dry_run(self) -> None:
        for false_like in (0, "false", None):
            with self.subTest(false_like=false_like):
                with self.assertRaisesRegex(ValueError, "dryRun phải là boolean"):
                    panel_api.validate_proxy_payload(
                        "/execute",
                        {
                            "command": "DrawingCleanup",
                            "config": {
                                "dryRun": false_like,
                                "purgeUnused": True,
                                "auditErrors": True,
                            },
                        },
                    )

    def test_real_autonumber_requires_confirmation(self) -> None:
        payload = {
            "command": "AutoNumbering",
            "config": {
                "blockName": "Dau Cat",
                "attributeTag": "A",
                "prefix": "DC",
                "startNumber": 1,
                "step": 1,
                "padWidth": 0,
                "dryRun": False,
            },
        }
        with self.assertRaisesRegex(ValueError, "WRITE_AUTONUMBER"):
            panel_api.validate_proxy_payload("/execute", payload)
        payload["confirmation"] = "WRITE_AUTONUMBER"
        panel_api.validate_proxy_payload("/execute", payload)

    def test_export_path_must_be_csv_in_temp(self) -> None:
        panel_api.validate_proxy_payload(
            "/execute",
            {"command": "LayerExport", "config": {"outputPath": "layers.csv"}},
        )
        with self.assertRaisesRegex(ValueError, "thư mục tạm"):
            panel_api.validate_proxy_payload(
                "/execute",
                {"command": "LayerExport", "config": {"outputPath": "C:/Windows/system.csv"}},
            )

    def test_rejects_unknown_command(self) -> None:
        with self.assertRaisesRegex(ValueError, "command không hợp lệ"):
            panel_api.validate_proxy_payload(
                "/execute", {"command": "RunArbitraryCode", "config": {}}
            )

    def test_rejects_non_object_config(self) -> None:
        with self.assertRaisesRegex(ValueError, "config phải là JSON object"):
            panel_api.validate_proxy_payload(
                "/execute", {"command": "DrawingCleanup", "config": "unsafe"}
            )


class BridgeHeaderTests(unittest.TestCase):
    def test_uses_bridge_token_from_environment(self) -> None:
        with mock.patch.dict("os.environ", {"DHCB_BRIDGE_TOKEN": "bridge-secret"}):
            headers = panel_api.bridge_headers(True)
        self.assertEqual(headers["Content-Type"], "application/json")
        self.assertEqual(headers["Authorization"], "Bearer bridge-secret")


class OriginPolicyTests(unittest.TestCase):
    @staticmethod
    def handler_with_origin(origin: str | None) -> panel_api.Handler:
        handler = object.__new__(panel_api.Handler)
        headers = Message()
        if origin is not None:
            headers["Origin"] = origin
        handler.headers = headers
        return handler

    def test_rejects_file_panel_origin(self) -> None:
        self.assertFalse(self.handler_with_origin("null").origin_allowed())

    def test_allows_non_browser_clients(self) -> None:
        self.assertTrue(self.handler_with_origin(None).origin_allowed())

    def test_allows_same_origin_panel(self) -> None:
        self.assertTrue(
            self.handler_with_origin("http://127.0.0.1:8767").origin_allowed()
        )

    def test_rejects_public_web_origin(self) -> None:
        self.assertFalse(
            self.handler_with_origin("https://attacker.example").origin_allowed()
        )

    def test_accepts_only_current_panel_token(self) -> None:
        handler = self.handler_with_origin("http://127.0.0.1:8767")
        handler.headers["X-Panel-Token"] = panel_api.PANEL_TOKEN
        self.assertTrue(handler.token_valid())
        handler.headers.replace_header("X-Panel-Token", "wrong-token")
        self.assertFalse(handler.token_valid())


class AiHealthTests(OriginPolicyTests):
    """GET /ai/health must be a cheap stub — never invoke run_hermes."""

    def test_ai_health_is_side_effect_free(self) -> None:
        with mock.patch.object(panel_api, "run_hermes") as mock_hermes:
            handler = self.handler_with_origin(f"http://{panel_api.HOST}:{panel_api.PORT}")
            handler.path = "/ai/health"
            captured: list[dict] = []
            handler.send_json = lambda code, body: captured.append({"code": code, "body": body})  # type: ignore[method-assign]
            handler.do_GET()
            mock_hermes.assert_not_called()
            self.assertEqual(len(captured), 1)
            self.assertEqual(captured[0]["code"], 200)


class PlannerParsingTests(unittest.TestCase):
    def test_extracts_fenced_json(self) -> None:
        parsed = panel_api.extract_json('```json\n{"reply":"ok","query":null}\n```')
        self.assertEqual(parsed, {"reply": "ok", "query": None})

    def test_rejects_non_object_json(self) -> None:
        with self.assertRaisesRegex(ValueError, "JSON object"):
            panel_api.extract_json("[1, 2, 3]")


if __name__ == "__main__":
    unittest.main()
