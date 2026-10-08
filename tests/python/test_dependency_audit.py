"""Run the dependency audit with native stubs to catch mismatched MSBuild asset profiles."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
BASH = shutil.which("bash")
PROFILES = [("2022", "net48"), ("2023", "net48"), ("2024", "net48"),
            ("2025", "net8"), ("2025", "net10"), ("2026", "net8"),
            ("2026", "net10"), ("2027", "net10")]


@unittest.skipIf(os.name == "nt" or not BASH, "Native stub executables require a POSIX shell")
class DependencyAuditTests(unittest.TestCase):
    def execute(self, vulnerable=False, native_failure=False, python_failure=False):
        with tempfile.TemporaryDirectory(prefix="dhcb-audit-") as tmp:
            folder = Path(tmp)
            (folder / "scripts").mkdir()
            (folder / "bin").mkdir()
            shutil.copy2(ROOT / "scripts/check-vulnerable.sh", folder / "scripts/check-vulnerable.sh")
            stub = folder / "bin/dotnet"
            stub.write_text("#!" + sys.executable + "\n" + r'''import json, os, sys
from pathlib import Path
properties = {name: os.environ.get(name) for name in
              ("RevitVersion", "AcadVersion", "AcadRuntime", "EnableWindowsTargeting")}
event = {"tool": "dotnet", "args": sys.argv[1:], "properties": properties}
with open(os.environ["AUDIT_LOG"], "a") as stream:
    stream.write(json.dumps(event) + "\n")
key = properties["AcadVersion"] + "-" + properties["AcadRuntime"]
asset = Path(os.environ["AUDIT_FIXTURE"]) / (key + ".assets")
if sys.argv[1] == "restore":
    asset.write_text(json.dumps(properties))
else:
    if not asset.is_file() or json.loads(asset.read_text()) != properties:
        print("Restore/list profile mismatch", file=sys.stderr)
        sys.exit(31)
    if os.environ.get("AUDIT_NATIVE_FAILURE") == "1":
        sys.exit(23)
    projects = []
    if os.environ.get("AUDIT_VULNERABLE") == "1" and key == "2025-net10":
        projects = [{"path": "host.csproj", "frameworks": [{"framework": "net10.0-windows",
                     "topLevelPackages": [{"id": "AffectedPackage", "resolvedVersion": "1.0.0",
                     "vulnerabilities": [{"advisoryurl": "https://example.test/advisory"}]}]}]}]
    print(json.dumps({"projects": projects}))
''', encoding="utf-8")
            stub.chmod(0o755)
            python_stub = folder / "bin/python3"
            python_stub.write_text("#!" + sys.executable + "\n" + r'''import json, os, sys
if sys.argv[1:3] == ["-m", "pip_audit"]:
    with open(os.environ["AUDIT_LOG"], "a") as stream:
        stream.write(json.dumps({"tool": "pip-audit", "args": sys.argv[1:]}) + "\n")
    sys.exit(int(os.environ.get("AUDIT_PYTHON_FAILURE", "0")))
os.execv(sys.executable, [sys.executable, *sys.argv[1:]])
''', encoding="utf-8")
            python_stub.chmod(0o755)
            log = folder / "calls.jsonl"
            env = {**os.environ, "PATH": str(folder / "bin") + os.pathsep + os.environ["PATH"],
                   "AUDIT_LOG": str(log), "AUDIT_FIXTURE": str(folder),
                   "AUDIT_VULNERABLE": str(int(vulnerable)),
                   "AUDIT_NATIVE_FAILURE": str(int(native_failure)),
                   "AUDIT_PYTHON_FAILURE": str(int(python_failure)),
                   # Poison the inherited defaults; the script must select every profile itself.
                   "RevitVersion": "2024", "AcadVersion": "2024", "AcadRuntime": "net48"}
            result = subprocess.run([BASH, str(folder / "scripts/check-vulnerable.sh")], env=env,
                                    capture_output=True, text=True, timeout=30)
            events = [json.loads(line) for line in log.read_text().splitlines()]
            return result, events

    def assert_profiles(self, events):
        native = [event for event in events if event["tool"] == "dotnet"]
        self.assertEqual(16, len(native))
        for index, (year, runtime) in enumerate(PROFILES):
            restore, listing = native[index * 2:index * 2 + 2]
            self.assertEqual("restore", restore["args"][0])
            self.assertEqual("list", listing["args"][0])
            self.assertEqual(restore["properties"], listing["properties"])
            self.assertEqual({"RevitVersion": year, "AcadVersion": year, "AcadRuntime": runtime,
                              "EnableWindowsTargeting": "true"}, listing["properties"])
            self.assertIn("--no-restore", listing["args"])
            self.assertFalse(any(arg.startswith("-p:") for arg in listing["args"]))

    def test_restore_and_list_audit_all_eight_identical_profiles(self):
        result, events = self.execute()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assert_profiles(events)
        self.assertEqual("pip-audit", events[-1]["tool"])

    def test_vulnerability_in_alternate_runtime_fails_but_finishes_other_profiles(self):
        result, events = self.execute(vulnerable=True)
        self.assertEqual(1, result.returncode, result.stderr)
        self.assert_profiles(events)
        self.assertIn("AffectedPackage 1.0.0", result.stdout)
        self.assertIn("https://example.test/advisory", result.stdout)
        self.assertEqual("pip-audit", events[-1]["tool"])

    def test_native_package_listing_failure_cannot_pass_the_audit(self):
        result, events = self.execute(native_failure=True)
        self.assertEqual(23, result.returncode)
        self.assertEqual(2, len(events))
        self.assertFalse(any(event["tool"] == "pip-audit" for event in events))

    def test_python_audit_failure_also_fails_the_gate(self):
        result, events = self.execute(python_failure=True)
        self.assertEqual(1, result.returncode)
        self.assert_profiles(events)


if __name__ == "__main__":
    unittest.main()
