"""Test cho scripts/boc-khoi-luong-ong-gio.py — bóc ống gió từ cặp Line song song + tag RxC → .xlsx.

Bản vẽ giả: ống SEAD 700x300 nằm ngang dài 5 m, ống RAD 400x250 đứng dài 3 m, một cặp rộng 1650 mm không
có tag (phải rơi vào "Chưa xác định"), cùng các đường nhiễu (tim ống, đường ngắn, đường xiên, ngoài khung).
"""

from __future__ import annotations

import collections
import importlib.util
import io
import json
import re
import tempfile
import unittest
import zipfile
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest import mock
from xml.etree import ElementTree

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "boc-khoi-luong-ong-gio.py"
_spec = importlib.util.spec_from_file_location("boc_ong_gio", SCRIPT)
bog = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(bog)

NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
BBOX = (-1000, -1000, 20000, 20000)


def line(handle, x1, y1, x2, y2, layer="M-HVAC-DUCT-1"):
    return {"handle": handle, "type": "Line", "layer": layer,
            "start": {"x": x1, "y": y1, "z": 0}, "end": {"x": x2, "y": y2, "z": 0}}


def text(t, x, y, layer="M-HVAC-DUCT-IDEN"):
    return {"type": "MText", "layer": layer, "text": t, "x": x, "y": y}


ENTITIES = [
    line("A1", 0, 0, 5000, 0), line("A2", 5000, 700, 0, 700),           # SEAD 700x300, cạnh 2 vẽ ngược chiều
    line("B1", 10000, 3000, 10000, 0), line("B2", 10400, 0, 10400, 3000),  # RAD 400x250 đứng, cạnh 1 vẽ từ trên xuống
    line("C1", 0, 15000, 2000, 15000), line("C2", 0, 16650, 2000, 16650),   # rộng 1650, không có tag
    line("N1", 0, 350, 5000, 350, layer="M-HVAC-DUCT-CNTR-1"),            # tim ống — bỏ
    line("N2", 0, 5000, 30, 5000),                                         # quá ngắn
    line("N3", 0, 8000, 3000, 9000),                                       # xiên, không có cặp
    line("N4", 50000, 0, 55000, 0),                                        # ngoài khung
    line("N5", 3000, 9000, 3000, 9030),                                    # ngắn
    line("N6", 0, 12000, 5000, 12000), line("N7", 4900, 12300, 9000, 12300),  # song song nhưng chồng quá ít
    line("N8", 0, 19000, 5000, 19000), line("N9", 0, 19020, 5000, 19020),     # quá sát (< 80 mm)
    {"handle": "P1", "type": "Polyline", "layer": "M-HVAC-DUCT-1"},
    line("X1", 0, 0, 100, 0, layer="A-WALL"),
]
TEXTS = [
    text("{\\fArial|b1;SEAD - 700x300}", 2500, 1200),
    text("\\pxql;RAD - 400x250", 10900, 1500),
    text("PAG+OBD 500X500\\PBOG=FFL+1900", 2500, 900, layer="VHT_AC.FAD.LINE"),  # miệng gió, không phải ống
    text("SEAD - 700x300", 2500, 800, layer="AC.6 Chi tiet$0$M-HVAC-DUCT"),      # chi tiết xref ghép
    text("SAD - 400x250", 90000, 90000),                                          # ngoài khung
    text("Ghi chú chung", 0, 0),
]
GEOMETRY = [
    {"type": "BlockReference", "layer": "M-HVAC-DUCT-1", "position": {"x": 100, "y": 100},
     "blockName": "VHT_M_DF_Rec_C_Elbow_Square - VHT_Type1-11064867-MẶT BẰNG"},
    {"type": "BlockReference", "layer": "M-HVAC-DUCT-1", "position": {"x": 200, "y": 100},
     "blockName": "VHT_M_DF_Rec_C_Elbow_Square - VHT_Type1-2"},
    {"type": "BlockReference", "layer": "M-HVAC-DUCT-4", "position": {"x": 300, "y": 100}, "blockName": "DSupport"},
    {"type": "BlockReference", "layer": "M-HVAC-DUCT-4", "position": {"x": 99999, "y": 100}, "blockName": "Rec_Cap_End"},
    {"type": "BlockReference", "layer": "VHT_AC.EQUIP", "position": {"x": 300, "y": 100}, "blockName": "DAMPER"},
    {"type": "BlockReference", "layer": "M-HVAC-DUCT-1", "position": None, "blockName": "Rec_Cap_End"},
    {"type": "Polyline", "layer": "M-HVAC-DUCT-1"},
]


def sheet_cells(path: Path, index: int) -> dict:
    with zipfile.ZipFile(path) as z:
        root = ElementTree.fromstring(z.read(f"xl/worksheets/sheet{index}.xml"))
    cells = {}
    for c in root.iter(f"{{{NS['m']}}}c"):
        f, v, t = c.find("m:f", NS), c.find("m:v", NS), c.find("m:is/m:t", NS)
        cells[c.get("r")] = "=" + f.text if f is not None else (float(v.text) if v is not None else t.text)
    return cells


class ParseTests(unittest.TestCase):
    def test_clean_mtext(self) -> None:
        self.assertEqual(bog.clean_mtext("\\pxql;{\\fArial|b1|i0;SEAD - 700x300}\\PBOD=FFL+3000"),
                         "SEAD - 700x300 | BOD=FFL+3000")

    def test_parse_tags_chi_lay_ong_trong_khung(self) -> None:
        tags = bog.parse_tags(TEXTS, BBOX)
        self.assertEqual([(t["sys"], t["W"], t["H"]) for t in tags], [("SEAD", 700, 300), ("RAD", 400, 250)])
        self.assertEqual(len(bog.parse_tags(TEXTS)), 3)  # không khung → lấy cả tag ngoài

    def test_duct_segments_bo_nhieu(self) -> None:
        handles = {s["handle"] for s in bog.duct_segments(ENTITIES, BBOX)}
        self.assertEqual(handles, {"A1", "A2", "B1", "B2", "C1", "C2", "N3", "N6", "N7", "N8", "N9"})

    def test_direction_chuan_hoa_chieu(self) -> None:
        segs = {s["handle"]: s for s in bog.duct_segments(ENTITIES, BBOX)}
        self.assertEqual(bog.direction(segs["A2"]), (1.0, 0.0))
        self.assertEqual(bog.direction(segs["B1"]), (0.0, 1.0))

    def test_parse_bbox(self) -> None:
        self.assertEqual(bog.parse_bbox("10,20,0,5"), (0, 5, 10, 20))
        with self.assertRaises(ValueError):
            bog.parse_bbox("1,2,3")

    def test_load_json(self) -> None:
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "t.json"
            p.write_text(json.dumps({"texts": [1]}), encoding="utf-8")
            self.assertEqual(bog.load_json(str(p), "texts"), [1])
            p.write_text(json.dumps([2]), encoding="utf-8")
            self.assertEqual(bog.load_json(str(p), "entities"), [2])


class GeometryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.pairs = bog.pair_segments(bog.duct_segments(ENTITIES, BBOX))
        self.assigned = bog.assign_sizes(self.pairs, bog.parse_tags(TEXTS, BBOX))

    def test_ghep_cap(self) -> None:
        got = sorted((p["a"], p["b"], p["w"], round(p["len"])) for p in self.pairs)
        self.assertEqual(got, [("A1", "A2", 700, 5000), ("B1", "B2", 400, 3000), ("C1", "C2", 1650, 2000)])

    def test_chon_canh_gan_nhat(self) -> None:
        segs = bog.duct_segments([line("a", 0, 0, 1000, 0), line("b", 0, 900, 1000, 900), line("c", 0, 500, 1000, 500)])
        self.assertEqual([(p["a"], p["b"]) for p in bog.pair_segments(segs)], [("a", "c"), ("b", "c")])

    def test_gan_kich_thuoc(self) -> None:
        by = {p["a"]: p for p in self.assigned}
        self.assertEqual((by["A1"]["sys"], by["A1"]["W"], by["A1"]["H"]), ("SEAD", 700, 300))
        self.assertEqual((by["B1"]["sys"], by["B1"]["W"]), ("RAD", 400))
        self.assertIsNone(by["C1"]["sys"])
        far = bog.assign_sizes(self.pairs, bog.parse_tags(TEXTS, BBOX), radius=10)
        self.assertTrue(all(p["sys"] is None for p in far))

    def test_danh_so(self) -> None:
        extra = dict(self.assigned[0], sys="XAD", stt=None)
        numbered = bog.number_segments(self.assigned + [extra])
        self.assertEqual([(a["stt"], a["code"]) for a in numbered], [(1, "SEAD-001"), (2, "RAD-001"), (3, "XAD-001")])

    def test_dem_phu_kien(self) -> None:
        c = bog.count_fittings(GEOMETRY, BBOX)
        self.assertEqual(c, collections.Counter({"Rec_C_Elbow_Square": 2, "DSupport": 1}))


class XlsxTests(unittest.TestCase):
    def test_col_name(self) -> None:
        self.assertEqual([bog.col_name(n) for n in (1, 26, 27, 52)], ["A", "Z", "AA", "AZ"])

    def test_sheet_xml_cac_loai_o(self) -> None:
        xml = bog.sheet_xml([["a&b", 1, 2.5, None, bog.F("SUM(B1:C1)")]], [10] * 5, {1: bog.BOLD})
        self.assertIn('<c r="A1" s="1" t="inlineStr"><is><t xml:space="preserve">a&amp;b</t></is></c>', xml)
        self.assertIn('<c r="B1" s="1"><v>1</v></c>', xml)
        self.assertIn('<c r="E1" s="1"><f>SUM(B1:C1)</f></c>', xml)
        self.assertNotIn('r="D1"', xml)

    def test_workbook_day_du_va_cong_thuc_dung_dong(self) -> None:
        assigned = bog.assign_sizes(bog.pair_segments(bog.duct_segments(ENTITIES, BBOX)), bog.parse_tags(TEXTS, BBOX))
        numbered = bog.number_segments(assigned)
        sheets = bog.build_sheets(numbered, [a for a in assigned if not a["sys"]],
                                  bog.count_fittings(GEOMETRY, BBOX), "test.dwg", 0.05)
        with tempfile.TemporaryDirectory() as d:
            out = Path(d) / "kl.xlsx"
            bog.write_xlsx(out, sheets)
            with zipfile.ZipFile(out) as z:
                self.assertIsNone(z.testzip())
                wb = z.read("xl/workbook.xml").decode()
            self.assertEqual(re.findall(r'<sheet name="([^"]+)"', wb),
                             ["Tổng hợp", "Chi tiết", "Phụ kiện", "Chưa xác định", "Ghi chú"])
            tot, det = sheet_cells(out, 1), sheet_cells(out, 2)
            self.assertEqual(tot["I3"], 0.05)
            self.assertEqual((tot["B6"], tot["C7"], tot["D7"]), ("Hút khói (Smoke Exhaust Air Duct)", "SEAD", "700x300"))
            self.assertEqual(tot["I7"], "=ROUND(G7*H7,2)")
            self.assertEqual(tot["J7"], "=ROUND(I7*(1+$I$3),2)")
            self.assertEqual((det["B4"], det["G4"], det["H4"]), ("SEAD-001", 5.0, "=2*(E4+F4)/1000"))
            self.assertEqual(det["G6"], "=SUM(G4:G5)")
            self.assertEqual(sheet_cells(out, 3)["D4"], 2.0)
            self.assertEqual(sheet_cells(out, 4)["B4"], 1650.0)


class FakeAgent:
    def __init__(self, fail_geometry_once=True, bad=None):
        self.calls, self.fail, self.bad = [], fail_geometry_once, bad

    def request(self, app, method, path, payload, timeout=0):
        q = payload["query"]
        self.calls.append(q)
        if q == self.bad:
            return {"error": "hỏng"}
        if q == "entities":
            off = payload["params"]["offset"]
            page = ENTITIES[off:off + 10]
            more = off + 10 < len(ENTITIES)
            return {"entities": page + ([{"handle": "BR", "type": "BlockReference", "layer": "M-HVAC-DUCT-1"}] if off == 0 else []),
                    "hasMore": more, "nextOffset": off + 10 if more else None}
        if q == "text":
            return {"texts": TEXTS}
        if q == "entity_geometry":
            if self.fail:
                self.fail = False
                return {"error": "Timeout"}
            return {"entities": GEOMETRY[:1]}
        return {"filename": "C:\\du an\\Tang 2.dwg"}


class LiveAndMainTests(unittest.TestCase):
    def test_fetch_live_phan_trang_va_thu_lai(self) -> None:
        agent = FakeAgent()
        entities, texts, geometry, name = bog.fetch_live(agent, pause=0)
        self.assertEqual(len(entities), len(ENTITIES) + 1)
        self.assertEqual(agent.calls.count("entity_geometry"), 2)
        self.assertEqual((len(texts), len(geometry), name), (len(TEXTS), 1, "Tang 2.dwg"))

    def test_fetch_live_bao_loi(self) -> None:
        for bad in ("entities", "text"):
            with self.subTest(bad=bad), self.assertRaises(RuntimeError):
                bog.fetch_live(FakeAgent(bad=bad), pause=0)

    def test_main_tu_file_json(self) -> None:
        with tempfile.TemporaryDirectory() as d:
            e, t, g = Path(d) / "e.json", Path(d) / "t.json", Path(d) / "g.json"
            e.write_text(json.dumps(ENTITIES), encoding="utf-8")
            t.write_text(json.dumps({"texts": TEXTS}), encoding="utf-8")
            g.write_text(json.dumps({"entities": GEOMETRY}), encoding="utf-8")
            out = Path(d) / "kl.xlsx"
            buf = io.StringIO()
            with redirect_stdout(buf):
                code = bog.main(["--entities", str(e), "--texts", str(t), "--geometry", str(g),
                                 "--bbox=-1000,-1000,20000,20000", "-o", str(out)])
            self.assertEqual(code, 0)
            self.assertTrue(out.is_file())
            self.assertIn("2 đoạn, 8.00 m", buf.getvalue())
            self.assertIn("1 cặp chưa xác định (2.00 m)", buf.getvalue())
            self.assertIn("e.json", sheet_cells(out, 1)["A2"])

            with redirect_stderr(io.StringIO()) as err:
                self.assertEqual(bog.main(["--entities", str(e), "-o", str(out)]), 2)
                self.assertEqual(bog.main(["--entities", str(Path(d) / "khong-co.json"), "--texts", str(t), "-o", str(out)]), 2)
                self.assertEqual(bog.main(["--entities", str(e), "--texts", str(t), "--layer-prefix", "KHONG-CO", "-o", str(out)]), 1)
            self.assertIn("không có --bbox", err.getvalue())

    def test_main_doc_truc_tiep_qua_bridge(self) -> None:
        with tempfile.TemporaryDirectory() as d, mock.patch.object(bog.time, "sleep"):
            out = Path(d) / "kl.xlsx"
            with redirect_stdout(io.StringIO()):
                self.assertEqual(bog.main(["--bbox=-1000,-1000,20000,20000", "-o", str(out)], agent=FakeAgent()), 0)
            self.assertEqual(sheet_cells(out, 1)["A2"], "Nguồn: Tang 2.dwg")
            with redirect_stderr(io.StringIO()):
                self.assertEqual(bog.main(["-o", str(out)], agent=FakeAgent(bad="text")), 2)


if __name__ == "__main__":
    unittest.main()
