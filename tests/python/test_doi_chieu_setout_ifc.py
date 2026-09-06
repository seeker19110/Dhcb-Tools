"""Test cho scripts/doi-chieu-setout-ifc.py — đối chiếu setout.csv với IFC của Autodesk (§52).

Fixture IFC viết tay: đơn vị FOOT như Revit xuất, site dời (1000, 2000) ft và xoay 90°, một trục dọc + một
trục ngang + một trục cong (3 điểm, bị bỏ), ba cột: hình chữ nhật qua IFCMAPPEDITEM, hình tròn bị cắt Boolean,
và một cột B-rep; cột thứ tư không có hình học.
"""

from __future__ import annotations

import importlib.util
import io
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "doi-chieu-setout-ifc.py"
_spec = importlib.util.spec_from_file_location("doi_chieu", SCRIPT)
doi_chieu = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(doi_chieu)

FT = 0.3048

# Site: gốc (1000, 2000, 10) ft, trục x quay 90° → (0,1). Điểm cục bộ (x, y) → thế giới (1000 - y, 2000 + x).
IFC = """ISO-10303-21;
HEADER;
ENDSEC;
DATA;
#1=IFCCARTESIANPOINT((0.,0.,0.));
#2=IFCDIRECTION((0.,0.,1.));
#3=IFCDIRECTION((1.,0.,0.));
#4=IFCAXIS2PLACEMENT3D(#1,#2,#3);
#10=IFCCARTESIANPOINT((1000.,2000.,10.));
#11=IFCDIRECTION((0.,1.,0.));
#12=IFCAXIS2PLACEMENT3D(#10,#2,#11);
#13=IFCLOCALPLACEMENT($,#12);
#14=IFCSITE('site',$,'Site',$,$,#13,$,$,.ELEMENT.,$,$,$,$,$);
#20=IFCLOCALPLACEMENT(#13,#4);
#21=IFCCARTESIANPOINT((0.,-5.));
#22=IFCCARTESIANPOINT((0.,5.));
#23=IFCPOLYLINE((#21,#22));
#24=IFCGRIDAXIS('1',#23,.T.);
#25=IFCCARTESIANPOINT((-5.,3.));
#26=IFCCARTESIANPOINT((5.,3.));
#27=IFCPOLYLINE((#25,#26));
#28=IFCGRIDAXIS('A',#27,.T.);
#29=IFCCARTESIANPOINT((-5.,-3.));
#30=IFCCARTESIANPOINT((5.,-3.));
#31=IFCPOLYLINE((#29,#30));
#32=IFCGRIDAXIS('B',#31,.T.);
#33=IFCCARTESIANPOINT((7.,7.));
#34=IFCCARTESIANPOINT((8.,8.));
#35=IFCCARTESIANPOINT((9.,7.));
#36=IFCPOLYLINE((#33,#34,#35));
#37=IFCGRIDAXIS('Cong',#36,.T.);
#38=IFCCARTESIANPOINT((20.,0.));
#39=IFCCARTESIANPOINT((20.,1.));
#40=IFCPOLYLINE((#38,#39));
#41=IFCGRIDAXIS('Xa',#40,.T.);
#42=IFCGRID('grid',$,'Grid',$,$,#20,$,(#24,#41),(#28,#32,#37),$);
#50=IFCCARTESIANPOINT((4.,6.,0.));
#51=IFCAXIS2PLACEMENT3D(#50,$,$);
#52=IFCLOCALPLACEMENT(#13,#51);
#53=IFCCARTESIANPOINT((0.5,0.));
#54=IFCDIRECTION((1.,0.));
#55=IFCAXIS2PLACEMENT2D(#53,#54);
#56=IFCRECTANGLEPROFILEDEF(.AREA.,$,#55,2.,1.);
#57=IFCEXTRUDEDAREASOLID(#56,#4,#2,10.);
#58=IFCSHAPEREPRESENTATION(#99,'Body','SweptSolid',(#57));
#59=IFCREPRESENTATIONMAP(#4,#58);
#60=IFCCARTESIANPOINT((1.,0.,0.));
#61=IFCCARTESIANTRANSFORMATIONOPERATOR3D($,$,#60,1.,$);
#62=IFCMAPPEDITEM(#59,#61);
#63=IFCSHAPEREPRESENTATION(#99,'Body','MappedRepresentation',(#62));
#64=IFCSHAPEREPRESENTATION(#99,'Axis','Curve2D',(#23));
#65=IFCPRODUCTDEFINITIONSHAPE($,$,(#64,#63));
#66=IFCCOLUMN('c1',$,'Cot chu nhat:1',$,$,#52,#65,$);
#70=IFCCARTESIANPOINT((-4.,0.,0.));
#71=IFCAXIS2PLACEMENT3D(#70,$,$);
#72=IFCLOCALPLACEMENT(#13,#71);
#73=IFCCARTESIANPOINT((0.,0.));
#74=IFCAXIS2PLACEMENT2D(#73);
#75=IFCCIRCLEPROFILEDEF(.AREA.,$,#74,1.);
#76=IFCEXTRUDEDAREASOLID(#75,#4,#2,10.);
#77=IFCBOOLEANCLIPPINGRESULT(.DIFFERENCE.,#76,#76);
#78=IFCSHAPEREPRESENTATION(#99,'Body','Clipping',(#77));
#79=IFCPRODUCTDEFINITIONSHAPE($,$,(#78));
#80=IFCCOLUMN('c2',$,'Cot tron:2',$,$,#72,#79,$);
#90=IFCCARTESIANPOINT((0.,0.,0.));
#91=IFCAXIS2PLACEMENT3D(#90,$,$);
#92=IFCLOCALPLACEMENT(#13,#91);
#93=IFCCARTESIANPOINT((2.,2.,0.));
#94=IFCCARTESIANPOINT((4.,2.,0.));
#95=IFCCARTESIANPOINT((4.,4.,0.));
#96=IFCPOLYLOOP((#93,#94,#95,#93));
#97=IFCFACEOUTERBOUND(#96,.T.);
#98=IFCFACE((#97));
#100=IFCCLOSEDSHELL((#98));
#101=IFCFACETEDBREP(#100);
#102=IFCSHAPEREPRESENTATION(#99,'Body','Brep',(#101));
#103=IFCPRODUCTDEFINITIONSHAPE($,$,(#102));
#104=IFCCOLUMN('c3','x','Cot brep:3',$,$,#92,#103,$);
#110=IFCCARTESIANPOINT((1.,1.));
#111=IFCCARTESIANPOINT((2.,1.));
#112=IFCCARTESIANPOINT((2.,3.));
#113=IFCPOLYLINE((#110,#111,#112,#110));
#114=IFCARBITRARYCLOSEDPROFILEDEF(.AREA.,$,#113);
#115=IFCEXTRUDEDAREASOLID(#114,#4,#2,10.);
#116=IFCSHAPEREPRESENTATION(#99,'Body','SweptSolid',(#115));
#117=IFCPRODUCTDEFINITIONSHAPE($,$,(#116));
#118=IFCCOLUMN('c4',$,'Cot L:4',$,$,#92,#117,$);
#120=IFCSHAPEREPRESENTATION(#99,'Body','Other',(#121));
#121=IFCTRIMMEDCURVE(#23,(#21),(#22),.T.,.PARAMETER.);
#122=IFCPRODUCTDEFINITIONSHAPE($,$,(#120));
#123=IFCCOLUMN('c5',$,'Cot khong hinh hoc:5',$,$,#92,#122,$);
#130=IFCEXTRUDEDAREASOLID(#999,#4,#2,10.);
#131=IFCARBITRARYPROFILEDEFWITHVOIDS(.AREA.,$,#998,(#113));
#132=IFCEXTRUDEDAREASOLID(#131,#4,#2,10.);
#133=IFCSHAPEREPRESENTATION(#99,'Body','SweptSolid',(#130,#132));
#134=IFCPRODUCTDEFINITIONSHAPE($,$,(#133));
#135=IFCCOLUMN('c6',$,'Cot profile thieu:6',$,$,#92,#134,$);
#140=IFCCOLUMN('c7',$,'Cot cat:7 30"D x 30"W',$,$,#92,#79,$);
ENDSEC;
END-ISO-10303-21;
"""


def world(x: float, y: float) -> tuple[float, float]:
    """Điểm cục bộ (ft) → hệ Survey (m) theo đúng placement của fixture."""
    return (1000 - y) * FT, (2000 + x) * FT


class ParseTests(unittest.TestCase):
    def test_parse_giu_chuoi_va_lap_to(self):
        kind, args = doi_chieu.parse("IFCX('a,(b)',$,(#1,#2),(1.,2.))")
        self.assertEqual("IFCX", kind)
        self.assertEqual(["'a,(b)'", "$", ["#1", "#2"], ["1.", "2."]], args)

    def test_ref(self):
        self.assertEqual(12, doi_chieu.ref("#12"))


class IfcTests(unittest.TestCase):
    def setUp(self):
        self.ifc = doi_chieu.Ifc(IFC.splitlines())

    def test_site_origin_theo_placement(self):
        e, n, z, angle = self.ifc.site_origin()
        self.assertAlmostEqual(1000 * FT, e)
        self.assertAlmostEqual(2000 * FT, n)
        self.assertAlmostEqual(10 * FT, z)
        self.assertAlmostEqual(90.0, angle)

    def test_giao_truc_bo_truc_cong_va_truc_khong_cat(self):
        pts = self.ifc.grid_intersections()
        expected = sorted([world(0, 3), world(0, -3)])
        self.assertEqual(expected, sorted((round(x, 9), round(y, 9)) for x, y in pts))

    def test_tam_cot_moi_loai_hinh_hoc(self):
        by_name = {name: (e, n) for name, e, n in self.ifc.column_centres()}
        # Chữ nhật 2×1 qua mapped item: profile lệch +0.5, operator dời +1 → tâm cục bộ (1.5, 0) + gốc cột (4, 6).
        self.assertAlmostEqual(world(5.5, 6)[0], by_name["Cot chu nhat:1"][0])
        self.assertAlmostEqual(world(5.5, 6)[1], by_name["Cot chu nhat:1"][1])
        # Tròn bán kính 1 bị Boolean: lấy toán hạng đầu; gốc cột (-4, 0).
        self.assertAlmostEqual(world(-4, 0)[0], by_name["Cot tron:2"][0])
        self.assertAlmostEqual(world(-4, 0)[1], by_name["Cot tron:2"][1])
        # B-rep tam giác (2,2)-(4,2)-(4,4) → hộp bao tâm (3, 3).
        self.assertAlmostEqual(world(3, 3)[0], by_name["Cot brep:3"][0])
        self.assertAlmostEqual(world(3, 3)[1], by_name["Cot brep:3"][1])
        # Profile tuỳ ý (1,1)-(2,1)-(2,3) → hộp bao tâm (1.5, 2).
        self.assertAlmostEqual(world(1.5, 2)[0], by_name["Cot L:4"][0])
        self.assertAlmostEqual(world(1.5, 2)[1], by_name["Cot L:4"][1])
        self.assertNotIn("Cot khong hinh hoc:5", by_name)
        # Profile trỏ tới thực thể không có trong file (hay loại chưa đọc) → bỏ qua, không sập.
        self.assertNotIn("Cot profile thieu:6", by_name)

    def test_clipped_theo_kich_thuoc_danh_nghia(self):
        # 24"D x 24"W = 609,6 mm: thân 267 × 548 là bị cắt; 610 × 610 là nguyên; tên không có cỡ → không kết luận.
        self.assertTrue(doi_chieu.clipped('Rectangular Column (Off Center):24"D x 24"W:1', 267, 548))
        self.assertFalse(doi_chieu.clipped('Rectangular Column:24"D x 24"W:2', 610, 609.6))
        self.assertFalse(doi_chieu.clipped('Round Column:4" Diameter:3', 10, 10))
        by_name = dict((n.split(" [")[0], n) for n, _, _ in self.ifc.column_centres())
        self.assertIn("bị cắt", by_name['Cot cat:7 30"D x 30"W'])

    def test_profile_la_loai_khong_biet_thi_rong(self):
        self.assertEqual([], self.ifc.profile_points(23))

    def test_segment_intersection_song_song_hay_ngoai_doan(self):
        a = [[0, 0, 0], [10, 0, 0]]
        self.assertIsNone(doi_chieu.segment_intersection(a, [[0, 1, 0], [10, 1, 0]]))
        self.assertIsNone(doi_chieu.segment_intersection(a, [[20, -1, 0], [20, 1, 0]]))
        x, y = doi_chieu.segment_intersection(a, [[4, -1, 0], [4, 1, 0]])
        self.assertAlmostEqual(4 * FT, x)
        self.assertAlmostEqual(0, y)


class MainTests(unittest.TestCase):
    def run_main(self, argv):
        buf = io.StringIO()
        with redirect_stdout(buf):
            code = doi_chieu.main(argv)
        return code, buf.getvalue()

    def write(self, folder: Path, rows: str) -> tuple[Path, Path]:
        csv_path = folder / "setout.csv"
        csv_path.write_text("Name,N,E,Z,Desc\n" + rows, encoding="utf-8")
        ifc_path = folder / "m.ifc"
        ifc_path.write_text(IFC, encoding="utf-8")
        return csv_path, ifc_path

    def test_thieu_tham_so_va_thieu_file(self):
        code, out = self.run_main([])
        self.assertEqual(2, code)
        self.assertIn("Cách dùng", out)
        code, out = self.run_main(["khong-co.csv", "khong-co.ifc"])
        self.assertEqual(2, code)
        self.assertIn("Không thấy file", out)

    def test_khop_thi_0_lech_thi_1(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            (e1, n1), (e2, n2) = world(0, 3), world(5.5, 6)
            csv_path, ifc_path = self.write(folder, f"A-1,{n1:.4f},{e1:.4f},3.048,Grids\nCOL001,{n2:.4f},{e2:.4f},3.048,Columns L1\n")
            code, out = self.run_main([str(csv_path), str(ifc_path)])
            self.assertEqual(0, code, out)
            self.assertIn("True North xoay 90.0000°", out)
            self.assertIn("Giao trục: 1 điểm", out)
            self.assertIn("Tim cột — cột IFC còn nguyên thân: 1 điểm", out)

            # Điểm trùng cột bị cắt (c7 = thân tròn bán kính 1 ft, tên 24"): chỉ tham khảo, mã thoát vẫn 0.
            e4, n4 = world(0, 0)
            csv_path, _ = self.write(folder, f"COL002,{n4:.4f},{e4:.4f},3.048,Columns L1\n")
            code, out = self.run_main([str(csv_path), str(ifc_path)])
            self.assertEqual(0, code, out)
            self.assertIn("bị cắt bởi join", out)

            # Cột lệch 305 mm (1 ft) → mã thoát 1 và nêu tên điểm; ngưỡng tự đặt.
            e3, n3 = world(6.5, 6)
            csv_path, _ = self.write(folder, f"COL001,{n3:.4f},{e3:.4f},3.048,Columns L1\n")
            code, out = self.run_main([str(csv_path), str(ifc_path), "--tol-mm", "10"])
            self.assertEqual(1, code)
            self.assertIn("COL001: lệch 305 mm (IFC gần nhất: Cot chu nhat:1)", out)
            self.assertIn("lệch quá 10 mm: 1", out)

    def test_csv_khong_co_diem_nao_de_so(self):
        with tempfile.TemporaryDirectory() as tmp:
            csv_path, ifc_path = self.write(Path(tmp), "X,1,1,1,Khac\n")
            code, out = self.run_main([str(csv_path), str(ifc_path)])
            self.assertEqual(0, code)
            self.assertNotIn("Giao trục", out)

    def test_summarise_rong(self):
        self.assertIn("không có điểm nào", doi_chieu.summarise("Tim cột", [], 5))


if __name__ == "__main__":
    unittest.main()
