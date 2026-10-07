"""Chạy installer thật trên Windows, chỉ ghi vào thư mục tạm, không đăng ký uninstall."""

import itertools
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
ACAD_YEARS = (2024, 2025, 2026)


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
        for year in (2023, 2024, 2025, 2026):
            folder = stage / f"revit-{year}"
            folder.mkdir(parents=True)
            for name in (*REVIT_FILES, "Newtonsoft.Json.dll"):
                (folder / name).write_text(f"fixture {year}", encoding="utf-8")
        for year in ACAD_YEARS:
            folder = stage / f"autocad-{year}"
            folder.mkdir()
            (folder / "DhcbTools.AutoCAD.dll").write_text(f"fixture {year}", encoding="utf-8")
        (stage / "batchrunner" / "scripts").mkdir(parents=True)
        (stage / "batchrunner" / "DhcbTools.BatchRunner.exe").write_text("fixture batch", encoding="utf-8")
        (stage / "batchrunner" / "scripts" / "dhcb_agent.py").write_text("# fixture", encoding="utf-8")
        shutil.copyfile(ROOT / "installer" / "PackageContents.xml", stage / "PackageContents.xml")
        cls.original = ET.parse(stage / "PackageContents.xml").getroot()
        cls.acad = cls.work / "fake-acad"
        cls.acad.mkdir()
        (cls.acad / "acad.exe").touch()
        (cls.acad / "acdbmgd.runtimeconfig.json").write_text('{"tfm":"net10.0"}', encoding="utf-8")

        # Chỉ chuyển các đích ghi của bộ cài vào sandbox; giữ nguyên [Code]/[InstallDelete].
        text = (ROOT / "installer" / "dhcb-tools.iss").read_text(encoding="utf-8")
        text = text.replace("[Setup]", "[Setup]\nUninstallable=no\nCreateUninstallRegKey=no")
        for constant, folder in (("userappdata", "roaming"), ("localappdata", "local"), ("group", "shortcuts")):
            text = text.replace("{" + constant + "}", str(cls.profile / folder))
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
            raise AssertionError(f"Exit {result.returncode}: {args}\n{result.stdout!r}\n{result.stderr!r}")
        return result

    def setUp(self):
        if self.profile.exists():
            shutil.rmtree(self.profile)
        self.bundle = self.profile / "roaming" / "Autodesk" / "ApplicationPlugins" / "DhcbTools.bundle"
        self.addins = self.profile / "roaming" / "Autodesk" / "Revit" / "Addins"
        (self.acad / "acad.exe").touch()
        (self.acad / "acdbmgd.runtimeconfig.json").write_text('{"tfm":"net10.0"}', encoding="utf-8")

    def install(self, components, expect_success=True):
        return self.run_process([str(self.setup), "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
                          "/TYPE=custom", "/COMPONENTS=" + ",".join(components),
                          f"/DIR={self.profile / 'app'}", f"/ACAD2026DIR={self.acad}",
                          f"/LOG={self.work / 'setup.log'}"], expect_success=expect_success)

    def test_scripts_only_install(self):
        self.install(["scripts"])
        self.assertEqual("# fixture", (self.profile / "app" / "scripts" / "dhcb_agent.py").read_text(encoding="utf-8"))
        self.assert_bundle([])
        self.assertFalse(self.addins.exists())
        self.assertFalse((self.profile / "app" / "DhcbTools.BatchRunner.exe").exists())

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
        for fault in ("missing-exe", "missing-runtime", "net8"):
            with self.subTest(fault=fault):
                exe.touch()
                runtime.write_text('{"tfm":"net10.0"}', encoding="utf-8")
                if fault == "missing-exe":
                    exe.unlink()
                elif fault == "missing-runtime":
                    runtime.unlink()
                else:
                    runtime.write_text('{"tfm":"net8.0"}', encoding="utf-8")
                result = self.install(["revit2026", "acad2026"], expect_success=False)
                self.assertNotEqual(0, result.returncode)
                after = {path.relative_to(self.profile): path.read_bytes()
                         for path in self.profile.rglob("*") if path.is_file()}
                self.assertEqual(before, after, "Runtime không đúng phải bị chặn trước khi ghi/xoá file")

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
        for choices in itertools.product((False, True), repeat=3):
            years = [year for year, chosen in zip(ACAD_YEARS, choices) if chosen]
            with self.subTest(years=years):
                self.install([f"acad{year}" for year in years])
                self.assert_bundle(years)

    def test_upgrade_deselects_revit_and_preserves_other_files(self):
        self.install([f"revit{year}" for year in (2023, 2024, 2025, 2026)] + ["acad2024", "acad2025", "acad2026"])
        for year in (2023, 2024, 2025, 2026):
            (self.addins / str(year) / "DhcbTools.Custom.addin").write_text("user file", encoding="utf-8")
        self.install(["revit2024", "acad2025"])
        self.assert_bundle([2025])
        for year in (2023, 2024, 2025, 2026):
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


if __name__ == "__main__":
    unittest.main()
