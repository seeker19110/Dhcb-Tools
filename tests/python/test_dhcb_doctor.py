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

    def make_bundle(self, years=(2026,), missing=()):
        bundle = self.base / "Autodesk" / "ApplicationPlugins" / "DhcbTools.bundle"
        bundle.mkdir(parents=True, exist_ok=True)
        series = {2024: "R24.3", 2025: "R25.0", 2026: "R25.1"}
        components = []
        for year in years:
            folder = bundle / "Contents" / str(year)
            folder.mkdir(parents=True, exist_ok=True)
            for filename in ("DhcbTools.AutoCAD.dll", "DhcbTools.AutoCAD.Core.dll", "DhcbTools.Core.AutoCAD.dll",
                             "DhcbTools.Shared.Hosting.dll", "DhcbTools.Shared.Logic.dll", "Newtonsoft.Json.dll"):
                if filename not in missing: (folder / filename).write_bytes(b"fixture, never executed")
            components.append(f'<Components><RuntimeRequirements SeriesMin="{series[year]}" SeriesMax="{series[year]}"/>'
                              f'<ComponentEntry ModuleName="./Contents/{year}/DhcbTools.AutoCAD.dll"/></Components>')
        (bundle / "PackageContents.xml").write_text('<ApplicationPackage AppVersion="secret-version">'
                                                   + "".join(components) + '</ApplicationPackage>', encoding="utf-8")
        return bundle

    def make_host(self, year, tfm=None):
        host = self.base / "Programs" / "Autodesk" / ("AutoCAD " + str(year))
        host.mkdir(parents=True, exist_ok=True)
        (host / "acad.exe").write_bytes(b"fixture, never executed")
        if tfm is not None:
            (host / "acdbmgd.runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {"tfm": tfm}}), encoding="utf-8")
        return host

    def installation(self, acad_dir=None):
        with mock.patch.dict(dhcb_doctor.os.environ, {"APPDATA": str(self.base), "ProgramFiles": str(self.base / "Programs")}):
            return dhcb_doctor.inspect_autocad_installation(acad_dir)

    def test_installation_missing_manifest_and_invalid_xml_never_prints_contents(self):
        self.assertEqual("warning", self.installation()[0]["status"])
        bundle = self.make_bundle(())
        path = bundle / "PackageContents.xml"
        for xml in ('<', '<wrong AppVersion="secret"/>', '<ApplicationPackage/>'):
            path.write_text(xml, encoding="utf-8")
            report = self.installation()
            self.assertEqual("error", report[0]["status"])
            self.assertNotIn("secret", json.dumps(report))
        with mock.patch.object(dhcb_doctor.ET, "parse", side_effect=PermissionError("secret")):
            self.assertNotIn("secret", json.dumps(self.installation()))

    def test_installation_components_paths_missing_assemblies_and_host(self):
        bundle = self.make_bundle((), missing=())
        self.assertEqual("error", self.installation()[-1]["status"])
        manifest = bundle / "PackageContents.xml"
        for components, status in [('<Components/>', "warning"),
                                    ('<Components><RuntimeRequirements SeriesMin="R25.1" SeriesMax="R25.2"/></Components>', "warning"),
                                    ('<Components><RuntimeRequirements SeriesMin="R25.1" SeriesMax="R25.1"/></Components>', "error"),
                                    ('<Components><RuntimeRequirements SeriesMin="R25.1" SeriesMax="R25.1"/>'
                                     '<ComponentEntry ModuleName="../private/secret.dll"/></Components>', "error")]:
            manifest.write_text('<ApplicationPackage AppVersion="1">' + components + '</ApplicationPackage>', encoding="utf-8")
            self.assertEqual(status, self.installation()[-1]["status"])
        self.make_bundle(missing=("Newtonsoft.Json.dll",))
        report = self.installation()
        self.assertEqual("error", report[1]["status"])
        self.assertIn("Newtonsoft.Json.dll", report[1]["detail"])
        self.assertEqual("warning", report[-1]["status"])
        self.assertEqual("error", self.installation(self.base / "custom-missing")[-1]["status"])
        self.make_bundle((2026, 2026))
        self.assertIn("Component trùng", self.installation()[-1]["detail"])

    def test_installation_matches_host_runtime_for_each_supported_year(self):
        self.make_bundle((2024, 2025, 2026))
        self.make_host(2024)
        self.make_host(2025, "net8.0")
        host = self.make_host(2026, "net10.0")
        checks = self.installation()
        self.assertTrue(all(c["status"] == "ok" for c in checks), checks)
        self.assertNotIn("secret-version", json.dumps(checks))
        self.assertTrue(all(c["status"] == "ok" for c in self.installation(host)))

    def test_installation_runtime_mismatch_and_unreadable_runtime(self):
        self.make_bundle()
        host = self.make_host(2026, "net8.0")
        self.assertEqual("error", self.installation()[-1]["status"])
        self.assertIn("Update 1.2", self.installation()[-1]["detail"])
        runtime = host / "acdbmgd.runtimeconfig.json"
        for contents in ('{', '[]', '{"runtimeOptions":null}', '{"runtimeOptions":{}}'):
            runtime.write_text(contents, encoding="utf-8")
            self.assertEqual("error", self.installation()[-1]["status"])
        runtime.unlink()
        self.assertEqual("error", self.installation()[-1]["status"])

    def test_windows_installation_checks_are_offline_and_forward_custom_host(self):
        with mock.patch.object(dhcb_doctor.platform, "system", return_value="Windows"), \
                mock.patch.object(dhcb_doctor, "inspect_autocad_installation", return_value=[
                    {"name": "runtime", "status": "error", "detail": "mismatch"}]) as installation, \
                mock.patch.object(dhcb_doctor.dhcb_agent, "load_token", return_value=""), \
                mock.patch.object(dhcb_doctor.dhcb_agent, "request") as request:
            self.assertEqual(1, dhcb_doctor.diagnose("autocad", True, self.base, "custom-host")["errors"])
            installation.assert_called_once_with("custom-host")
            request.assert_not_called()
            installation.reset_mock()
            dhcb_doctor.diagnose("revit", True, self.base)
            installation.assert_not_called()
        report = {"checks": [], "errors": 0, "warnings": 0, "scope": "read only"}
        with mock.patch.object(dhcb_doctor, "diagnose", return_value=report) as diagnose, redirect_stdout(io.StringIO()):
            dhcb_doctor.main(["--app", "autocad", "--offline", "--acad-dir", "custom-host"])
            diagnose.assert_called_once_with("autocad", True, acad_dir="custom-host")

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
