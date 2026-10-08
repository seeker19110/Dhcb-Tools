"""Thực thi bước đóng gói thật với công cụ native lỗi, không tạo artifact phát hành."""

import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "release.yml"
PWSH = shutil.which("pwsh")


def release_step(job, name):
    workflow = WORKFLOW.read_text(encoding="utf-8")
    section = re.search(rf"^  {re.escape(job)}:\n(.*?)(?=^  [\w-]+:|\Z)",
                        workflow, re.M | re.S).group(1)
    step = re.search(rf"^      - name: {re.escape(name)}\n(.*?)(?=^      - |\Z)",
                     section, re.M | re.S).group(1)
    block = re.search(r"        run: \|\n((?: {10}.*\n|\n)*)", step).group(1)
    script = textwrap.dedent(block).rstrip()
    return re.sub(r"\$\{\{\s*(.*?)\s*\}\}",
                  lambda match: "1.2.3" if "version.outputs" in match.group(1) else "2025", script)


def ps_quote(value):
    return "'" + str(value).replace("'", "''") + "'"


@unittest.skipUnless(PWSH, "Cần pwsh để thực thi các bước PowerShell của release")
class ReleaseWorkflowTests(unittest.TestCase):
    def run_script(self, script, prepare=None):
        with tempfile.TemporaryDirectory(prefix="dhcb-release-") as tmp:
            if prepare is not None:
                prepare(Path(tmp))
            source = Path(tmp) / "step.ps1"
            source.write_text("$ErrorActionPreference = 'Stop'\n" + script, encoding="utf-8")
            return subprocess.run([PWSH, "-NoProfile", "-File", str(source)], cwd=tmp,
                                  capture_output=True, text=True, timeout=30)

    def test_failed_msbuild_stops_before_packaging_even_with_tfm_on_stdout(self):
        # Native command trả cả TFM hợp lệ lẫn exit != 0. PowerShell vẫn nhận stdout;
        # kiểm chuỗi không rỗng hoặc để tới Copy-Item không bắt được lỗi này.
        native = ps_quote(sys.executable)
        stub = f"function dotnet {{ & {native} -c 'import sys; print(\"net10.0\"); sys.exit(23)' }}\n"
        for job, name in (("build-revit", "Đóng gói"), ("build-autocad", "Đóng gói"),
                          ("build-batchrunner", "Đóng gói kèm script và job mẫu")):
            with self.subTest(job=job):
                script = release_step(job, name)
                # Chạy tới trước Copy-Item đầu tiên: file đầu ra chỉ được đọc sau cổng này.
                script = re.split(r"(?m)^Copy-Item\b", script, maxsplit=1)[0] + "Write-Output 'PACKAGING_REACHED'\n"
                result = self.run_script(stub + script)
                self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertNotIn("PACKAGING_REACHED", result.stdout)
                self.assertIn("MSBuild", result.stderr)

    def test_msbuild_silent_failure_and_empty_success_stop_before_packaging(self):
        native = ps_quote(sys.executable)
        for exit_code in (0, 23):
            stub = f"function dotnet {{ & {native} -c 'import sys; sys.exit({exit_code})' }}\n"
            for job in ("build-revit", "build-autocad"):
                with self.subTest(exit_code=exit_code, job=job):
                    script = release_step(job, "Đóng gói")
                    script = re.split(r"(?m)^Copy-Item\b", script, maxsplit=1)[0] + "Write-Output 'PACKAGING_REACHED'\n"
                    result = self.run_script(stub + script)
                    self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
                    self.assertNotIn("PACKAGING_REACHED", result.stdout)
                    self.assertIn("MSBuild", result.stderr)

    def test_failed_targetdir_lookup_stops_before_copying_stale_files(self):
        native = ps_quote(sys.executable)
        stub = ("function dotnet {\n"
                "  if ($args -contains 'TargetDir') {\n"
                f"    & {native} -c 'import sys; print(\"fixture-output\"); sys.exit(23)'\n"
                "  } else {\n"
                f"    & {native} -c 'print(\"net10.0\")'\n"
                "  }\n}\n")
        for job, name in (("build-revit", "Đóng gói"), ("build-autocad", "Đóng gói"),
                          ("build-batchrunner", "Đóng gói kèm script và job mẫu")):
            with self.subTest(job=job):
                script = release_step(job, name)
                script = re.split(r"(?m)^Copy-Item\b", script, maxsplit=1)[0] + "Write-Output 'COPY_REACHED'\n"
                result = self.run_script(stub + script)
                self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertNotIn("COPY_REACHED", result.stdout)
                self.assertIn("TargetDir", result.stderr)

    def test_successful_msbuild_reaches_packaging(self):
        native = ps_quote(sys.executable)
        stub = f"function dotnet {{ & {native} -c 'print(\"net10.0\")' }}\n"
        for job, name in (("build-revit", "Đóng gói"), ("build-autocad", "Đóng gói"),
                          ("build-batchrunner", "Đóng gói kèm script và job mẫu")):
            with self.subTest(job=job):
                script = release_step(job, name)
                script = re.split(r"(?m)^Copy-Item\b", script, maxsplit=1)[0] + "Write-Output 'PACKAGING_REACHED'\n"
                result = self.run_script(stub + script)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertIn("PACKAGING_REACHED", result.stdout)

    def test_packages_include_exact_profile_and_exclude_host_api_binaries(self):
        native = ps_quote(sys.executable)
        for job, product, names in (
            ("build-revit", "revit", ("DhcbTools.Revit.addin", "DhcbTools.Revit.dll", "DhcbTools.Core.dll",
                                      "DhcbTools.Shared.Logic.dll", "DhcbTools.Shared.Hosting.dll", "Newtonsoft.Json.dll")),
            ("build-autocad", "autocad", ("DhcbTools.AutoCAD.dll", "DhcbTools.AutoCAD.Core.dll", "DhcbTools.Core.AutoCAD.dll",
                                          "DhcbTools.Shared.Logic.dll", "DhcbTools.Shared.Hosting.dll", "Newtonsoft.Json.dll",
                                          "DhcbTools.AutoCAD.deps.json", "DhcbTools.AutoCAD.Core.deps.json")),
        ):
            with self.subTest(job=job):
                def prepare(folder):
                    (folder / "fixture-output").mkdir()
                    for name in (*names, "RevitAPI.dll", "AcDbMgd.dll", "System.Drawing.Common.dll"):
                        (folder / "fixture-output" / name).write_text("fixture", encoding="utf-8")
                    (folder / "docs").mkdir()
                    for name in ("LICENSE", "NOTICE", "docs/huong-dan-cai-dat-va-kiem-thu-thu-cong.md",
                                 "docs/tuong-thich-2022-2027.md", "docs/chan-doan-windows.md"):
                        (folder / name).write_text("fixture", encoding="utf-8")
                stub = ("function dotnet {\n"
                        "  if ($args -contains 'TargetDir') {\n"
                        f"    & {native} -c 'print(\"fixture-output\")'\n"
                        "  } else {\n"
                        f"    & {native} -c 'print(\"net8.0-windows\")'\n"
                        "  }\n}\n")
                verify = "\n$profileFile = Get-ChildItem dist -Filter dhcb-host-profile.json -Recurse | Select-Object -First 1\n"
                verify += "$profile = Get-Content $profileFile.FullName -Raw | ConvertFrom-Json\n"
                verify += f"if ($profile.product -ne '{product}' -or $profile.year -ne 2025 -or $profile.runtime -ne 'net8.0') {{ throw 'Invalid profile' }}\n"
                verify += "if (Get-ChildItem dist -Include RevitAPI.dll,AcDbMgd.dll,System.Drawing.Common.dll -Recurse) { throw 'Host binary shipped' }\n"
                if product == "autocad":
                    verify += "if (-not (Test-Path (Join-Path $profileFile.DirectoryName 'DhcbTools.AutoCAD.Core.deps.json'))) { throw 'Core dependency metadata missing' }\n"
                verify += "Write-Output 'PACKAGE_VERIFIED'\n"
                result = self.run_script(stub + release_step(job, "Đóng gói") + verify, prepare)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertIn("PACKAGE_VERIFIED", result.stdout)

    @unittest.skipIf(os.name == "nt", "Stub executable dùng shebang trên Linux CI")
    def test_failed_iscc_stops_even_when_old_installer_exists(self):
        with tempfile.TemporaryDirectory(prefix="dhcb-iscc-") as tmp:
            compiler = Path(tmp) / "ISCC.exe"
            compiler.write_text(f"#!{sys.executable}\nimport sys\nsys.exit(23)\n", encoding="utf-8")
            compiler.chmod(0o755)
            script = release_step("installer", "Đóng gói installer")
            script = re.sub(r'^\$iscc = .*$', "$iscc = " + ps_quote(compiler), script, flags=re.M)
            # Danh sách artifact cũ không được biến lỗi compiler thành bước thành công.
            prefix = ("New-Item -ItemType Directory -Path dist | Out-Null\n"
                      "Set-Content dist/DhcbTools-Setup-0.9.0.exe fixture\n")
            result = self.run_script(prefix + script + "\nWrite-Output 'OLD_INSTALLER_REACHED'\n")
            self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertNotIn("OLD_INSTALLER_REACHED", result.stdout)
            self.assertIn("ISCC", result.stderr)

    @unittest.skipIf(os.name == "nt", "Stub executable dùng shebang trên Linux CI")
    def test_successful_iscc_requires_installer_for_requested_version(self):
        with tempfile.TemporaryDirectory(prefix="dhcb-iscc-") as tmp:
            compiler = Path(tmp) / "ISCC.exe"
            compiler.write_text(f"#!{sys.executable}\n", encoding="utf-8")
            compiler.chmod(0o755)
            script = release_step("installer", "Đóng gói installer")
            script = re.sub(r'^\$iscc = .*$', "$iscc = " + ps_quote(compiler), script, flags=re.M)
            for version in ("0.9.0", "1.2.3"):
                with self.subTest(version=version):
                    prefix = ("New-Item -ItemType Directory -Path dist | Out-Null\n"
                              f"Set-Content dist/DhcbTools-Setup-{version}.exe fixture\n")
                    result = self.run_script(prefix + script + "\nWrite-Output 'INSTALLER_LIST_REACHED'\n")
                    if version == "1.2.3":
                        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                        self.assertIn("INSTALLER_LIST_REACHED", result.stdout)
                    else:
                        self.assertNotEqual(0, result.returncode)
                        self.assertIn("ISCC", result.stderr)
                        self.assertNotIn("INSTALLER_LIST_REACHED", result.stdout)


if __name__ == "__main__":
    unittest.main()
