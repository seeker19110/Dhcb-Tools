import contextlib
import csv
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("dhcb_pilot", ROOT / "scripts" / "dhcb_pilot.py")
pilot = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(pilot)


class PilotTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.path = Path(self.tmp.name) / "pilot.csv"

    def write(self, rows):
        with self.path.open("w", encoding="utf-8-sig", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=pilot.FIELDS)
            writer.writeheader()
            writer.writerows(rows)

    def row(self, **changes):
        row = dict(zip(pilot.FIELDS, ("2026-10-08", "u01", "p01", "AutoCAD", "TextReplace", "10", "2", "true", "0")))
        row.update(changes)
        return row

    def run_main(self, *args):
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            return pilot.main([str(self.path), *args])

    def test_no_measurement_is_not_presented_as_savings(self):
        self.write([{"Lenh": "TextReplace"}])
        report = pilot.analyze(self.path)
        self.assertEqual("no-data", report["status"])
        self.assertEqual(0, report["samples"])
        self.assertEqual(1, report["placeholders"])
        self.assertIn("Chưa có dữ liệu", pilot.render(report))
        self.assertEqual(0, self.run_main())

    def test_aggregates_time_including_failed_tasks_without_personal_identifiers(self):
        self.write([self.row(), self.row(HoanThanh="không", PhutDungToolGomCauHinhVaSua="12", SoLoi="1"),
                    self.row(Lenh="<unsafe>", PhutLamTay="0", HoanThanh="có")])
        report = pilot.analyze(self.path)
        item = report["commands"]["AutoCAD/TextReplace"]
        self.assertEqual(2, item["samples"])
        self.assertEqual(1, item["completed"])
        self.assertEqual(1, item["errors"])
        self.assertEqual(30, item["savedPercent"])
        self.assertEqual(.5, item["completionRate"])
        self.assertIsNone(report["commands"]["AutoCAD/<unsafe>"]["savedPercent"])
        rendered = pilot.render(report)
        self.assertIn("&lt;unsafe&gt;", rendered)
        self.assertNotIn("u01", rendered)
        self.assertNotIn("p01", json.dumps(report))
        output = self.path.parent / "reports" / "pilot.html"
        self.assertEqual(0, self.run_main("--output", str(output)))
        self.assertIn("Thí điểm", output.read_text(encoding="utf-8"))
        output = output.with_suffix(".json")
        self.assertEqual(0, self.run_main("--output", str(output)))
        self.assertEqual(3, json.loads(output.read_text(encoding="utf-8"))["samples"])

    def test_invalid_rows_are_reported_and_block_a_clean_conclusion(self):
        self.write([self.row(Ngay="bad"), self.row(SoLoi="-1"), self.row(PhutLamTay="nan"),
                    self.row(HoanThanh="maybe"), self.row(Lenh=""), self.row(SoLoi="1.5")])
        report = pilot.analyze(self.path)
        self.assertEqual("invalid", report["status"])
        self.assertEqual(6, len(report["errors"]))
        self.assertIn("Dòng 2", pilot.render(report))
        self.assertEqual(1, self.run_main())

    def test_bad_schema_extra_columns_and_io_failures_are_explicit(self):
        self.path.write_text("Lenh\nTextReplace\n", encoding="utf-8")
        self.assertEqual(2, self.run_main())
        self.write([self.row()])
        with self.path.open("a", encoding="utf-8") as stream:
            stream.write("2026-10-08,u,p,AutoCAD,Test,10,2,true,0,extra\n")
        self.assertEqual(1, self.run_main())
        self.assertEqual(2, self.run_main("--output", str(self.path)))
        self.assertEqual(2, self.run_main("--output", str(self.path.with_suffix(".txt"))))
        directory = self.path.parent / "not-a-file.json"
        directory.mkdir()
        self.assertEqual(2, self.run_main("--output", str(directory)))
        disguised = self.path.with_suffix(".json")
        disguised.write_text(self.path.read_text(encoding="utf-8"), encoding="utf-8")
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(2, pilot.main([str(disguised), "--output", str(disguised)]))
        self.path.unlink()
        self.assertEqual(2, self.run_main())
