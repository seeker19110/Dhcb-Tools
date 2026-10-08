"""Chạy các cổng MSBuild thật của script, chặn DLL cũ khi truy vấn framework lỗi."""

from pathlib import Path
import os
import json
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
PWSH = shutil.which("pwsh")
LOOKUPS = (
    ("run-in-autocad-tests.ps1", "acadTfm", "AutoCAD.Core"),
    ("run-in-autocad-tests.ps1", "runnerTfm", "BatchRunner"),
    ("run-in-revit-tests.ps1", "tfm", "Revit"),
    ("run-in-revit-tests.ps1", "runnerTfm", "BatchRunner"),
    ("dung-family.ps1", "runnerTfm", "BatchRunner"),
    ("dung-family.ps1", "tfm", "Revit"),
    ("dung-family.ps1", "binDir", "Revit"),
    ("run-in-revit-tests.ps1", "binDir", "Revit"),
    ("run-in-autocad-tests.ps1", "acadOut", "AutoCAD.Core"),
)


@unittest.skipUnless(PWSH, "Cần pwsh để thực thi script PowerShell")
class ScriptFrameworkTests(unittest.TestCase):
    def test_tag_signing_requires_certificate_but_dev_can_skip(self):
        env = {key: value for key, value in os.environ.items()
               if key not in {"DHCB_SIGN_PFX_BASE64", "DHCB_SIGN_PFX_PASSWORD"}}
        with tempfile.TemporaryDirectory(prefix="dhcb-sign-") as tmp:
            for required in (False, True):
                with self.subTest(required=required):
                    args = [PWSH, "-NoProfile", "-File", str(ROOT / "scripts" / "sign-release.ps1"), "-Path", tmp]
                    if required:
                        args.append("-RequireSignature")
                    result = subprocess.run(args, env=env, capture_output=True, text=True, timeout=30)
                    self.assertEqual(required, result.returncode != 0, result.stdout + result.stderr)
                    self.assertIn("DHCB_SIGN_PFX_BASE64", result.stdout + result.stderr)
                    self.assertEqual([], list(Path(tmp).iterdir()))

    def probe(self, name, variable, output, exit_code):
        text = "\n".join(line.lstrip() for line in (ROOT / "scripts" / name).read_text(encoding="utf-8-sig").splitlines())
        stop = re.search(r"(?m)^function Stop-WithMessage\b[^\n]*\n.*?^}", text, re.S).group()
        # Lấy cổng từ chính script; chặn trước khi dùng TFM để chọn DLL/runner.
        block = re.search(rf"(?m)^\${variable} = .*\n(?:^if .*\n|^\${variable} = .*\n)*", text).group()
        native = "'" + sys.executable.replace("'", "''") + "'"
        code = "import sys; " + (f"print({output!r}); " if output is not None else "") + f"sys.exit({exit_code})"
        quoted_code = "'" + code.replace("'", "''") + "'"
        invocation_check = ("  if ($args[0] -ne 'msbuild' -or $args -notcontains '-nologo') { throw 'Unexpected MSBuild invocation' }\n"
                            if exit_code == 0 and output else "")
        stub = ("function dotnet {\n" + invocation_check
                + f"  & {native} -c {quoted_code}\n"
                "}\n")
        with tempfile.TemporaryDirectory(prefix="dhcb-tfm-") as tmp:
            folder = Path(tmp)
            (folder / "net10.0").mkdir()
            marker = folder / "existing-addin.dll"
            marker.write_bytes(b"previous installation")
            script = folder / "probe.ps1"
            script.write_text("$ErrorActionPreference = 'Stop'\n" + stop + "\n" + stub + block
                              + "Set-Content existing-addin.dll overwritten\n"
                              + f"Write-Output ('FRAMEWORK=' + ${variable})\n", encoding="utf-8")
            result = subprocess.run([PWSH, "-NoProfile", "-File", str(script)], cwd=tmp,
                                    capture_output=True, text=True, timeout=30)
            return result, marker.read_bytes()

    def runtime_probe(self, year, requested, config):
        text = (ROOT / "scripts" / "run-in-autocad-tests.ps1").read_text(encoding="utf-8-sig")
        function = re.search(r"(?m)^function Get-AcadRuntime\b[^\n]*\n.*?^}", text, re.S).group()
        with tempfile.TemporaryDirectory(prefix="dhcb-host-runtime-") as tmp:
            folder = Path(tmp)
            if config is not None:
                (folder / "acdbmgd.runtimeconfig.json").write_text(config, encoding="utf-8")
            source = folder / "runtime.ps1"
            source.write_text("$ErrorActionPreference = 'Stop'\n" + function + "\n"
                              + f"Get-AcadRuntime {year} $PSScriptRoot '{requested}'\n", encoding="utf-8")
            return subprocess.run([PWSH, "-NoProfile", "-File", str(source)],
                                  capture_output=True, text=True, timeout=30)

    def test_autocad_detects_host_runtime_and_rejects_mismatches(self):
        for year in (2025, 2026):
            for runtime in ("net8", "net10"):
                with self.subTest(year=year, runtime=runtime):
                    config = json.dumps({"runtimeOptions": {"tfm": runtime + ".0"}})
                    auto = self.runtime_probe(year, "Auto", config)
                    self.assertEqual(0, auto.returncode, auto.stdout + auto.stderr)
                    self.assertEqual(runtime, auto.stdout.strip())
                    wrong = self.runtime_probe(year, "net10" if runtime == "net8" else "net8", config)
                    self.assertNotEqual(0, wrong.returncode)
                    self.assertIn("khác runtime host", wrong.stderr)

    def test_autocad_runtime_requires_real_config_and_supported_profile(self):
        for config in (None, "broken JSON", '{}', '{"tfm":"net10.0"}',
                       '{"runtimeOptions":{"tfm":"net9.0"},"note":"net10.0"}'):
            with self.subTest(config=config):
                self.assertNotEqual(0, self.runtime_probe(2026, "Auto", config).returncode)
        self.assertNotEqual(0, self.runtime_probe(2027, "Auto", '{"runtimeOptions":{"tfm":"net8.0"}}').returncode)
        legacy = self.runtime_probe(2022, "Auto", None)
        self.assertEqual(0, legacy.returncode, legacy.stderr)
        self.assertEqual("net48", legacy.stdout.strip())
        self.assertNotEqual(0, self.runtime_probe(2024, "net10", None).returncode)

    def test_native_failure_with_valid_output_preserves_existing_addin(self):
        for name, variable, component in LOOKUPS:
            with self.subTest(script=name, variable=variable):
                result, installed = self.probe(name, variable, "net10.0", 23)
                self.assertEqual(2, result.returncode, result.stdout + result.stderr)
                self.assertIn(component, result.stdout)
                self.assertIn("23", result.stdout)
                self.assertNotIn("FRAMEWORK=", result.stdout)
                self.assertEqual(b"previous installation", installed)

    def test_empty_output_reports_framework_error_without_overwriting(self):
        for name, variable, component in LOOKUPS:
            with self.subTest(script=name, variable=variable):
                result, installed = self.probe(name, variable, None, 0)
                self.assertEqual(2, result.returncode, result.stdout + result.stderr)
                self.assertIn("TargetDir" if variable in {"binDir", "acadOut"} else "TargetFramework", result.stdout)
                self.assertIn(component, result.stdout)
                self.assertEqual(b"previous installation", installed)

    def test_successful_lookup_uses_trimmed_framework(self):
        for name, variable, _ in LOOKUPS:
            with self.subTest(script=name, variable=variable):
                result, installed = self.probe(name, variable, "  net10.0  ", 0)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertIn("FRAMEWORK=net10.0\n", result.stdout)
                self.assertNotEqual(b"previous installation", installed)


if __name__ == "__main__":
    unittest.main()
