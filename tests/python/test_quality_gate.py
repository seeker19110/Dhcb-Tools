"""Execute the aggregate CI gate with failed, skipped and cancelled dependency results."""
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import textwrap
import unittest

ROOT = Path(__file__).resolve().parents[2]


class QualityGateTests(unittest.TestCase):
    def setUp(self):
        self.workflow = (ROOT / ".github/workflows/tests.yml").read_text(encoding="utf-8")
        self.gate = self.workflow.split("  quality-gate:\n", 1)[1]
        match = re.search(r"        run: \|\n((?: {10}.*\n|\n)*)", self.gate)
        block = textwrap.dedent(match.group(1)).strip()
        self.script = block.split("\n", 1)[1].rsplit("\nPY", 1)[0]

    def test_gate_always_runs_and_covers_every_other_job(self):
        jobs = set(re.findall(r"^  ([\w-]+):$", self.workflow.split("jobs:\n", 1)[1], re.M))
        needs = set(re.search(r"needs: \[(.*?)\]", self.gate).group(1).split(", "))
        self.assertEqual(jobs - {"quality-gate"}, needs)
        self.assertIn("if: ${{ always() }}", self.gate)
        self.assertIn("NEEDS_JSON: ${{ toJSON(needs) }}", self.gate)

    def execute(self, results):
        return subprocess.run([sys.executable, "-c", self.script],
                              env={**os.environ, "NEEDS_JSON": json.dumps(results)},
                              capture_output=True, text=True, timeout=15)

    def test_gate_requires_success_for_every_job(self):
        names = ("logic-tests", "check-build", "build-wpf-windows", "build-autocad-windows")
        success = {name: {"result": "success"} for name in names}
        result = self.execute(success)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("All validation jobs passed", result.stdout)
        for name in names:
            for status in ("failure", "cancelled", "skipped"):
                with self.subTest(name=name, status=status):
                    result = self.execute({**success, name: {"result": status}})
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn(name, result.stderr)
                    self.assertIn(status, result.stderr)
        self.assertNotEqual(0, self.execute({}).returncode)


if __name__ == "__main__":
    unittest.main()
