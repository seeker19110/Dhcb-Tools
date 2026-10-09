"""Exercise acceptance preflight against native failures without starting AutoCAD."""
from pathlib import Path
import shutil
import os
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
PWSH = shutil.which("pwsh")
HELPER = ROOT / "tools/acceptance/HostPreflight.ps1"


def quote(value):
    return "'" + str(value).replace("'", "''") + "'"


@unittest.skipUnless(PWSH, "Acceptance preflight checks require PowerShell")
class AcceptanceScriptTests(unittest.TestCase):
    def execute(self, code, folder):
        script = folder / "probe.ps1"
        script.write_text("$ErrorActionPreference = 'Stop'\n. " + quote(HELPER)
                          + "\n" + code, encoding="utf-8")
        return subprocess.run([PWSH, "-NoProfile", "-File", str(script)],
                              capture_output=True, text=True, timeout=30)

    @unittest.skipUnless(os.name == "nt", "Windows PATHEXT behavior")
    def test_incomplete_pathext_is_fixed_only_in_child_process(self):
        with tempfile.TemporaryDirectory(prefix="dhcb-acceptance-") as tmp:
            before = os.environ.get("PATHEXT")
            code = ("$env:PATHEXT = '.CPL'\nInitialize-AcceptanceNativeTools\n"
                    "& " + quote(sys.executable) + " -c 'print(123)'\n"
                    "if ($LASTEXITCODE -ne 0) { throw 'native invocation failed' }\n")
            result = self.execute(code, Path(tmp))
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertIn("123", result.stdout)
            self.assertEqual(before, os.environ.get("PATHEXT"))

    def test_target_lookup_rejects_native_errors_empty_and_missing_outputs(self):
        with tempfile.TemporaryDirectory(prefix="dhcb-acceptance-") as tmp:
            folder = Path(tmp)
            for output, exit_code, valid in [(str(folder), 23, False), ("", 23, False),
                                             ("", 0, False), (str(folder / "missing"), 0, False),
                                             (str(folder), 0, True)]:
                with self.subTest(output=output, exit_code=exit_code):
                    native = "import sys; print(" + repr(output) + "); sys.exit(" + str(exit_code) + ")"
                    code = ("function FakeDotNet { & " + quote(sys.executable) + " -c " + quote(native) + " }\n"
                            + "$target = Get-AcceptanceTargetDir project FakeDotNet\n"
                            + "Write-Output ('TARGET_READY:' + $target)\n")
                    result = self.execute(code, folder)
                    self.assertEqual(valid, result.returncode == 0, result.stdout + result.stderr)
                    self.assertEqual(valid, "TARGET_READY:" in result.stdout)

    def test_missing_build_dll_is_rejected_before_native_host_launch(self):
        with tempfile.TemporaryDirectory(prefix="dhcb-acceptance-") as tmp:
            folder = Path(tmp)
            result = self.execute("Assert-AcceptanceOutput " + quote(folder / "missing.dll")
                                  + "\nWrite-Output 'HOST_LAUNCH_REACHED'\n", folder)
            self.assertNotEqual(0, result.returncode)
            self.assertNotIn("HOST_LAUNCH_REACHED", result.stdout)
            self.assertIn("build output missing", result.stderr)

    def test_missing_host_input_fails_before_build_or_output_creation(self):
        with tempfile.TemporaryDirectory(prefix="dhcb-acceptance-") as tmp:
            folder = Path(tmp)
            result = self.execute("Get-AcceptanceHost " + quote(folder) + " " + quote(sys.executable)
                                  + "\nWrite-Output 'BUILD_REACHED'\n", folder)
            self.assertNotEqual(0, result.returncode)
            self.assertNotIn("BUILD_REACHED", result.stdout)
            self.assertIn("acceptance input missing", result.stderr)

    def test_every_fixture_uses_evaluated_2026_net10_outputs_before_writing(self):
        for name in ("run-query.ps1", "run-pdf.ps1", "run-batch.ps1"):
            with self.subTest(name=name):
                text = (ROOT / "tools/acceptance" / name).read_text(encoding="utf-8-sig")
                self.assertNotRegex(text, r"bin[\\/]Release[\\/]")
                self.assertIn("-p:AcadVersion=2026 -p:AcadRuntime=net10", text)
                self.assertLess(text.index("Get-AcceptanceHost"), text.index(" build "))
                self.assertLess(text.index("Assert-AcceptanceOutput"), text.index("New-Item"))
        helper = HELPER.read_text(encoding="utf-8")
        self.assertIn("-getProperty:TargetDir", helper)
        self.assertIn("-p:AcadVersion=2026 -p:AcadRuntime=net10", helper)


if __name__ == "__main__":
    unittest.main()
