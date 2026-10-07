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
        source.write_text(text, encoding="utf-8-sig")
        cls.run_process([str(compiler), "/Q", "/DVersion=0.0.0-test", f"/DStageDir={stage}",
                         f"/O{cls.work}", str(source)])
        cls.setup = cls.work / "DhcbTools-Setup-0.0.0-test.exe"

    @staticmethod
    def run_process(args):
        result = subprocess.run(args, capture_output=True, timeout=60)
        if result.returncode:
            raise AssertionError(f"Exit {result.returncode}: {args}\n{result.stdout!r}\n{result.stderr!r}")

    def setUp(self):
        if self.profile.exists():
            shutil.rmtree(self.profile)
        self.bundle = self.profile / "roaming" / "Autodesk" / "ApplicationPlugins" / "DhcbTools.bundle"
        self.addins = self.profile / "roaming" / "Autodesk" / "Revit" / "Addins"

    def install(self, components):
        self.run_process([str(self.setup), "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
                          "/TYPE=custom", "/COMPONENTS=" + ",".join(components),
                          f"/DIR={self.profile / 'app'}", f"/ACAD2026DIR={self.acad}",
                          f"/LOG={self.work / 'setup.log'}"])

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
