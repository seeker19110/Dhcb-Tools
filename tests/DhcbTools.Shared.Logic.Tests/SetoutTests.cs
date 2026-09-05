using System;
using System.Collections.Generic;
using System.Linq;
using DhcbTools.Shared.Logic.Geometry;
using DhcbTools.Shared.Logic.Setout;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Tầng thuần của <c>SetoutExport</c> (đề xuất A1 — toạ độ định vị ra máy toàn đạc). Điều phải giữ, theo
/// thứ tự quan trọng: (1) <b>không bao giờ có hai điểm cùng tên</b> — trên máy, chọn nhầm điểm là đục lại
/// bê tông; (2) số ghi ra luôn dấu chấm thập phân, đúng đơn vị, không <c>-0.000</c>; (3) thứ tự cột đúng
/// chữ máy nhận (PNEZD / PENZD…), sai một chữ là báo lỗi chứ không đoán; (4) giao trục chỉ sinh khi hai
/// trục thật sự cắt nhau trong phạm vi vẽ.
/// </summary>
public class SetoutTests
{
    private static SetoutSource Src(string category, double e, double n, double z, string level = "", string kind = "tim", long id = 0, string mark = "")
        => new SetoutSource(kind, e, n, z) { Category = category, Level = level, ElementId = id, Mark = mark };

    // ── Cột ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Cot_MacDinhLaPNEZD()
    {
        Assert.True(SetoutColumns.TryParse("", out var cols, out var error), error);
        Assert.Equal(new[] { SetoutColumn.Name, SetoutColumn.North, SetoutColumn.East, SetoutColumn.Elevation, SetoutColumn.Description }, cols);
    }

    [Theory]
    [InlineData("PENZDI", new[] { SetoutColumn.Name, SetoutColumn.East, SetoutColumn.North, SetoutColumn.Elevation, SetoutColumn.Description, SetoutColumn.ElementId })]
    [InlineData("p,n,e", new[] { SetoutColumn.Name, SetoutColumn.North, SetoutColumn.East })]
    [InlineData("P N E Z C L", new[] { SetoutColumn.Name, SetoutColumn.North, SetoutColumn.East, SetoutColumn.Elevation, SetoutColumn.Code, SetoutColumn.Level })]
    public void Cot_NhanChuThuongVaDauNgan(string letters, SetoutColumn[] expected)
    {
        Assert.True(SetoutColumns.TryParse(letters, out var cols, out var error), error);
        Assert.Equal(expected, cols);
    }

    [Theory]
    [InlineData("PNEQ", "không hợp lệ")]
    [InlineData("PNNE", "lặp lại")]
    [InlineData("PZD", "thiếu N hoặc E")]
    [InlineData("NEZ", "thiếu P")]
    public void Cot_SaiThiBaoRoKhongDoan(string letters, string expectedError)
    {
        Assert.False(SetoutColumns.TryParse(letters, out var cols, out var error));
        Assert.Empty(cols);
        Assert.Contains(expectedError, error);
    }

    [Theory]
    [InlineData("m", true)]
    [InlineData("", true)]
    [InlineData("MM", false)]
    [InlineData("mét", true)]
    public void DonVi_NhanMVaMm(string unit, bool metres)
    {
        Assert.True(SetoutCsvFormat.TryParseUnit(unit, out var actual, out var error), error);
        Assert.Equal(metres, actual);
    }

    [Fact]
    public void DonVi_SaiThiBaoRo()
    {
        Assert.False(SetoutCsvFormat.TryParseUnit("ft", out _, out var error));
        Assert.Contains("m hoặc mm", error);
    }

    // ── Mã ngắn ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Structural Columns", "COL")]
    [InlineData("columns", "COL")]
    [InlineData("Grids", "GRD")]
    [InlineData("Curtain Wall Mullions", "CWM")]
    [InlineData("Mass", "M")]
    [InlineData("", "PT")]
    [InlineData(null, "PT")]
    public void MaNgan_TheoBangHoacChuCaiDau(string? category, string expected) =>
        Assert.Equal(expected, SetoutCodes.For(category));

    // ── Đặt tên ──────────────────────────────────────────────────────────────

    [Fact]
    public void DatTen_DemRiengTheoMa_VaSapXepTangTuNhien()
    {
        var sources = new List<SetoutSource>
        {
            Src("Structural Columns", 0, 0, 0, "Level 10", id: 3),
            Src("Structural Columns", 0, 0, 0, "Level 2", id: 2),
            Src("Mechanical Equipment", 0, 0, 0, "Level 2", id: 5),
            Src("Structural Columns", 0, 0, 0, "Level 2", id: 1),
        };

        var plan = SetoutPlanner.Plan(sources);

        Assert.Equal(new[] { "COL001", "COL002", "ME001", "COL003" }, plan.Points.Select(p => p.Name));
        Assert.Equal(new[] { "Level 2", "Level 2", "Level 2", "Level 10" }, plan.Points.Select(p => p.Level));
        Assert.Equal(new long[] { 1, 2, 5, 3 }, plan.Points.Select(p => p.ElementId));
        Assert.Equal(3, plan.CountByCode["COL"]);
        Assert.Equal(1, plan.CountByCode["ME"]);
        Assert.Empty(plan.Notes);
    }

    [Fact]
    public void DatTen_TheoToken_LamSachChoMayToanDac()
    {
        var sources = new List<SetoutSource> { Src("Structural Columns", 0, 0, 0, "Tầng 3", mark: "C 12,\"x\"") };
        var plan = SetoutPlanner.Plan(sources, new SetoutPlanOptions { NamePattern = "{Level}-{Mark}" });

        // Bỏ dấu, khoảng trắng → _, dấu phẩy/nháy biến mất: máy toàn đạc chỉ nhận ASCII, không hiểu RFC 4180.
        Assert.Equal("Tang_3-C_12x", plan.Points[0].Name);
    }

    [Fact]
    public void DatTen_DauCuoiCuaPhanTuDangDuong_TheoThuTuTrenPhanTu()
    {
        var sources = new List<SetoutSource>
        {
            Src("Structural Framing", 10, 0, 0, "L1", "cuối", 7),
            Src("Structural Framing", 0, 0, 0, "L1", "đầu", 7),
        };

        var plan = SetoutPlanner.Plan(sources, new SetoutPlanOptions { NamePattern = "{Code}{n:00}-{Kind}" });

        Assert.Equal(new[] { "BM01-dau", "BM02-cuoi" }, plan.Points.Select(p => p.Name));
    }

    [Fact]
    public void DatTen_KhongBaoGioTrung_CoGhiChu()
    {
        var sources = new List<SetoutSource>
        {
            Src("Structural Columns", 0, 0, 0, mark: "C1", id: 1),
            Src("Structural Columns", 0, 0, 0, mark: "C1", id: 2),
            Src("Structural Columns", 0, 0, 0, mark: "C1", id: 3),
        };

        var plan = SetoutPlanner.Plan(sources, new SetoutPlanOptions { NamePattern = "{Mark}" });

        Assert.Equal(new[] { "C1", "C1_2", "C1_3" }, plan.Points.Select(p => p.Name));
        Assert.Equal(plan.Points.Count, plan.Points.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(2, plan.Renamed);
        Assert.Contains(plan.Notes, n => n.Contains("trùng"));
    }

    [Fact]
    public void DatTen_CatTheoGioiHanMay_VaVanKhongTrung()
    {
        var sources = new List<SetoutSource>
        {
            Src("Structural Columns", 0, 0, 0, mark: "ABCDEFGHIJKLMNOPQRSTUVWXYZ", id: 1),
            Src("Structural Columns", 0, 0, 0, mark: "ABCDEFGHIJKLMNOPQRSTUVWXYZ", id: 2),
        };

        var plan = SetoutPlanner.Plan(sources, new SetoutPlanOptions { NamePattern = "{Mark}", MaxNameLength = 16 });

        // Bỏ ở GIỮA chứ không cắt đuôi: với tên ghép ({Level}-{Grid}, TrụcA-TrụcB) phần phân biệt
        // nằm ở đuôi, cắt đuôi là bỏ đúng thứ trắc đạc cần để nhận ra điểm.
        Assert.Equal("ABCDEFG..TUVWXYZ", plan.Points[0].Name);
        Assert.Equal("ABCDEFG..TUVWX_2", plan.Points[1].Name);   // cắt thân để cả hậu tố vẫn ≤ 16
        Assert.All(plan.Points, p => Assert.True(p.Name.Length <= 16));
        Assert.Equal(2, plan.Truncated);
        Assert.Contains(plan.Notes, n => n.Contains("16 ký tự"));
    }

    [Theory]
    [InlineData("Block_35_Left-B.1", 16, "Block_3..eft-B.1")]
    [InlineData("ABCDEFGHIJ", 10, "ABCDEFGHIJ")]   // vừa đủ thì không đụng vào
    [InlineData("ABCDEFGHIJ", 9, "ABCD..HIJ")]
    [InlineData("ABCDEFGHIJ", 5, "AB..J")]
    [InlineData("ABCDEFGHIJ", 4, "ABCD")]          // quá ngắn để chứa dấu .. thì đành cắt đuôi
    [InlineData("ABCDEFGHIJ", 0, "ABCDEFGHIJ")]    // 0 = không giới hạn
    public void RutTen_BoOGiuaGiuCaDauLanDuoi(string name, int max, string mong)
        => Assert.Equal(mong, SetoutPlanner.Shorten(name, max));

    [Fact]
    public void RutTen_GiaoTrucCungMotTrucA_VanPhanBietDuocODuoi()
    {
        // Đúng hình dáng dữ liệu thật của Snowdon Towers: trục A dài, trục B mới là phần phân biệt.
        var names = new[] { "Block_35_Left-B.1", "Block_35_Left-B.2", "Block_35_Left-X_1" }
            .Select(n => SetoutPlanner.Shorten(n, 16)).ToList();

        Assert.Equal(3, names.Distinct().Count());
        Assert.All(names, n => Assert.True(n.Length <= 16));
        Assert.Equal(new[] { "B.1", "B.2", "X_1" }, names.Select(n => n.Substring(n.Length - 3)));

        // Bản cắt đuôi cũ nuốt mất đúng phần đuôi ấy: ba tên còn lại hai, và hai cái sống sót chỉ
        // khác nhau ở ký tự cuối. Đây là điều không được lặp lại.
        var cuUnique = new[] { "Block_35_Left-B.1", "Block_35_Left-B.2", "Block_35_Left-X_1" }
            .Select(n => n.Substring(0, 16)).Distinct().Count();
        Assert.Equal(2, cuUnique);
    }

    [Fact]
    public void DatTen_MauChiToanTokenRong_VanCoTen()
    {
        var plan = SetoutPlanner.Plan(new List<SetoutSource> { Src("Structural Columns", 0, 0, 0) }, new SetoutPlanOptions { NamePattern = "{Mark}" });
        Assert.Equal("COL001", plan.Points[0].Name);
    }

    [Fact]
    public void GiaoTruc_TenLaCapTruc_KhongDemSo()
    {
        var grid = new SetoutSource("giao trục", 1000, 2000, 0) { Category = "Grids", Grid = "A-1" };
        var plan = SetoutPlanner.Plan(new List<SetoutSource> { grid, Src("Structural Columns", 0, 0, 0) });

        Assert.Contains(plan.Points, p => p.Name == "A-1" && p.Code == "GRD" && p.ElementId == 0);
        Assert.Contains(plan.Points, p => p.Name == "COL001");
    }

    [Fact]
    public void MoTa_MotDong_KhongDauPhay()
    {
        var source = new SetoutSource("tim", 0, 0, 0) { Category = "Mechanical Equipment", Level = "Level 1", TypeName = "AHU-01, 5000 CFM" };
        var plan = SetoutPlanner.Plan(new List<SetoutSource> { source }, new SetoutPlanOptions { DescriptionPattern = "{Type}\n{Level}" });

        Assert.Equal("AHU-01; 5000 CFM Level 1", plan.Points[0].Description);
    }

    [Theory]
    [InlineData("Level 2", "Level 10", -1)]
    [InlineData("Level 10", "Level 2", 1)]
    [InlineData("Tầng 03", "tầng 3", 0)]
    [InlineData("L1", "L1", 0)]
    [InlineData("", "L1", -1)]
    public void SoSanhTuNhien(string a, string b, int expectedSign) =>
        Assert.Equal(expectedSign, Math.Sign(NaturalComparer.Instance.Compare(a, b)));

    [Theory]
    [InlineData("Cột trục A", "Cot truc A")]
    [InlineData("Đường ống", "Duong ong")]
    [InlineData("ABC", "ABC")]
    public void BoDau(string input, string expected) => Assert.Equal(expected, SetoutPlanner.StripDiacritics(input));

    // ── CSV ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1234.5678, true, 3, "1.235")]
    [InlineData(1234.5, false, 0, "1235")]
    [InlineData(-0.0004, true, 3, "0.000")]
    [InlineData(-1500, true, 2, "-1.50")]
    [InlineData(0, false, 0, "0")]
    public void DinhDangSo_DauChamThapPhan_KhongAmKhong(double mm, bool metres, int decimals, string expected) =>
        Assert.Equal(expected, SetoutCsv.FormatCoordinate(mm, metres, decimals));

    [Fact]
    public void Csv_PNEZD_Met_BaSoLe_CRLF()
    {
        var points = new List<SetoutPoint>
        {
            new SetoutPoint("COL001", 1234.5, 5678.25, 3000) { Kind = "tim", Description = "Structural Columns Level 1", Code = "COL", Level = "Level 1", ElementId = 42 },
        };

        var csv = SetoutCsv.Write(points, new SetoutCsvFormat());

        Assert.Equal("Name,N,E,Z,Desc\r\nCOL001,5.678,1.235,3.000,Structural Columns Level 1\r\n", csv);
    }

    [Fact]
    public void Csv_PENZDI_Mm_KhongTieuDe()
    {
        SetoutColumns.TryParse("PENZDI", out var columns, out _);
        var format = new SetoutCsvFormat { Columns = columns, Metres = false, IncludeHeader = false };
        var points = new List<SetoutPoint>
        {
            new SetoutPoint("A-1", 1000.4, 2000.6, 0) { Code = "GRD", ElementId = 0 },
            new SetoutPoint("ME001", -10, 20, 3300.5) { Description = "AHU", Code = "ME", ElementId = 99 },
        };

        var csv = SetoutCsv.Write(points, format);

        Assert.Equal("A-1,1000,2001,0,,\r\nME001,-10,20,3301,AHU,99\r\n", csv);
    }

    [Fact]
    public void Csv_KhongCanNhayKep_VoiTenVaMoTaDaLamSach()
    {
        var plan = SetoutPlanner.Plan(new List<SetoutSource>
        {
            new SetoutSource("tim", 0, 0, 0) { Category = "Doors", TypeName = "Cửa 2 cánh, gỗ", Mark = "D 01" },
        }, new SetoutPlanOptions { NamePattern = "{Mark}", DescriptionPattern = "{Type}" });

        var csv = SetoutCsv.Write(plan.Points, new SetoutCsvFormat { IncludeHeader = false });

        Assert.DoesNotContain("\"", csv);
        Assert.Equal("D_01,0.000,0.000,0.000,Cửa 2 cánh; gỗ\r\n", csv);
    }

    // ── DXF ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Dxf_CoPointVaTextTrenLayerTheoMa_DungDonVi()
    {
        var points = new List<SetoutPoint>
        {
            new SetoutPoint("COL001", 1000, 2000, 3000) { Code = "COL" },
        };

        var dxf = SetoutDxf.Write(points, metres: true, decimals: 3);
        var lines = dxf.Split(new[] { "\r\n" }, StringSplitOptions.None);

        Assert.StartsWith("  0\r\nSECTION\r\n  2\r\nENTITIES\r\n", dxf);
        Assert.EndsWith("  0\r\nENDSEC\r\n  0\r\nEOF\r\n", dxf);
        Assert.Contains("POINT", lines);
        Assert.Contains("TEXT", lines);
        Assert.Contains("DHCB-COL", lines);
        Assert.Contains("DHCB-COL-TEN", lines);
        Assert.Contains("COL001", lines);

        // POINT: 10 = X = Đông, 20 = Y = Bắc, 30 = Z — theo mét.
        var i = Array.IndexOf(lines, "POINT");
        Assert.Equal(new[] { "  8", "DHCB-COL", " 10", "1.000", " 20", "2.000", " 30", "3.000" }, lines.Skip(i + 1).Take(8));
    }

    [Fact]
    public void Dxf_Mm_KhongSoLe()
    {
        var dxf = SetoutDxf.Write(new List<SetoutPoint> { new SetoutPoint("P1", 1000.4, 2000.6, 0) }, metres: false, decimals: 0);
        Assert.Contains("\r\n 10\r\n1000\r\n 20\r\n2001\r\n 30\r\n0\r\n", dxf);
    }

    // ── Giao trục ────────────────────────────────────────────────────────────

    private static NamedSegment2D Grid(string name, double x1, double y1, double x2, double y2) => new NamedSegment2D(name, new Segment2D(x1, y1, x2, y2));

    [Fact]
    public void GiaoTruc_LuoiVuong_TrucChuDungTruoc()
    {
        var grids = new List<NamedSegment2D>
        {
            Grid("1", 0, -1000, 0, 11000),
            Grid("2", 8000, -1000, 8000, 11000),
            Grid("A", -1000, 0, 9000, 0),
            Grid("B", -1000, 6000, 9000, 6000),
        };

        var found = GridIntersections.Find(grids);

        Assert.Equal(4, found.Count);
        Assert.Equal(new[] { "A-1", "B-1", "A-2", "B-2" }, found.Select(f => f.Name));
        var b2 = found.Single(f => f.Name == "B-2");
        Assert.Equal(8000, b2.X, 6);
        Assert.Equal(6000, b2.Y, 6);
    }

    [Fact]
    public void GiaoTruc_SongSongHoacNgoaiDoan_KhongSinhDiem()
    {
        var parallel = GridIntersections.Find(new List<NamedSegment2D> { Grid("A", 0, 0, 100, 0), Grid("B", 0, 50, 100, 50) });
        Assert.Empty(parallel);

        // Hai đoạn cắt nhau nếu kéo dài, nhưng trên bản vẽ không chạm nhau → không có điểm để đối chiếu.
        var outside = GridIntersections.Find(new List<NamedSegment2D> { Grid("A", 0, 0, 100, 0), Grid("1", 200, -50, 200, 50) });
        Assert.Empty(outside);
    }

    [Fact]
    public void GiaoTruc_DungSaiChoTrucVuaChamDau()
    {
        var grids = new List<NamedSegment2D> { Grid("A", 0, 0, 100, 0), Grid("1", 100.5, -50, 100.5, 50) };

        Assert.Empty(GridIntersections.Find(grids, toleranceMm: 0.1));
        Assert.Single(GridIntersections.Find(grids, toleranceMm: 1.0));
    }

    [Fact]
    public void GiaoTruc_TrucXien()
    {
        Assert.True(GridIntersections.Intersect(new Segment2D(0, 0, 100, 100), new Segment2D(0, 100, 100, 0), 0, out var x, out var y));
        Assert.Equal(50, x, 6);
        Assert.Equal(50, y, 6);
    }
}
