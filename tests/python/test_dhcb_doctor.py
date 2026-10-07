"""Chẩn đoán chỉ đọc, không gọi execute/query và không đưa token/cấu hình vào báo cáo."""

from __future__ import annotations

import io
import json
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import dhcb_doctor


class DoctorTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.base = Path(self.folder.name)

    def test_config_missing_good_bom_and_invalid(self):
        path = self.base / "settings.json"
        self.assertEqual("warning", dhcb_doctor.inspect_config(path)[1]["status"])
        for contents, status in [('\ufeff{"bridge":{"enabled":false}}', "ok"),
                                 ("{", "error"), ("[]", "error"),
                                 ('{"bridge":null}', "error"), ('{"bridge":{"enabled":"false"}}', "error")]:
            path.write_text(contents, encoding="utf-8")
            self.assertEqual(status, dhcb_doctor.inspect_config(path)[1]["status"])
        with mock.patch.object(Path, "read_text", side_effect=PermissionError("secret")):
            self.assertNotIn("secret", json.dumps(dhcb_doctor.inspect_config(path)))

    def test_offline_missing_token_never_connects_or_leaks_secret(self):
        (self.base / "ai.json").write_text('{"private":"project-secret"}', encoding="utf-8")
        for token in ("", "short", "secret-token-" * 4):
            with mock.patch.object(dhcb_doctor.dhcb_agent, "load_token", return_value=token), \
                    mock.patch.object(dhcb_doctor.dhcb_agent, "request") as request:
                result = dhcb_doctor.diagnose("all", True, self.base)
                request.assert_not_called()
                self.assertEqual(0, result["errors"])
                self.assertNotIn("project-secret", json.dumps(result))
                self.assertNotIn("secret-token-", json.dumps(result))

    def test_health_and_catalog_only_get_and_matching_host(self):
        with mock.patch.object(dhcb_doctor.dhcb_agent, "load_token", return_value="t" * 40), \
                mock.patch.object(dhcb_doctor.dhcb_agent, "request", side_effect=[
                    {"app": "Revit", "version": "1.2"}, {"tools": [{"name": "HealthReport"}]},
                    {"app": "AutoCAD", "version": "1.2"}, {"tools": [{"name": "LayerExport"}]},
                ]) as request, mock.patch.object(dhcb_doctor.platform, "system", return_value="Windows"):
            report = dhcb_doctor.diagnose(config_dir=self.base)
            self.assertEqual(0, report["errors"])
            self.assertEqual(["/health", "/tools", "/health", "/tools"], [c.args[2] for c in request.call_args_list])
            self.assertTrue(all(c.args[1] == "GET" for c in request.call_args_list))

    def test_unavailable_or_wrong_host_never_queries_tools(self):
        for health in ({"success": False}, {"app": "wrong", "version": "1"}, {"app": "Revit"}):
            with mock.patch.object(dhcb_doctor.dhcb_agent, "load_token", return_value="t" * 40), \
                    mock.patch.object(dhcb_doctor.dhcb_agent, "request", return_value=health) as request:
                self.assertEqual(1, dhcb_doctor.diagnose("revit", config_dir=self.base)["errors"])
                self.assertEqual(1, request.call_count)

    def test_bad_catalog(self):
        for catalog in ({}, {"tools": []}, {"tools": "oops"}, {"tools": [None]}, {"tools": [{"name": ""}]}):
            with mock.patch.object(dhcb_doctor.dhcb_agent, "load_token", return_value="t" * 40), \
                    mock.patch.object(dhcb_doctor.dhcb_agent, "request", side_effect=[
                        {"app": "Revit", "version": "1"}, catalog]):
                self.assertEqual(1, dhcb_doctor.diagnose("revit", config_dir=self.base)["errors"])

    def test_disabled_or_broken_settings_does_not_connect(self):
        for config in ('{"bridge":{"enabled":false}}', '{"bridge":[]}'):
            (self.base / "settings.json").write_text(config, encoding="utf-8")
            with mock.patch.object(dhcb_doctor.dhcb_agent, "load_token", return_value="t" * 40), \
                    mock.patch.object(dhcb_doctor.dhcb_agent, "request") as request:
                dhcb_doctor.diagnose("revit", config_dir=self.base)
                request.assert_not_called()

    def test_config_dir_fallback_and_python_requirement(self):
        with mock.patch.dict(dhcb_doctor.os.environ, {"APPDATA": str(self.base)}), \
                mock.patch.object(dhcb_doctor.dhcb_agent, "load_token", return_value=""), \
                mock.patch.object(dhcb_doctor.sys, "version_info", (3, 8)):
            self.assertEqual(1, dhcb_doctor.diagnose(offline=True)["errors"])
        with mock.patch.dict(dhcb_doctor.os.environ, {}, clear=True), \
                mock.patch.object(dhcb_doctor.dhcb_agent, "load_token", return_value=""):
            self.assertEqual(0, dhcb_doctor.diagnose(offline=True)["errors"])

    def test_cli_formats_and_exit_codes(self):
        for errors in (0, 1):
            report = {"checks": [{"name": "test", "status": "ok", "detail": "check"}],
                      "errors": errors, "warnings": 0, "scope": "only read"}
            for flags in ([], ["--json"]):
                with mock.patch.object(dhcb_doctor, "diagnose", return_value=report), redirect_stdout(io.StringIO()) as output:
                    self.assertEqual(errors, dhcb_doctor.main(flags))
                    self.assertIn("test", output.getvalue())
                    if flags: self.assertEqual(report, json.loads(output.getvalue()))


if __name__ == "__main__":
    unittest.main()
