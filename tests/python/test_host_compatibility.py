"""Check evaluated MSBuild profiles without restoring packages or compiling hosts."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
DOTNET = os.environ.get("DHCB_DOTNET") or shutil.which("dotnet")
if not DOTNET and Path("/mnt/c/Program Files/dotnet/dotnet.exe").is_file():
    DOTNET = "/mnt/c/Program Files/dotnet/dotnet.exe"


@unittest.skipUnless(DOTNET, "MSBuild profile checks require a .NET SDK")
class HostCompatibilityTests(unittest.TestCase):
    def msbuild(self, host, *arguments):
        project = f"src/DhcbTools.{host}/DhcbTools.{host}.csproj"
        return subprocess.run(
            [DOTNET, "msbuild", project, "-nologo", *arguments],
            cwd=ROOT, capture_output=True, text=True, timeout=60,
        )

    def profile(self, host, **properties):
        result = self.msbuild(
            host, "-getProperty:TargetFramework,BaseOutputPath,BaseIntermediateOutputPath,RevitApiVersion,AcadNetVersion,AcadCoreNetVersion",
            *(f"-p:{key}={value}" for key, value in properties.items()),
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return json.loads(result.stdout)["Properties"]

    def test_autocad_each_year_uses_its_own_sdk_and_isolated_restore(self):
        profiles = [
            (2022, "net48", "24.1.51000", "24.1.51000"),
            (2023, "net48", "24.2.0", "24.2.0"),
            (2024, "net48", "24.3.0", "24.3.0"),
            (2025, "net8", "25.0.1", "25.0.0"),
            (2025, "net10", "25.0.2", "25.0.2"),
            (2026, "net8", "25.1.0", "25.1.0"),
            (2026, "net10", "25.1.1", "25.1.1"),
            (2027, "net10", "26.0.0", "26.0.0"),
        ]
        for year, runtime, api, core in profiles:
            with self.subTest(year=year, runtime=runtime):
                profile = self.profile("AutoCAD", AcadVersion=year, AcadRuntime=runtime)
                tfm = "net48" if runtime == "net48" else runtime + ".0-windows"
                self.assertEqual(profile["TargetFramework"], tfm)
                self.assertEqual(profile["AcadNetVersion"], api)
                self.assertEqual(profile["AcadCoreNetVersion"], core)
                for key, prefix in [("BaseOutputPath", "bin"), ("BaseIntermediateOutputPath", "obj")]:
                    self.assertEqual(profile[key].replace(chr(92), "/"), f"{prefix}/{year}/{runtime}/")

    def test_revit_api_and_runtime_matrix(self):
        apis = {2022: "2022.1.80", 2023: "2023.1.90", 2024: "2024.3.60", 2025: "2025.4.60", 2026: "2026.4.10", 2027: "2027.2.0"}
        for year, api in apis.items():
            with self.subTest(year=year):
                profile = self.profile("Revit", RevitVersion=year)
                runtime = "net48" if year <= 2024 else "net8" if year <= 2026 else "net10"
                self.assertEqual(profile["TargetFramework"], "net48" if year <= 2024 else runtime + ".0-windows")
                self.assertEqual(profile["RevitApiVersion"], api)
                self.assertEqual(profile["BaseOutputPath"].replace(chr(92), "/"), f"bin/{year}/{runtime}/")
                self.assertEqual(profile["BaseIntermediateOutputPath"].replace(chr(92), "/"), f"obj/{year}/{runtime}/")

    def test_headless_checks_do_not_replace_full_ui_binaries_or_restore_assets(self):
        for host, year_property in [("AutoCAD", "AcadVersion"), ("Revit", "RevitVersion")]:
            with self.subTest(host=host):
                full = self.profile(host, **{year_property: 2027})
                headless = self.profile(host, UseWPF="false", **{year_property: 2027})
                for key in ("BaseOutputPath", "BaseIntermediateOutputPath"):
                    self.assertNotEqual(headless[key], full[key])
                    self.assertIn("/headless/", headless[key].replace(chr(92), "/"))
                self.assertEqual(headless["TargetFramework"], full["TargetFramework"])

    def test_defaults_preserve_existing_host_policy(self):
        self.assertEqual(self.profile("Revit")["RevitApiVersion"], "2024.3.60")
        self.assertEqual(self.profile("AutoCAD")["AcadNetVersion"], "24.3.0")
        self.assertEqual(self.profile("AutoCAD", AcadVersion=2025)["TargetFramework"], "net8.0-windows")
        self.assertEqual(self.profile("AutoCAD", AcadVersion=2026)["TargetFramework"], "net10.0-windows")

    def test_unsupported_or_mismatched_profiles_fail_before_restore(self):
        invalid = [
            ("AutoCAD", {"AcadVersion": 2021}),
            ("AutoCAD", {"AcadVersion": 2028}),
            ("AutoCAD", {"AcadVersion": "unexpected"}),
            ("AutoCAD", {"AcadVersion": 2022, "AcadNetVersion": "24.3.0"}),
            ("AutoCAD", {"AcadVersion": 2023, "AcadRuntime": "net8"}),
            ("AutoCAD", {"AcadVersion": 2027, "AcadRuntime": "net8"}),
            ("AutoCAD", {"AcadVersion": 2025, "AcadRuntime": "net8", "AcadNetVersion": "25.0.2"}),
            ("AutoCAD", {"AcadVersion": 2026, "AcadRuntime": "net10", "AcadNetVersion": "25.1.0"}),
            ("AutoCAD", {"AcadVersion": 2025, "AcadCoreNetVersion": "25.1.0"}),
            ("AutoCAD", {"AcadVersion": 2025, "TargetFramework": "net10.0-windows"}),
            ("Revit", {"RevitVersion": 2021}),
            ("Revit", {"RevitVersion": 2028}),
            ("Revit", {"RevitVersion": "unexpected"}),
            ("Revit", {"RevitVersion": 2022, "RevitApiVersion": "2024.3.60"}),
            ("Revit", {"RevitVersion": 2025, "RevitRuntime": "net10"}),
            ("Revit", {"RevitVersion": 2027, "TargetFramework": "net8.0-windows"}),
        ]
        for host, properties in invalid:
            with self.subTest(host=host, properties=properties):
                result = self.msbuild(host, "-t:ValidateDhcbHostProfile", *(f"-p:{key}={value}" for key, value in properties.items()))
                self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertIn("error", result.stdout.lower())


if __name__ == "__main__":
    unittest.main()
