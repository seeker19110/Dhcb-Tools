"""Chạy installer thật trên Windows, chỉ ghi vào thư mục tạm, không đăng ký uninstall."""

import itertools
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
REVIT_FILES = (
    "DhcbTools.Revit.addin", "DhcbTools.Revit.dll", "DhcbTools.Core.dll",
    "DhcbTools.Shared.Logic.dll", "DhcbTools.Shared.Hosting.dll",
)
ACAD_YEARS = tuple(range(2022, 2028))
REVIT_YEARS = tuple(range(2022, 2028))


@unittest.skipUnless(os.name == "nt", "ISCC và installer cần Windows")
class InstallerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        compiler = Path(os.environ.get(
            "DHCB_ISCC", r"C:\Program Files (x86)\Inno Setup 6\ISCC.exe"))
        if not compiler.is_file():
            raise RuntimeError(f"Thiếu compiler Inno Setup: {compiler}")
        cls.tmp = tempfile.TemporaryDirectory(prefix="dhcb-installer-")
        cls.addClassCleanup(cls.tmp.cleanup)
        cls.work = Path(cls.tmp.name)
        cls.profile = cls.work / "profile"
        stage = cls.work / "stage"
        cls.stage = stage
        cls.compiler = compiler
        for year in REVIT_YEARS:
            folder = stage / f"revit-{year}"
            folder.mkdir(parents=True)
            for name in (*REVIT_FILES, "Newtonsoft.Json.dll"):
                (folder / name).write_text(f"fixture {year}", encoding="utf-8")
        for year in ACAD_YEARS:
            folder = stage / f"autocad-{year}"
            folder.mkdir()
            (folder / "DhcbTools.AutoCAD.dll").write_text(f"fixture {year}", encoding="utf-8")
            runtime = "net48" if year <= 2024 else "net8.0" if year == 2025 else "net10.0"
            (folder / "dhcb-host-profile.json").write_text(json.dumps({"product": "autocad", "year": year, "runtime": runtime}), encoding="utf-8")
        for profile in ("2025-net10", "2026-net8"):
            folder = stage / f"autocad-{profile}"
            folder.mkdir()
            (folder / "DhcbTools.AutoCAD.dll").write_text(f"fixture {profile}", encoding="utf-8")
            year, runtime = profile.split("-")
            (folder / "dhcb-host-profile.json").write_text(json.dumps({"product": "autocad", "year": int(year), "runtime": runtime + ".0"}), encoding="utf-8")
        (stage / "batchrunner" / "scripts").mkdir(parents=True)
        (stage / "batchrunner" / "DhcbTools.BatchRunner.exe").write_text("fixture batch", encoding="utf-8")
        (stage / "batchrunner" / "scripts" / "dhcb_agent.py").write_text("# fixture", encoding="utf-8")
        for folder in ("jobs", "configs"):
            (stage / "batchrunner" / folder).mkdir()
            (stage / "batchrunner" / folder / "custom.json").write_text("fixture", encoding="utf-8")
            (stage / "batchrunner" / folder / "fresh.sample.json").write_text("new sample", encoding="utf-8")
        shutil.copyfile(ROOT / "installer" / "PackageContents.xml", stage / "PackageContents.xml")
        cls.original = ET.parse(stage / "PackageContents.xml").getroot()
        cls.acad2025 = cls.work / "fake-acad2025"
        cls.acad2025.mkdir()
        (cls.acad2025 / "acad.exe").touch()
        (cls.acad2025 / "acdbmgd.runtimeconfig.json").write_text('{"runtimeOptions":{"tfm":"net8.0"}}', encoding="utf-8")
        cls.acad = cls.work / "fake-acad"
        cls.acad.mkdir()
        (cls.acad / "acad.exe").touch()
        (cls.acad / "acdbmgd.runtimeconfig.json").write_text('{"runtimeOptions":{"tfm":"net10.0"}}', encoding="utf-8")

        cls.detected = cls.work / "detected-hosts"
        for name, executable in (("AutoCAD 2026", "acad.exe"), ("Revit 2022", "Revit.exe")):
            folder = cls.detected / name
            folder.mkdir(parents=True)
            (folder / executable).touch()

        # Chỉ chuyển các đích ghi của bộ cài vào sandbox; giữ nguyên [Code]/[InstallDelete].
        text = (ROOT / "installer" / "dhcb-tools.iss").read_text(encoding="utf-8")
        text = text.replace("[Setup]", "[Setup]\nUninstallable=no\nCreateUninstallRegKey=no")
        for constant, folder in (("userappdata", "roaming"), ("localappdata", "local"), ("group", "shortcuts")):
            text = text.replace("{" + constant + "}", str(cls.profile / folder))
        text = text.replace(r"..\LICENSE", str(ROOT / "LICENSE")).replace(r"..\NOTICE", str(ROOT / "NOTICE"))
        text = text.replace(r"{autopf}\Autodesk", str(cls.detected))
        source = cls.work / "sandbox.iss"
        cls.source = source
        source.write_text(text, encoding="utf-8-sig")
        cls.run_process([str(compiler), "/Q", "/DVersion=0.0.0-test", f"/DStageDir={stage}",
                         f"/O{cls.work}", str(source)])
        cls.setup = cls.work / "DhcbTools-Setup-0.0.0-test.exe"

    @staticmethod
    def run_process(args, expect_success=True):
        result = subprocess.run(args, capture_output=True, timeout=60)
        if expect_success and result.returncode:
            log = ""
            for arg in args:
                if arg.upper().startswith("/LOG=") and Path(arg[5:]).is_file():
                    log = Path(arg[5:]).read_text(encoding="utf-8-sig", errors="replace")[-8000:]
            raise AssertionError(f"Exit {result.returncode}: {args}\n{result.stdout!r}\n{result.stderr!r}\n{log}")
        return result

    def setUp(self):
        if self.profile.exists():
            shutil.rmtree(self.profile)
        self.bundle = self.profile / "roaming" / "Autodesk" / "ApplicationPlugins" / "DhcbTools.bundle"
        self.addins = self.profile / "roaming" / "Autodesk" / "Revit" / "Addins"
        (self.acad2025 / "acdbmgd.runtimeconfig.json").write_text('{"runtimeOptions":{"tfm":"net8.0"}}', encoding="utf-8")
        (self.acad / "acad.exe").touch()
        (self.acad / "acdbmgd.runtimeconfig.json").write_text('{"runtimeOptions":{"tfm":"net10.0"}}', encoding="utf-8")

    def install(self, components, expect_success=True, preserve_unselected=False):
        return self.run_process([str(self.setup), "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
                          "/TYPE=custom", "/COMPONENTS=" + ",".join(components),
                          f"/DIR={self.profile / 'app'}", f"/ACAD2026DIR={self.acad}", f"/ACAD2025DIR={self.acad2025}",
                          f"/PRESERVEUNSELECTED={int(preserve_unselected)}",
                          f"/LOG={self.work / 'setup.log'}"], expect_success=expect_success)

    def test_fresh_default_install_selects_only_detected_hosts(self):
        self.run_process([str(self.setup), "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
                          f"/DIR={self.profile / 'app'}", f"/ACAD2026DIR={self.acad}", f"/LOG={self.work / 'default.log'}"])
        self.assert_bundle([2026])
        for year in REVIT_YEARS:
            self.assertEqual(year == 2022, (self.addins / str(year) / "DhcbTools.Revit.addin").is_file())
        self.assertTrue((self.profile / "app" / "DhcbTools.BatchRunner.exe").is_file())
        self.assertTrue((self.profile / "app" / "scripts" / "dhcb_agent.py").is_file())

    def test_scripts_only_install(self):
        self.install(["scripts"])
        self.assertEqual("# fixture", (self.profile / "app" / "scripts" / "dhcb_agent.py").read_text(encoding="utf-8"))
        self.assert_bundle([])
        self.assertFalse(self.addins.exists())
        self.assertFalse((self.profile / "app" / "DhcbTools.BatchRunner.exe").exists())

    def test_batch_only_install_respects_scripts_component(self):
        self.install(["batch"])
        self.assertTrue((self.profile / "app" / "DhcbTools.BatchRunner.exe").is_file())
        self.assertTrue((self.profile / "app" / "jobs" / "custom.json").is_file())
        self.assertFalse((self.profile / "app" / "scripts").exists())

    def test_missing_scripts_fail_compilation(self):
        scripts = self.stage / "batchrunner" / "scripts"
        saved = self.work / "saved-scripts"
        scripts.rename(saved)
        try:
            result = self.run_process([str(self.compiler), "/Q", "/DVersion=0.0.0-missing",
                                       f"/DStageDir={self.stage}", f"/O{self.work}", str(self.source)],
                                      expect_success=False)
            self.assertNotEqual(0, result.returncode)
            self.assertFalse((self.work / "DhcbTools-Setup-0.0.0-missing.exe").exists())
        finally:
            saved.rename(scripts)

    def test_incompatible_autocad_does_not_change_existing_install(self):
        self.install(["revit2024", "acad2025"])
        before = {path.relative_to(self.profile): path.read_bytes()
                  for path in self.profile.rglob("*") if path.is_file()}
        runtime = self.acad / "acdbmgd.runtimeconfig.json"
        exe = self.acad / "acad.exe"
        for fault in ("missing-exe", "missing-runtime", "unsupported", "unrelated-net10", "duplicate-tfm", "root-tfm", "malformed-json"):
            with self.subTest(fault=fault):
                exe.touch()
                runtime.write_text('{"runtimeOptions":{"tfm":"net10.0"}}', encoding="utf-8")
                if fault == "missing-exe":
                    exe.unlink()
                elif fault == "missing-runtime":
                    runtime.unlink()
                elif fault == "unsupported":
                    runtime.write_text('{"runtimeOptions":{"tfm":"net9.0"}}', encoding="utf-8")
                elif fault == "unrelated-net10":
                    runtime.write_text('{"note":"net10.0","runtimeOptions":{"tfm":"net9.0"}}', encoding="utf-8")
                elif fault == "duplicate-tfm":
                    runtime.write_text('{"runtimeOptions":{"tfm":"net10.0","tfm":"net8.0"}}', encoding="utf-8")
                elif fault == "root-tfm":
                    runtime.write_text('{"tfm":"net10.0"}', encoding="utf-8")
                else:
                    runtime.write_text('{"runtimeOptions":{"tfm":"net10.0"}', encoding="utf-8")
                result = self.install(["revit2026", "acad2026"], expect_success=False)
                self.assertNotEqual(0, result.returncode)
                after = {path.relative_to(self.profile): path.read_bytes()
                         for path in self.profile.rglob("*") if path.is_file()}
                self.assertEqual(before, after, "Runtime không đúng phải bị chặn trước khi ghi/xoá file")

    def test_autocad_runtime_profiles_selected_without_stale_binary(self):
        for year, folder in ((2025, self.acad2025), (2026, self.acad)):
            for runtime in ("net8.0", "net10.0", "net8.0"):
                with self.subTest(year=year, runtime=runtime):
                    (folder / "acdbmgd.runtimeconfig.json").write_text(
                        '{"runtimeOptions":{"tfm":"' + runtime + '"}}', encoding="utf-8")
                    self.install([f"acad{year}"])
                    default = (year == 2025 and runtime == "net8.0") or (year == 2026 and runtime == "net10.0")
                    profile = str(year) if default else f"{year}-{'net8' if runtime == 'net8.0' else 'net10'}"
                    self.assertEqual(f"fixture {profile}",
                                     (self.bundle / "Contents" / str(year) / "DhcbTools.AutoCAD.dll").read_text(encoding="utf-8"))
                    self.assert_bundle([year])
                    metadata = json.loads((self.bundle / "Contents" / str(year) / "dhcb-host-profile.json").read_text(encoding="utf-8-sig"))
                    self.assertEqual({"product": "autocad", "year": year, "runtime": runtime}, metadata)

    def test_runtimeconfig_json_supports_bom_escapes_arrays_and_numbers(self):
        config = {"runtimeOptions": {"tfm": "net10.0", "frameworks": [
            {"name": "Microsoft.NETCore.App", "version": "10.0.0"},
            {"name": "Microsoft.WindowsDesktop.App", "version": "10.0.0"}],
            "configProperties": {"enabled": True, "nullable": None, "number": -1.25e-4}},
            "note": "tiếng Việt \"quoted\""}
        for bom, escaped in ((False, False), (True, False), (True, True)):
            with self.subTest(bom=bom, escaped=escaped):
                content = json.dumps(config, ensure_ascii=True)
                if escaped:
                    content = content.replace('"tfm"', '"' + chr(92) + 'u0074fm"')
                (self.acad / "acdbmgd.runtimeconfig.json").write_text(
                    content, encoding="utf-8-sig" if bom else "utf-8")
                self.install(["acad2026"])
                self.assert_bundle([2026])

    def assert_bundle(self, years):
        if not years:
            self.assertFalse(self.bundle.exists())
            return
        root = ET.parse(self.bundle / "PackageContents.xml").getroot()
        # XML còn hợp lệ và tiếng Việt không bị chuyển qua code page ANSI.
        self.assertEqual(self.original.attrib, root.attrib)
        self.assertEqual(self.original.find("CompanyDetails").attrib, root.find("CompanyDetails").attrib)
        modules = [entry.attrib["ModuleName"] for entry in root.findall("Components/ComponentEntry")]
        self.assertEqual([f"./Contents/{year}/DhcbTools.AutoCAD.dll" for year in years], modules)
        for year in ACAD_YEARS:
            self.assertEqual(year in years, (self.bundle / "Contents" / str(year)).exists())
        for module in modules:
            self.assertTrue((self.bundle / module).is_file(), module)

    def test_all_autocad_component_combinations(self):
        for choices in itertools.product((False, True), repeat=len(ACAD_YEARS)):
            years = [year for year, chosen in zip(ACAD_YEARS, choices) if chosen]
            with self.subTest(years=years):
                self.install([f"acad{year}" for year in years])
                self.assert_bundle(years)

    def test_upgrade_deselects_revit_and_preserves_other_files(self):
        self.install([f"revit{year}" for year in REVIT_YEARS] + [f"acad{year}" for year in ACAD_YEARS])
        for year in REVIT_YEARS:
            (self.addins / str(year) / "DhcbTools.Custom.addin").write_text("user file", encoding="utf-8")
        self.install(["revit2024", "acad2025"])
        self.assert_bundle([2025])
        for year in REVIT_YEARS:
            folder = self.addins / str(year)
            for name in REVIT_FILES:
                self.assertEqual(year == 2024, (folder / name).exists(), f"{year}/{name}")
            self.assertEqual("user file", (folder / "DhcbTools.Custom.addin").read_text(encoding="utf-8"))
            self.assertTrue((folder / "Newtonsoft.Json.dll").is_file())
        # Chọn lại thành phần sau khi đã gỡ phải khôi phục DLL và manifest.
        self.install(["revit2026", "acad2024", "acad2026"])
        self.assert_bundle([2024, 2026])
        self.assertTrue((self.addins / "2026" / "DhcbTools.Revit.addin").is_file())
        self.assertFalse((self.addins / "2024" / "DhcbTools.Revit.addin").exists())
        self.install([])
        self.assert_bundle([])
        self.assertFalse((self.addins / "2026" / "DhcbTools.Revit.addin").exists())

    def test_scoped_upgrade_preserves_revit_and_existing_autocad_settings(self):
        self.install(["revit2024", "acad2024", "acad2026"])
        state_paths = ("roaming/DHCB/settings.json", "roaming/DHCB/bridge-token.txt", "local/DHCB/ledger.jsonl")
        for relative in state_paths:
            path = self.profile / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("user state " + relative, encoding="utf-8")
        state_before = {relative: (self.profile / relative).read_bytes() for relative in state_paths}
        before_revit = {p.relative_to(self.addins): p.read_bytes()
                        for p in self.addins.rglob("*") if p.is_file()}
        xml = self.bundle / "PackageContents.xml"
        tree = ET.parse(xml)
        component = tree.getroot().findall("Components")[0]
        component.set("Description", "Thiết lập cũ — tiếng Việt")
        component.find("ComponentEntry").set("LoadOnAutoCADStartup", "False")
        tree.write(xml, encoding="utf-8", xml_declaration=True)
        xml.write_bytes(b"\xef\xbb\xbf" + xml.read_bytes())
        old_acad = (self.bundle / "Contents" / "2024" / "DhcbTools.AutoCAD.dll").read_bytes()
        self.install(["acad2025", "batch", "scripts"], preserve_unselected=True)
        self.assert_bundle([2024, 2025, 2026])
        self.assertEqual(before_revit, {p.relative_to(self.addins): p.read_bytes()
                                      for p in self.addins.rglob("*") if p.is_file()})
        self.assertEqual(state_before, {relative: (self.profile / relative).read_bytes() for relative in state_paths})
        self.assertEqual(old_acad, (self.bundle / "Contents" / "2024" / "DhcbTools.AutoCAD.dll").read_bytes())
        retained = ET.parse(xml).getroot().findall("Components")[0]
        self.assertEqual("Thiết lập cũ — tiếng Việt", retained.attrib["Description"])
        self.assertEqual("False", retained.find("ComponentEntry").attrib["LoadOnAutoCADStartup"])
        # A scripts-only scoped upgrade leaves host manifests and binaries untouched.
        before = {p.relative_to(self.bundle): p.read_bytes() for p in self.bundle.rglob("*") if p.is_file()}
        self.install(["scripts"], preserve_unselected=True)
        self.assertEqual(before, {p.relative_to(self.bundle): p.read_bytes()
                                 for p in self.bundle.rglob("*") if p.is_file()})

    def test_scoped_fresh_install_does_not_activate_unselected_hosts(self):
        self.install(["acad2025"], preserve_unselected=True)
        self.assert_bundle([2025])
        self.assertFalse(self.addins.exists())

    def test_noncanonical_and_commented_components_preserve_only_live_settings(self):
        for variant in ("no-attributes", "newline", "single-quotes", "commented-decoy"):
            with self.subTest(variant=variant):
                self.setUp()
                self.install(["acad2024"])
                manifest = self.bundle / "PackageContents.xml"
                original = manifest.read_text(encoding="utf-8-sig").replace('LoadOnAutoCADStartup="True"', 'LoadOnAutoCADStartup="False"')
                if variant == "no-attributes":
                    changed = original.replace('<Components Description="AutoCAD 2024">', '<Components>')
                elif variant == "newline":
                    changed = original.replace('<Components Description=', '<Components\n Description=')
                else:
                    changed = original.replace('ModuleName="./Contents/2024/DhcbTools.AutoCAD.dll"', "ModuleName='./Contents/2024/DhcbTools.AutoCAD.dll'")
                    if variant == "commented-decoy":
                        decoy = '<!-- <Components Description="Decoy"><ComponentEntry ModuleName="./Contents/2024/DhcbTools.AutoCAD.dll" LoadOnAutoCADStartup="True"/></Components> -->'
                        changed = changed.replace('<Components Description="AutoCAD 2024">', decoy + '<Components Description="AutoCAD 2024">')
                manifest.write_text(changed, encoding="utf-8")
                self.install(["acad2025"], preserve_unselected=True)
                self.assert_bundle([2024, 2025])
                retained = ET.parse(manifest).getroot().findall("Components")[0]
                self.assertEqual("False", retained.find("ComponentEntry").attrib["LoadOnAutoCADStartup"])
                self.assertNotEqual("Decoy", retained.attrib.get("Description"))

    def test_grouped_host_entries_block_scope_changes_without_duplication(self):
        self.install(["acad2024", "acad2025"])
        manifest = self.bundle / "PackageContents.xml"
        tree = ET.parse(manifest)
        first, second = tree.getroot().findall("Components")
        first.append(second.find("ComponentEntry"))
        tree.getroot().remove(second)
        tree.write(manifest, encoding="utf-8", xml_declaration=True)
        before = {p.relative_to(self.profile): p.read_bytes() for p in self.profile.rglob("*") if p.is_file()}
        result = self.install(["acad2026"], preserve_unselected=True, expect_success=False)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(before, {p.relative_to(self.profile): p.read_bytes()
                                 for p in self.profile.rglob("*") if p.is_file()})

    def test_preserved_node_comment_cannot_activate_an_unselected_year(self):
        self.install(["acad2024"])
        manifest = self.bundle / "PackageContents.xml"
        tree = ET.parse(manifest)
        component = tree.getroot().find("Components")
        component.append(ET.Comment('<Components Description="AutoCAD 2025"><ComponentEntry ModuleName="./Contents/2025/DhcbTools.AutoCAD.dll"/></Components>'))
        tree.write(manifest, encoding="utf-8", xml_declaration=True)
        self.install(["acad2026"], preserve_unselected=True)
        self.assert_bundle([2024, 2026])

    def test_invalid_existing_manifest_blocks_scoped_upgrade_without_changes(self):
        self.install(["revit2024", "acad2024"])
        manifest = self.bundle / "PackageContents.xml"
        original = manifest.read_text(encoding="utf-8-sig")
        for content in ("broken XML", "<ApplicationPackage/>", "<!DOCTYPE x [<!ENTITY e 'x'>]><ApplicationPackage>&e;</ApplicationPackage>",
                        original.replace('./Contents/2024/DhcbTools.AutoCAD.dll', '.\\Contents\\2024\\DhcbTools.AutoCAD.dll')):
            with self.subTest(content=content):
                manifest.write_text(content, encoding="utf-8")
                before = {p.relative_to(self.profile): p.read_bytes()
                          for p in self.profile.rglob("*") if p.is_file()}
                result = self.install(["acad2025"], preserve_unselected=True, expect_success=False)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual(before, {p.relative_to(self.profile): p.read_bytes()
                                         for p in self.profile.rglob("*") if p.is_file()})

    def test_batch_upgrade_preserves_edited_jobs_configs_and_adds_new_samples(self):
        self.install(["batch"])
        app = self.profile / "app"
        for folder in ("jobs", "configs"):
            (app / folder / "custom.json").write_text("user edited", encoding="utf-8")
            (app / folder / "fresh.sample.json").unlink()
        self.install(["batch"])
        for folder in ("jobs", "configs"):
            self.assertEqual("user edited", (app / folder / "custom.json").read_text(encoding="utf-8"))
            self.assertEqual("new sample", (app / folder / "fresh.sample.json").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
