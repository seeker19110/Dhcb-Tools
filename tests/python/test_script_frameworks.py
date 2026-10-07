"""Chạy các cổng MSBuild thật của script, chặn DLL cũ khi truy vấn framework lỗi."""

from pathlib import Path
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
)


@unittest.skipUnless(PWSH, "Cần pwsh để thực thi script PowerShell")
class ScriptFrameworkTests(unittest.TestCase):
    def probe(self, name, variable, output, exit_code):
        text = (ROOT / "scripts" / name).read_text(encoding="utf-8-sig")
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
            marker = folder / "existing-addin.dll"
            marker.write_bytes(b"previous installation")
            script = folder / "probe.ps1"
            script.write_text("$ErrorActionPreference = 'Stop'\n" + stop + "\n" + stub + block
                              + "Set-Content existing-addin.dll overwritten\n"
                              + f"Write-Output ('FRAMEWORK=' + ${variable})\n", encoding="utf-8")
            result = subprocess.run([PWSH, "-NoProfile", "-File", str(script)], cwd=tmp,
                                    capture_output=True, text=True, timeout=30)
            return result, marker.read_bytes()

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
                self.assertIn("TargetFramework", result.stdout)
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
