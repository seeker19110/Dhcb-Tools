"""Test cho scripts/boc-khoi-luong-ong-gio.py — dựng tuyến ống gió từ cặp Line + tag RxC, cắt đốt tại mối nối,
chia theo quy tắc BKL (L max theo nẹp TDC 1110 / C 1180, dung sai 80, đoạn vụn < 300 chia đôi, chân rẽ tới 1400),
box bít đầu, co.

Bản vẽ giả:
- SEAD 700x300 ngang dài 3620, nẹp TDC ở x = 1110 và 2220, đầu trái nối co 90°, đầu phải có nắp bịt
  → 1110 + 1110 + box 1400; trong hộp bao co có hai nét vẽ song song phải bị bỏ;
- RAD 400x250 đứng dài 3000, nẹp C ở y = 1180, đầu trên nối co 45° → 1180 + (khoảng 1820 chia) 1180 + 640;
- RAD 400x250 dài 1300 giữa hai côn, có miệng gió bên hông, không mối nối → nhận nẹp C theo tuyến cùng kích
  thước, được giữ 1300 (≤ 1400) thay vì chia;
- FAD 200x150 hai mảnh cách nhau 150 mm, mối nối TDC ở x = 1070 nằm trong khe → một tuyến, 1070 + 930;
- một cặp rộng 1650 không có tag ("Chưa xác định"), một co không nối tuyến nào, cùng các đường nhiễu.
"""

from __future__ import annotations

import collections
import importlib.util
import io
import json
import math
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
BBOX = (-1000, -1000, 25000, 25000)


def line(handle, x1, y1, x2, y2, layer="M-HVAC-DUCT-1"):
    return {"handle": handle, "type": "Line", "layer": layer,
            "start": {"x": x1, "y": y1, "z": 0}, "end": {"x": x2, "y": y2, "z": 0}}


def text(t, x, y, layer="M-HVAC-DUCT-IDEN"):
    return {"type": "MText", "layer": layer, "text": t, "x": x, "y": y}


def block(name, x0, y0, x1=None, y1=None, layer="M-HVAC-DUCT-1"):
    e = {"type": "BlockReference", "layer": layer, "position": {"x": x0, "y": y0}, "blockName": name}
    if x1 is not None:
        e["extentsMin"], e["extentsMax"] = {"x": x0, "y": y0}, {"x": x1, "y": y1}
    return e


TDC = "VHT_M_DF_Rec_TDC_Union - VHT_Type1-1"
ENTITIES = [
    # SEAD: cạnh dưới ngắt tại mối nối, cạnh trên vẽ ngược chiều
    line("A1", 0, 0, 1100, 0), line("A3", 1120, 0, 2210, 0), line("A5", 2230, 0, 3620, 0),
    line("A2", 1100, 700, 0, 700), line("A4", 2210, 700, 1120, 700), line("A6", 3620, 700, 2230, 700),
    line("B1", 10000, 3000, 10000, 0), line("B2", 10400, 0, 10400, 3000),   # RAD đứng, cạnh 1 vẽ từ trên xuống
    line("R1", 13000, 0, 13000, 1300), line("R2", 13400, 0, 13400, 1300),   # RAD có miệng gió bên hông
    line("F1", 0, 15000, 1000, 15000), line("F2", 0, 15200, 1000, 15200),   # FAD mảnh 1
    line("F3", 1150, 15000, 2000, 15000), line("F4", 1150, 15200, 2000, 15200),  # FAD mảnh 2
    line("C1", 0, 19000, 2000, 19000), line("C2", 0, 20650, 2000, 20650),   # rộng 1650, không có tag
    line("N1", 0, 350, 4000, 350, layer="M-HVAC-DUCT-CNTR-1"),            # tim ống — bỏ
    line("N2", 0, 5000, 30, 5000),                                         # quá ngắn
    line("N3", 0, 8000, 3000, 9000),                                       # xiên, không có cặp
    line("N4", 50000, 0, 55000, 0),                                        # ngoài khung
    line("N6", 0, 12000, 5000, 12000), line("N7", 4900, 12300, 9000, 12300),  # song song nhưng chồng quá ít
    line("N8", 0, 23000, 5000, 23000), line("N9", 0, 23020, 5000, 23020),     # quá sát (< 80 mm)
    line("E1", -650, 100, -50, 100), line("E2", -650, 600, -50, 600),         # nét vẽ trong co — bỏ
    {"handle": "P1", "type": "Polyline", "layer": "M-HVAC-DUCT-1"},
    line("X1", 0, 0, 100, 0, layer="A-WALL"),
]
TEXTS = [
    text("{\\fArial|b1;SEAD - 700x300}", 2500, 1200),
    text("\\pxql;RAD - 400x250", 10900, 1500),
    text("FAD - 200x150", 1000, 15600),
    text("PAG+OBD 500X500\\PBOG=FFL+1900", 2500, 900, layer="VHT_AC.FAD.LINE"),  # miệng gió, không phải ống
    text("SEAD - 700x300", 2500, 800, layer="AC.6 Chi tiet$0$M-HVAC-DUCT"),      # chi tiết xref ghép
    text("SAD - 400x250", 90000, 90000),                                          # ngoài khung
    text("Ghi chú chung", 0, 0),
]
JOINTS = [
    block(TDC, 1096, -30, 1124, 730), block(TDC, 2206, -30, 2234, 730),
    block("VHT_M_DF_Rec_C_Union - VHT_Type1-9", 9980, 1166, 10420, 1194),
    block(TDC, 1070, 15100),                                         # không có hộp bao → dùng điểm chèn
    block(TDC, 99999, 0),                                            # ngoài khung
    {"type": "BlockReference", "layer": "M-HVAC-DUCT-1", "blockName": "Rec_C_Union"},  # không có toạ độ
]
FITS = [
    block("VHT_M_DF_Rec_C_Elbow_Square - VHT_Type1-11064867-MẶT BẰNG", -700, 0, 0, 700),
    block("VHT_M_DF_Rec_Cap_End - VHT_Type1-10820628", 3620, 0, 3645, 700),
    block("VHT_M_DF_Rec_C_Elbow_45 - VHT_Type _1W_-11000371", 9900, 3000, 10500, 3600),
    block("VHT_M_DF_Rec_C_Elbow_Square - VHT_Type1-2", 20000, 20000, 20500, 20500),  # không nối tuyến nào
    block("VHT_M_DF_Rec_C_Transition - VHT_Type1-11000497", 12950, 1300, 13450, 1500),
    block("VHT_M_DF_Rec_C_Transition - VHT_Type1-11000498", 12950, -200, 13450, 0),
    block("$GV600x300", 13400, 300, 13700, 900, layer="VHT_AC.EAD.LINE"),           # miệng gió bên hông
]
GEOMETRY = JOINTS + FITS + [
    block("DSupport", 300, 100, layer="M-HVAC-DUCT-4"),
    block("DAMPER", 300, 100, 400, 200, layer="VHT_AC.EQUIP"),
    {"type": "Polyline", "layer": "M-HVAC-DUCT-1"},
]


def pipeline(geometry=GEOMETRY):
    joints, boxes = bog.joint_points(geometry, BBOX), bog.fitting_boxes(geometry, BBOX)
    pairs = bog.drop_inside_fittings(bog.pair_segments(bog.duct_segments(ENTITIES, BBOX)), boxes)
    runs = bog.build_runs(bog.assign_sizes(pairs, bog.parse_tags(TEXTS, BBOX)), joints)
    elbows = bog.find_elbows(boxes, runs)
    return runs, bog.number_pieces(bog.split_runs(runs, joints, boxes)), elbows


# Co vuông 90° Revit xuất thành hai nửa block 45° + đoạn chéo 100 mm (cổ co) giữa hai tuyến SEAD 700x300.
_N = 350 / math.sqrt(2)  # nửa bề rộng chiếu lên x/y theo pháp tuyến của phương chéo (0.707, 0.707)
_D = 100 / math.sqrt(2)
CO_PAIR_LINES = [
    line("H1", 0, 0, 2000, 0), line("H2", 0, 700, 2000, 700),                         # tuyến ngang
    line("D1", 2450 - _N, 650 + _N, 2450 - _N + _D, 650 + _N + _D),                  # hai cạnh đoạn chéo
    line("D2", 2450 + _N, 650 - _N, 2450 + _N + _D, 650 - _N + _D),
    line("V1", 2500, 1200, 2500, 3000), line("V2", 3200, 1200, 3200, 3000),          # tuyến đứng
    # nét trong block co bị ghép thành "tuyến" chéo rộng 100 — không được làm lệch góc co
    line("X1", 2100, 600, 2600, 100), line("X2", 2171, 671, 2671, 171),
]
CO_PAIR_BLOCKS = [
    block("VHT_M_DF_Rec_C_Elbow_Square - VHT_Type1-11064867", 2000, 0, 2500, 700),
    block("VHT_M_DF_Rec_C_Elbow_Square - VHT_Type1-11064867", 2500, 700, 3200, 1200),
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
        self.assertEqual([(t["sys"], t["W"], t["H"]) for t in tags], [("SEAD", 700, 300), ("RAD", 400, 250), ("FAD", 200, 150)])
        self.assertEqual(len(bog.parse_tags(TEXTS)), 4)  # không khung → lấy cả tag ngoài

    def test_duct_segments_bo_nhieu(self) -> None:
        handles = {s["handle"] for s in bog.duct_segments(ENTITIES, BBOX)}
        self.assertFalse(handles & {"N1", "N2", "N4", "X1"})
        self.assertIn("A6", handles)

    def test_joint_points_kem_loai_nep(self) -> None:
        self.assertEqual(bog.joint_points(GEOMETRY, BBOX),
                         [(1110, 350, "TDC"), (2220, 350, "TDC"), (10200, 1180, "C"), (1070, 15100, "TDC")])

    def test_fitting_boxes(self) -> None:
        self.assertEqual([(b["kind"], b["duct"]) for b in bog.fitting_boxes(GEOMETRY, BBOX)],
                         [("ELBOW", True), ("ENDCAP", True), ("ELBOW", True), ("ELBOW", True), ("TRANS", True),
                          ("TRANS", True), ("TERM", False), ("DAMPER", False)])

    def test_loai_phu_kien_theo_ten_family(self) -> None:
        self.assertEqual(bog.family_part("VHT_M_DF_Rec_C_Elbow_Square - VHT_Type1-11064867-HỆ THỐNG"),
                         "VHT_M_DF_Rec_C_Elbow_Square - VHT_Type1")
        self.assertEqual(bog.family_part("A - B-V273-MẶT BẰNG"), "A - B")
        got = [bog.fit_kind(n) for n in ("$GV1200x400", "VHT_M_DF_Rec_TDC_Union - X-1234567-V", "DSupport",
                                         "VHT_M_DA_MotorizedFireDamper - X", "02_DSC_REC_Flexible - 250mm",
                                         "VHT_M_DF_Rec_C_Tap - X", "Khung tên")]
        self.assertEqual(got, ["TERM", None, None, "DAMPER", "FLEX", "TAP", None])

    def test_quy_tac_chia_va_lam_tron(self) -> None:
        self.assertEqual(bog.split_interval(0, 1150, 1110), [])             # vượt ≤ 80 mm: không chia
        self.assertEqual(bog.split_interval(0, 1500, 1110), [1110])          # dư 390 ≥ 300: giữ
        self.assertEqual(bog.split_interval(0, 2400, 1110), [1110, 1755])    # dư 180 < 300: cộng đốt trước, chia đôi
        self.assertEqual(bog.split_interval(0, 1300, 1110, tol=50), [650])   # chỉ một cắt: chia đôi cả khoảng
        self.assertEqual([bog.round_len(v) for v in (767, 764, 1107, 1404.9)], [770, 760, 1110, 1400])
        self.assertEqual((bog.joint_by_size(700, 300), bog.joint_by_size(500, 2000)), ("TDC", "V"))

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
    def test_ghep_cap_va_bo_net_trong_phu_kien(self) -> None:
        pairs = bog.pair_segments(bog.duct_segments(ENTITIES, BBOX))
        self.assertIn(("E1", "E2"), [(p["a"], p["b"]) for p in pairs])
        kept = bog.drop_inside_fittings(pairs, bog.fitting_boxes(GEOMETRY, BBOX))
        got = sorted((p["a"], p["b"], p["w"], round(p["len"])) for p in kept)
        self.assertEqual(got, [("A1", "A2", 700, 1100), ("A3", "A4", 700, 1090), ("A5", "A6", 700, 1390),
                               ("B1", "B2", 400, 3000), ("C1", "C2", 1650, 2000),
                               ("F1", "F2", 200, 1000), ("F3", "F4", 200, 850), ("R1", "R2", 400, 1300)])

    def test_chon_canh_gan_nhat(self) -> None:
        segs = bog.duct_segments([line("a", 0, 0, 1000, 0), line("b", 0, 900, 1000, 900), line("c", 0, 500, 1000, 500)])
        self.assertEqual([(p["a"], p["b"]) for p in bog.pair_segments(segs)], [("a", "c"), ("b", "c")])

    def test_gan_kich_thuoc(self) -> None:
        pairs = bog.pair_segments(bog.duct_segments(ENTITIES, BBOX))
        by = {p["a"]: p for p in bog.assign_sizes(pairs, bog.parse_tags(TEXTS, BBOX))}
        self.assertEqual((by["A1"]["sys"], by["A1"]["W"], by["A1"]["H"]), ("SEAD", 700, 300))
        self.assertEqual((by["B1"]["sys"], by["B1"]["W"]), ("RAD", 400))
        self.assertIsNone(by["C1"]["sys"])
        far = bog.assign_sizes(pairs, bog.parse_tags(TEXTS, BBOX), radius=10)
        self.assertTrue(all(p["sys"] is None for p in far))

    def test_tuyen_dot_theo_nep_va_box(self) -> None:
        runs, pieces, _ = pipeline()
        self.assertEqual(len(runs), 5)  # SEAD, RAD, RAD có miệng gió, FAD (hai mảnh nối qua mối nối), cặp 1650
        got = [(p["code"], p["runCode"], p["joint"], p["len"], p["kind"]) for p in pieces]
        self.assertEqual(got, [("SEAD-001", "T01", "TDC", 1110, "Đốt chuẩn"), ("SEAD-002", "T01", "TDC", 1110, "Đốt chuẩn"),
                               ("SEAD-003", "T01", "TDC", 1400, "Box bít đầu"),
                               ("RAD-001", "T02", "C", 1180, "Đốt chuẩn"), ("RAD-002", "T02", "C", 1180, "Đốt chuẩn"),
                               ("RAD-003", "T02", "C", 640, "Đoạn bù"), ("RAD-004", "T03", "C", 1300, "Đoạn bù"),
                               ("FAD-001", "T04", "TDC", 1070, "Đoạn bù"), ("FAD-002", "T04", "TDC", 930, "Đoạn bù")])
        self.assertEqual(pieces[6]["note"], "có chân rẽ/miệng gió bên hông")
        self.assertEqual((round(pieces[0]["x"]), round(pieces[0]["y"])), (555, 350))
        self.assertEqual([r["w"] for r in runs if not r["size"]], [1650])

    def test_khong_co_block_thi_cat_tai_khe(self) -> None:
        _, pieces, elbows = pipeline(geometry=[])
        self.assertEqual([(p["code"], p["joint"], p["len"], p["kind"]) for p in pieces],
                         [("SEAD-001", "TDC", 1110, "Đốt chuẩn"), ("SEAD-002", "TDC", 1110, "Đốt chuẩn"),
                          ("SEAD-003", "TDC", 1400, "Box bít đầu"),
                          ("RAD-001", "TDC", 1110, "Đốt chuẩn"), ("RAD-002", "TDC", 1110, "Đốt chuẩn"),
                          ("RAD-003", "TDC", 780, "Đoạn bù"), ("RAD-004", "TDC", 1300, "Box bít đầu"),
                          ("FAD-001", "TDC", 1000, "Đoạn bù"), ("FAD-002", "TDC", 850, "Đoạn bù")])  # khe FAD 150 → hai tuyến
        self.assertNotEqual(pieces[7]["runCode"], pieces[8]["runCode"])
        self.assertEqual(elbows, [])

    def test_danh_so_he_ngoai_danh_muc(self) -> None:
        runs, _, _ = pipeline()
        next(r for r in runs if r["size"] and r["size"][0] == "SEAD")["size"] = ("XAD", 700, 300)
        joints, boxes = bog.joint_points(GEOMETRY, BBOX), bog.fitting_boxes(GEOMETRY, BBOX)
        pieces = bog.number_pieces(bog.split_runs(runs, joints, boxes))
        self.assertEqual(pieces[-1]["code"], "XAD-003")

    def test_nhan_dien_co(self) -> None:
        _, _, elbows = pipeline()
        self.assertEqual([(e["code"], e["sys"], e["W"], e["H"], e["angle"], e["joint"]) for e in elbows],
                         [("CO-001", "SEAD", 700, 300, 90, "TDC"), ("CO-002", "RAD", 400, 250, 45, "C"),
                          ("CO-003", None, None, None, 90, "?")])
        self.assertEqual([(e["R"], e["halves"]) for e in elbows], [(None, 1), (None, 1), (None, 1)])

    def test_gop_hai_nua_co_45_thanh_co_90(self) -> None:
        boxes = bog.fitting_boxes(CO_PAIR_BLOCKS)
        pairs = bog.drop_inside_fittings(bog.pair_segments(bog.duct_segments(CO_PAIR_LINES)), boxes)
        tags = bog.parse_tags([text("SEAD - 700x300", 1000, 1500), text("FAD - 100x100", 2300, 2000)])
        runs = bog.build_runs(bog.assign_sizes(pairs, tags), [])
        # tuyến ngang, đoạn chéo, tuyến đứng + "tuyến" FAD nhiễu trong block co
        self.assertEqual(sorted(r["size"][0] for r in runs if r["size"]), ["FAD", "SEAD", "SEAD", "SEAD"])
        elbows = bog.find_elbows(boxes, runs)
        self.assertEqual([(e["sys"], e["W"], e["H"], e["angle"], e["R"], e["halves"], e["joint"]) for e in elbows],
                         [("SEAD", 700, 300, 90, 100, 2, "TDC")])
        neck = [r for r in runs if r.get("elbow_part")]
        self.assertEqual([round(r["t1"] - r["t0"]) for r in neck], [100])
        pieces = [p for p in bog.split_runs(runs, [], boxes) if p["sys"] == "SEAD"]
        self.assertEqual(sorted(p["len"] for p in pieces), [690, 890, 1110, 1110])  # cổ co 100 không thành ống

    def test_goc_co_theo_tuyen_khong_theo_ten_family(self) -> None:
        # block tên "Elbow_45" nhưng nối hai tuyến vuông góc → co 90°
        boxes = bog.fitting_boxes([block("VHT_M_DF_Rec_C_Elbow_45 - VHT_Type _1W_-11000371", 2000, 0, 2700, 700)])
        # tuyến ngang là SAD, tuyến đứng là RAD cùng 400x250 — khác hệ vẫn phải ra góc giữa hai tuyến
        segs = [line("a", 0, 0, 2000, 0), line("b", 0, 400, 2000, 400),
                line("c", 2100, 700, 2100, 3500), line("d", 2500, 700, 2500, 3500)]
        tags = bog.parse_tags([text("SAD - 400x250", 1000, -300), text("RAD - 400x250", 2900, 3000)])
        runs = bog.build_runs(bog.assign_sizes(bog.pair_segments(bog.duct_segments(segs)), tags), [])
        self.assertEqual(sorted(r["size"][0] for r in runs), ["RAD", "SAD"])
        self.assertEqual([(e["sys"], e["angle"]) for e in bog.find_elbows(boxes, runs)], [("RAD", 90)])

    def test_dem_phu_kien(self) -> None:
        c = bog.count_fittings(GEOMETRY, BBOX)
        self.assertEqual(c, collections.Counter({"Rec_TDC_Union": 3, "Rec_C_Elbow_Square": 2, "Rec_C_Transition": 2,
                                                 "Rec_C_Union": 1, "Rec_Cap_End": 1, "Rec_C_Elbow_45": 1, "DSupport": 1}))


LABELS = [(-500, 1000, "HÚT KHÓI 700X1300 T/A"), (11000, 3500, "FCU 01")]


def sections_of(geometry=GEOMETRY, labels=LABELS):
    runs, pieces, elbows = pipeline(geometry)
    return bog.build_sections(runs, pieces, elbows, bog.fitting_boxes(geometry, BBOX), labels)


class SectionTests(unittest.TestCase):
    def test_nhan_truc(self) -> None:
        got = bog.shaft_labels([text("HÚT KHÓI 700X1300 T/A", 0, 0), text("SEAD - 700x300", 0, 0),
                                text("FAF-2F-01 100L/S", 99999, 0)], BBOX)
        self.assertEqual(got, [(0, 0, "HÚT KHÓI 700X1300 T/A")])

    def test_chia_cum_va_danh_so_theo_truc(self) -> None:
        got = [(title, [[(it["ma"], it.get("len") or it.get("angle"), it["tt"]) for it in c] for c in clusters])
               for title, clusters in sections_of()]
        self.assertEqual(got, [
            # SEAD bắt đầu từ đầu gần van (DAMPER), co ở đầu đó đứng trước các đốt
            ("TRỤC HÚT KHÓI 700X1300 T/A", [[("C", 90, 1), ("T", 1110, 2), ("T", 1110, 3), ("B", 1400, 4)]]),
            # đi từ đầu gần nhãn ra ngoài: co ở đầu trên trước; cụm thứ 2 (giữa hai côn) đánh lại từ 1, côn = G
            ("GIÓ HỒI (RAD) — gần FCU 01", [[("C", 45, 1), ("T", 640, 2), ("T", 1180, 3), ("T", 1180, 4)],
                                           [("G", 200, 1), ("T", 1300, 2), ("G", 200, 3)]]),
            ("GIÓ TƯƠI (FAD)", [[("T", 1070, 1), ("T", 930, 2)]]),
        ])

    def test_chuoi_ong_giam_co_ong_tu_van_ra(self) -> None:
        segs = [line("a1", 0, 0, 2000, 0), line("a2", 0, 700, 2000, 700),
                line("b1", 2400, 700, 2400, 2500), line("b2", 3100, 700, 3100, 2500)]
        geo = [block("VHT_M_DF_Rec_C_Transition - VHT_Type1-1", 2000, 0, 2400, 700),
               block("VHT_M_DF_Rec_C_Elbow_Square - VHT_Type1-1", 2400, 0, 3100, 700),
               block("VHT_M_DA_MotorizedFireDamper - X", -300, 0, -100, 700, layer="VHT_AC.EQUIP")]
        boxes = bog.fitting_boxes(geo)
        runs = bog.build_runs(bog.assign_sizes(bog.pair_segments(bog.duct_segments(segs)),
                                               bog.parse_tags([text("SEAD - 700x300", 1000, 1500)])), [])
        elbows = bog.find_elbows(boxes, runs)
        secs = bog.build_sections(runs, bog.split_runs(runs, [], boxes), elbows, boxes, [])
        got = [[(it["ma"], it.get("len") or it.get("angle"), it["tt"]) for it in c] for _, cl in secs for c in cl]
        # giảm sát co là một chuỗi: một cụm, số chạy liên tục từ đầu gần van ra ngoài
        self.assertEqual(got, [[("T", 1110, 1), ("T", 890, 2), ("G", 400, 3), ("C", 90, 4), ("T", 1110, 5), ("T", 690, 6)]])
        rows, _, _ = bog.sample_rows(secs)
        self.assertEqual([r[2] for r in rows if r and r[1] == "G"], ["Giảm 700x300 L400"])

    def test_tuyen_noi_dau_cham_dau_cung_mot_cum_va_co_le(self) -> None:
        runs, pieces, elbows = pipeline(geometry=[])  # FAD hai mảnh cách 150 mm → hai tuyến chạm đầu
        stray = {"sys": "SEAD", "W": 700, "H": 300, "R": None, "angle": 90, "joint": "TDC", "x": 0, "y": 0,
                 "links": [], "boxes": []}
        sections = bog.build_sections(runs, pieces, elbows + [stray], [], [])
        titles = [t for t, _ in sections]
        self.assertEqual(titles, ["HÚT KHÓI (SEAD)", "GIÓ HỒI (RAD)", "GIÓ TƯƠI (FAD)", "CO CHƯA GẮN ĐƯỢC VÀO TUYẾN"])
        fad = dict(sections)["GIÓ TƯƠI (FAD)"]
        self.assertEqual([[it["len"] for it in c] for c in fad], [[1000, 850]])

    def test_sheet_theo_mau(self) -> None:
        rows, widths, styles = bog.sample_rows(sections_of())
        self.assertEqual(rows[0][:4], ["TT", "MÃ", "QUY CÁCH ỐNG", "W"])
        self.assertEqual(rows[1][2], "TRỤC HÚT KHÓI 700X1300 T/A")
        self.assertEqual((rows[2][0], rows[2][2], rows[2][12], rows[2][14]), ("1", "Co vuông 90 độ 700x300", "cái", None))
        self.assertEqual(rows[2][17], "ỐNG EI60; DT co theo công thức của đơn vị bóc")
        self.assertEqual(rows[2][15], 0.75)  # ống hút khói bọc EI: luôn 0.75
        self.assertEqual(rows[3][:5] + [rows[3][9], rows[3][13], rows[3][14]],
                         ["2~3", "T", "Ống 700x300xL1110", 700, 300, 1110, 2, "ROUND((2*(D4+E4)*J4)*N4/10^6,3)"])
        self.assertEqual((rows[4][0], rows[4][2], rows[4][14], rows[4][16]),
                         ("4", "Ống bịt đầu 700x300xL1400", "ROUND((2*(D5+E5)*J5+D5*E5)*N5/10^6,3)", "TDC/ bít đầu"))
        rad = [r for r in rows if r and r[1] == "C" and r[3] == 400][0]
        self.assertEqual(rad[2], "Co 45 độ 400x250")
        g = [r for r in rows if r and r[1] == "G"]
        self.assertEqual([(r[0], r[2], r[9], r[12], r[14]) for r in g],
                         [("1", "Giảm 400x250 L200", 200, "cái", None), ("3", "Giảm 400x250 L200", 200, "cái", None)])
        self.assertEqual(g[0][17], "DT giảm/gót theo công thức của đơn vị bóc")
        self.assertEqual(rows[-1][14], f"SUM(O2:O{len(rows) - 1})")
        self.assertEqual(len(widths), 18)
        self.assertEqual(styles[1], bog.HEAD)

    def test_co_co_R_ghi_vao_quy_cach(self) -> None:
        sec = [("X", [[{"ma": "C", "sys": "SEAD", "W": 700, "H": 300, "R": 150, "angle": 90, "joint": "TDC", "tt": 1}]])]
        rows, _, _ = bog.sample_rows(sec)
        self.assertEqual((rows[2][2], rows[2][10]), ("Co vuông 90 độ 700x300xR150", 150))

    def test_file_lisp(self) -> None:
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "kl.lsp"
            self.assertEqual(bog.write_lsp(p, sections_of()), (13, 9))
            raw = p.read_bytes()
        text_ = raw.decode("ascii")
        self.assertIn(b"\r\n", raw)
        self.assertNotIn("{", text_)
        self.assertEqual(text_.count("(dhcb:t "), 13)
        self.assertEqual(text_.count("(dhcb:d "), 9)
        # đốt SEAD đầu tiên (TT 2, sau co): tâm (555, 350), số bên trái tuyến, DIM bên phải ghi đè chiều dài Excel
        self.assertIn('(dhcb:t 555.000 950.000 0.000000 "2")', text_)
        self.assertIn('(dhcb:d 0.000 0.000 1110.000 0.000 555.000 -450.000 "1110")', text_)
        self.assertIn('(dhcb:d 2220.000 0.000 3620.000 0.000 2920.000 -450.000 "1400")', text_)
        self.assertIn('(dhcb:t 13200.000 1400.000 0.0 "1")', text_)  # côn: số đặt giữa block
        for s in ("DHCB-KL-STT", "DHCB-KL-DIM", "(cons 40 150.0)", "c:DHCB_KL_GHI", "c:DHCB_KL_XOA",
                  "Ghi 13 so TT va 9 DIM", '(command "_.UNDO" "_BE")', '(command "_.UNDO" "_E")'):
            self.assertIn(s, text_)


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
        runs, pieces, elbows = pipeline()
        sheets = bog.build_sheets(pieces, elbows, [r for r in runs if not r["size"]],
                                  bog.count_fittings(GEOMETRY, BBOX), "test.dwg", 0.05)
        with tempfile.TemporaryDirectory() as d:
            out = Path(d) / "kl.xlsx"
            bog.write_xlsx(out, sheets)
            with zipfile.ZipFile(out) as z:
                self.assertIsNone(z.testzip())
                wb = z.read("xl/workbook.xml").decode()
            self.assertEqual(re.findall(r'<sheet name="([^"]+)"', wb),
                             ["Tổng hợp", "Chi tiết", "Đoạn bù &amp; box", "Co", "Phụ kiện", "Chưa xác định", "Ghi chú"])
            tot, det, fill, co = (sheet_cells(out, i) for i in (1, 2, 3, 4))
            self.assertEqual(tot["S3"], 0.05)
            self.assertEqual((tot["B6"], tot["C7"], tot["D7"], tot["G7"], tot["H7"]),
                             ("Hút khói (Smoke Exhaust Air Duct)", "SEAD", "700x300", "TDC", 1110.0))
            self.assertEqual(tot["I7"], "=COUNTIFS('Chi tiết'!D:D,C7,'Chi tiết'!E:E,D7,'Chi tiết'!J:J,\"Đốt chuẩn\")")
            self.assertEqual(tot["P7"], "=COUNTIFS(Co!D:D,C7,Co!E:E,D7)")
            self.assertEqual(tot["S7"], "=ROUND(R7*(1+$S$3),2)")
            self.assertEqual((tot["G9"], tot["H9"]), ("C", 1180.0))
            self.assertEqual((det["B4"], det["C4"], det["H4"], det["J4"], det["K4"], det["L4"]),
                             ("SEAD-001", "T01", "TDC", "Đốt chuẩn", 1110.0, "=K4/1000"))
            self.assertEqual((det["B6"], det["J6"], det["N6"]), ("SEAD-003", "Box bít đầu", "=ROUND(L6*M6+F6*G6/1000000,3)"))
            self.assertEqual(det["S10"], "có chân rẽ/miệng gió bên hông")
            self.assertEqual(det["L13"], "=SUM(L4:L12)")
            self.assertEqual((det["T3"], det["U3"], det["T4"], det["U4"]), ("Bọc / ghi thêm (tag)", "Độ dày tole (mm)", "EI60", 0.75))
            row = next(r for r in range(14, 30) if tot.get(f"B{r}") == "Tole dày 0.75 mm")
            self.assertEqual(tot[f"M{row}"], "=SUMIFS('Chi tiết'!N:N,'Chi tiết'!U:U,0.75)")
            self.assertEqual(tot[f"Q{row}"], "=SUMIFS(Co!L:L,Co!R:R,0.75)")
            self.assertEqual([fill[f"E{r}"] for r in range(4, 9)], [1400.0, 640.0, 1300.0, 930.0, 1070.0])
            self.assertEqual(fill["B4"], "Box bít đầu")
            self.assertEqual((co["B4"], co["C4"], co["J4"]), ("CO-001", "Co 90°", "=ROUND(F4*TAN(RADIANS(H4/2))/1000,3)"))
            self.assertEqual((co["D6"], co["E6"]), ("?", "?"))
            self.assertEqual(sheet_cells(out, 5)["D4"], 3.0)
            self.assertEqual(sheet_cells(out, 6)["B4"], 1650.0)


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
            return {"entities": JOINTS[:2]}
        return {"filename": "C:\\du an\\Tang 2.dwg"}


class LiveAndMainTests(unittest.TestCase):
    def test_fetch_live_phan_trang_va_thu_lai(self) -> None:
        agent = FakeAgent()
        entities, texts, geometry, name = bog.fetch_live(agent, pause=0)
        self.assertEqual(len(entities), len(ENTITIES) + 1)
        self.assertEqual(agent.calls.count("entity_geometry"), 2)
        self.assertEqual((len(texts), len(geometry), name), (len(TEXTS), 2, "Tang 2.dwg"))

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
            out, lsp = Path(d) / "kl.xlsx", Path(d) / "kl.lsp"
            buf = io.StringIO()
            with redirect_stdout(buf):
                code = bog.main(["--entities", str(e), "--texts", str(t), "--geometry", str(g),
                                 "--bbox=-1000,-1000,25000,25000", "-o", str(out), "--lsp", str(lsp)])
            self.assertEqual(code, 0)
            self.assertTrue(out.is_file())
            self.assertIn("4 đốt chuẩn, 4 đoạn bù, 1 box bít đầu trên 4 tuyến (8.52 m ống), 3 co, 16.06 m² tôn",
                          buf.getvalue())
            self.assertIn("1 tuyến chưa xác định (2.00 m)", buf.getvalue())
            self.assertIn("13 số TT, 9 DIM", buf.getvalue())
            self.assertIn("e.json", sheet_cells(out, 1)["A2"])
            with zipfile.ZipFile(out) as z:
                names = re.findall(r'<sheet name="([^"]+)"', z.read("xl/workbook.xml").decode())
            self.assertEqual(names[:3], ["Tổng hợp", "Theo mẫu", "Chi tiết"])
            self.assertIn("DHCB_KL_GHI", lsp.read_text(encoding="ascii"))

            with redirect_stderr(io.StringIO()) as err:
                self.assertEqual(bog.main(["--entities", str(e), "-o", str(out)]), 2)
                self.assertEqual(bog.main(["--entities", str(Path(d) / "khong-co.json"), "--texts", str(t), "-o", str(out)]), 2)
                self.assertEqual(bog.main(["--entities", str(e), "--texts", str(t), "--layer-prefix", "KHONG-CO", "-o", str(out)]), 1)
            self.assertIn("không có --bbox", err.getvalue())
            self.assertIn("không có block mối nối", err.getvalue())

    def test_main_doc_truc_tiep_qua_bridge(self) -> None:
        with tempfile.TemporaryDirectory() as d, mock.patch.object(bog.time, "sleep"):
            out = Path(d) / "kl.xlsx"
            with redirect_stdout(io.StringIO()):
                self.assertEqual(bog.main(["--bbox=-1000,-1000,25000,25000", "-o", str(out)], agent=FakeAgent()), 0)
            self.assertEqual(sheet_cells(out, 1)["A2"], "Nguồn: Tang 2.dwg")
            with redirect_stderr(io.StringIO()):
                self.assertEqual(bog.main(["-o", str(out)], agent=FakeAgent(bad="text")), 2)


if __name__ == "__main__":
    unittest.main()


class TagSpecAndToleTests(unittest.TestCase):
    def test_duoi_tag_va_nhieu_tag_trong_mot_chu(self) -> None:
        tags = bog.parse_tags([text(r"{\fArial;SEAD- 700x300+ EI120\PBOD=FFL+2730}", 0, 0),
                               text(r"FAD- 450x250+\PTIÊU ÂM TRONG 50mm\PBOD=FFL+2880", 10, 0),
                               text("PAD- 800x250 BOD=FFL+4270 SEAD- 800x250", 20, 0)])
        self.assertEqual([(t["sys"], t["W"], t["H"], t["spec"]) for t in tags],
                         [("SEAD", 700, 300, "EI120"), ("FAD", 450, 250, "TIÊU ÂM TRONG 50MM"),
                          ("PAD", 800, 250, ""), ("SEAD", 800, 250, "")])

    def test_cap_chong_chay_va_do_day_tole(self) -> None:
        self.assertEqual((bog.ei_class("SEAD"), bog.ei_class("SEAD", "EI120"), bog.ei_class("FAD"), bog.ei_class("PAD", "EI 30")),
                         ("EI60", "EI120", "", "EI30"))
        self.assertEqual([bog.tole_thickness(d) for d in ((1300, 700), (1250, 400), (600, 300), (500, 400), (150, 150))],
                         [0.95, 0.75, 0.75, 0.58, 0.58])
        self.assertEqual(bog.tole_thickness((300, 200), "EI60"), 0.75)
        self.assertEqual(bog.tole_thickness((1350, 550), "EI120"), 0.75)

    def test_canh_ve_theo_mep_bich_van_khop_tag_nhung_uu_tien_khop_dung(self) -> None:
        pair = {"w": 740, "mid": (0, 0)}
        tags = [{"sys": "SEAD", "W": 700, "H": 300, "x": 100, "y": 0, "spec": "EI120"},
                {"sys": "PAD", "W": 750, "H": 400, "x": 3000, "y": 0, "spec": ""}]
        got = bog.assign_sizes([pair], tags)[0]
        self.assertEqual((got["sys"], got["W"], got["spec"]), ("PAD", 750, ""))  # khớp đúng ±30 thắng khớp nhờ bích
        got = bog.assign_sizes([pair], tags[:1])[0]
        self.assertEqual((got["sys"], got["W"], got["spec"]), ("SEAD", 700, "EI120"))
        self.assertIsNone(bog.assign_sizes([{"w": 800, "mid": (0, 0)}], tags[:1])[0]["sys"])  # rộng hơn 60 mm: không nhận

    def test_bich_ve_bang_net_thanh_moi_noi(self) -> None:
        # ống 700x300 vẽ theo mép bích (cạnh cách 740), bích TDC = 3 nét dọc cách 20 mm tại x = 1110 và 2220
        segs = [line("s1", 0, 0, 3330, 0), line("s2", 0, 740, 3330, 740)]
        for k, x in enumerate((1110, 2220)):
            segs += [line(f"f{k}{i}", x + d, 0, x + d, 740) for i, d in enumerate((-20, 0, 20))]
        joints, rest = bog.flange_joints(bog.duct_segments(segs))
        self.assertEqual([(round(x), round(y), k) for x, y, k in joints], [(1110, 370, "TDC"), (2220, 370, "TDC")])
        self.assertEqual(sorted(s["handle"] for s in rest), ["s1", "s2"])
        runs = bog.build_runs(bog.assign_sizes(bog.pair_segments(rest), bog.parse_tags([text("SEAD - 700x300+ EI120", 1500, 1200)])), joints)
        pieces = bog.split_runs(runs, joints)
        self.assertEqual([(p["len"], p["W"], p["ei"], p["tole"]) for p in pieces],
                         [(1110, 700, "EI120", 0.75)] * 3)

    def test_net_vuong_goc_cung_chieu_dai_khong_phai_bich(self) -> None:
        segs = bog.duct_segments([line("v", 0, 0, 0, 740), line("h", 10, 0, 750, 0)])
        joints, rest = bog.flange_joints(segs)
        self.assertEqual((joints, sorted(s["handle"] for s in rest)), ([], ["h", "v"]))

    def test_ong_mem_net_gap_nep_khong_tinh_dot(self) -> None:
        # flex D100: 4 cặp nét 80 mm nối tiếp lệch nhau (gấp nếp) + một ống thật 1110 riêng
        segs = [line("a1", 0, 0, 1110, 0), line("a2", 0, 300, 1110, 300)]
        for k in range(4):
            x = 3000 + k * 90
            segs += [line(f"m{k}a", x, k * 40, x + 80, k * 40), line(f"m{k}b", x, 100 + k * 40, x + 80, 100 + k * 40)]
        tags = bog.parse_tags([text("FAD - 300x200", 500, 600), text("FAD - 100x100", 3100, 400)])
        runs = bog.build_runs(bog.assign_sizes(bog.pair_segments(bog.duct_segments(segs)), tags), [])
        self.assertEqual(bog.mark_flex(runs), 4)
        self.assertEqual([p["len"] for p in bog.split_runs(runs, [])], [1110])

    def test_thu_tu_cum_trai_phai(self) -> None:
        runs, pieces, elbows = pipeline()
        secs = bog.build_sections(runs, pieces, elbows, bog.fitting_boxes(GEOMETRY, BBOX), LABELS, order="trai-phai")
        firsts = [min(it["x"] for it in c) for _, cl in secs for c in cl]
        self.assertEqual(firsts, sorted(firsts))
