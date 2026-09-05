using System;
using System.IO;
using System.Linq;
using DhcbTools.Shared.Logic.Ifc;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Tầng thuần của <c>--verify-ifc</c> (mục 11.2 — kiểm IFC trước nộp).
/// <para>
/// Điều phải giữ: (1) đọc đúng cú pháp STEP kể cả những chỗ hay làm bộ đọc viết vội gãy — chuỗi có dấu
/// chấm phẩy bên trong, chú thích, dãy thoát Unicode của tên tiếng Việt, thực thể xuống nhiều dòng;
/// (2) tra được thuộc tính qua quan hệ ngược, kể cả thuộc tính thừa kế từ kiểu; (3) <b>phát hiện được
/// đúng những kiểu hỏng mà bộ xuất IFC gây ra trong im lặng</b> — thiếu phần tử, thiếu thuộc tính,
/// tham chiếu gãy, mã định danh trùng; (4) file đúng thì KHÔNG báo lỗi nào, vì bộ kiểm hay báo sai sẽ
/// bị tắt đi và thành vô dụng.
/// </para>
/// </summary>
public class IfcTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dhcb-Tools.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
    }

    /// <summary>File IFC nhỏ nhưng đủ hình dáng thật: có HEADER, dự án, tầng, hai bức tường, một Pset
    /// gán trực tiếp, một Pset thừa kế từ kiểu, và một phân loại.</summary>
    private const string FileMau = """
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION((''),'2;1');
        FILE_NAME('toa-a.ifc','2026-09-05T01:02:03',(''),(''),'DHCB','DHCB Tools','');
        FILE_SCHEMA(('IFC4'));
        ENDSEC;
        DATA;
        #1=IFCPROJECT('0Aa$b1cD2eF3gH4iJ5kL6m',$,'Toa nha A',$,$,$,$,$,$);
        #2=IFCBUILDINGSTOREY('1Aa$b1cD2eF3gH4iJ5kL6m',$,'Tang 1',$,$,$,$,$,.ELEMENT.,0.);
        #10=IFCWALLSTANDARDCASE('2Aa$b1cD2eF3gH4iJ5kL6m',$,'Tuong ngoai 200',$,$,$,$,$,$);
        #11=IFCWALLSTANDARDCASE('3Aa$b1cD2eF3gH4iJ5kL6m',$,'T\X2\01B0\X0\\X2\1EDD\X0\ng trong 100',$,$,$,$,$,$);
        #20=IFCPROPERTYSINGLEVALUE('IsExternal',$,IFCBOOLEAN(.T.),$);
        #21=IFCPROPERTYSET('0Ba$b1cD2eF3gH4iJ5kL6m',$,'Pset_WallCommon',$,(#20));
        #22=IFCRELDEFINESBYPROPERTIES('1Ba$b1cD2eF3gH4iJ5kL6m',$,$,$,(#10,#11),#21);
        #30=IFCPROPERTYSINGLEVALUE('Reference',$,IFCIDENTIFIER('W-200'),$);
        #31=IFCPROPERTYSET('2Ba$b1cD2eF3gH4iJ5kL6m',$,'Pset_WallCommon',$,(#30));
        #32=IFCWALLTYPE('3Ba$b1cD2eF3gH4iJ5kL6m',$,'Basic Wall 200',$,$,(#31),$,$,$,.STANDARD.);
        #33=IFCRELDEFINESBYTYPE('0Ca$b1cD2eF3gH4iJ5kL6m',$,$,$,(#10),#32);
        #40=IFCCLASSIFICATIONREFERENCE('https://uniclass','EF_25_10','Walls',$);
        #41=IFCRELASSOCIATESCLASSIFICATION('1Ca$b1cD2eF3gH4iJ5kL6m',$,$,$,(#10),#40);
        ENDSEC;
        END-ISO-10303-21;
        """;

    private static IfcModel Mau() => IfcModel.Parse(FileMau);

    // ── Bộ đọc STEP ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DocDuocLuocDoVaSoLuongThucThe()
    {
        var model = Mau();
        Assert.Equal("IFC4", model.Schema);
        Assert.Equal(13, model.Count);
        Assert.Equal(2, model.OfType("IfcWallStandardCase").Count);
        Assert.Empty(model.OfType("IfcWall")); // KHÔNG suy ra lớp cha: đây là hai tên khác nhau
    }

    [Fact]
    public void TenKieuKhongPhanBietHoaThuong()
    {
        var model = Mau();
        Assert.Single(model.OfType("ifcproject"));
        Assert.Single(model.OfType("IFCPROJECT"));
    }

    [Fact]
    public void ChuoiChuaDauChamPhayVaNgoacKhongLamGayCauTruc()
    {
        // Dấu ; và ) nằm TRONG chuỗi: bộ đọc cắt theo dấu ; ngây thơ sẽ đứt câu ở đây.
        var model = IfcModel.Parse(
            "ISO-10303-21;\nDATA;\n#1=IFCWALL('g',$,'Tuong; (dot 2)',$,$,$,$,$,$);\nENDSEC;\nEND-ISO-10303-21;\n");
        Assert.Equal("Tuong; (dot 2)", IfcModel.NameOf(model.OfType("IfcWall")[0]));
    }

    [Fact]
    public void HaiDauNhayLienNhauLaMotDauNhay()
    {
        var model = IfcModel.Parse(
            "ISO-10303-21;\nDATA;\n#1=IFCWALL('g',$,'Tuong 3''',$,$,$,$,$,$);\nENDSEC;\nEND-ISO-10303-21;\n");
        Assert.Equal("Tuong 3'", IfcModel.NameOf(model.OfType("IfcWall")[0]));
    }

    [Fact]
    public void GiaiMaDuocTenTiengVietTrongDayThoat()
    {
        // Không giải mã thì tên tiếng Việt đọc thành rác và mọi so khớp tên đều trượt.
        Assert.Equal("Tường trong 100", IfcModel.NameOf(Mau().ById(11)!));
    }

    [Theory]
    [InlineData(@"\X2\00C0\X0\", "À")]
    [InlineData(@"\X2\1EA05\X0\", "\u1ea0")] // số hex lẻ: đọc hết nhóm 4 chữ số, bỏ phần thừa
    [InlineData(@"\X\C0", "\u00c0")]
    [InlineData(@"\S\A", "\u00c1")]
    [InlineData(@"C:\\ban ve", @"C:\ban ve")]
    [InlineData("khong co day thoat", "khong co day thoat")]
    public void GiaiMaDayThoatTheoIso10303(string raw, string mong)
        => Assert.Equal(mong, IfcStepParser.DecodeEscapes(raw));

    [Fact]
    public void BoQuaChuThichVaThucTheXuongNhieuDong()
    {
        var model = IfcModel.Parse("""
            ISO-10303-21;
            /* bo qua toan bo phan nay;
               ke ca dau ; trong chu thich */
            DATA;
            #1=IFCWALL(
                'g',$,
                'Tuong',$,$,$,$,$,$);
            ENDSEC;
            END-ISO-10303-21;
            """);
        Assert.Equal("Tuong", IfcModel.NameOf(model.OfType("IfcWall")[0]));
    }

    [Fact]
    public void FileKhongCoPhanDataThiBaoLoiChuKhongTraModelRong()
    {
        var ex = Assert.Throws<IfcParseException>(
            () => IfcModel.Parse("ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nEND-ISO-10303-21;\n"));
        Assert.Contains("DATA", ex.Message);

        // File hoàn toàn không phải STEP cũng là lỗi cú pháp, không phải một model rỗng.
        Assert.Throws<IfcParseException>(() => IfcModel.Parse("day khong phai IFC\n"));
    }

    [Fact]
    public void FileHongThiKetQuaLaLoiChuKhongNemNgoaiLe()
    {
        var result = IfcChecker.Check("noi dung rac", IfcCheckSpec.Default());
        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("Không đọc được file"));
    }

    // ── Tra thuộc tính và phân loại ───────────────────────────────────────────────────────────────

    [Fact]
    public void TraDuocThuocTinhGanTrucTiep()
    {
        var model = Mau();
        Assert.True(model.TryProperty(10, "Pset_WallCommon.IsExternal", out var v));
        Assert.Equal("T", v);
    }

    [Fact]
    public void KhongNoiPsetThiKhopTenThuocTinhOBatKyPsetNao()
    {
        Assert.True(Mau().TryProperty(11, "IsExternal", out var v));
        Assert.Equal("T", v);
    }

    [Fact]
    public void NoiSaiTenPsetThiCoiNhuThieu()
    {
        Assert.False(Mau().TryProperty(10, "Pset_KhongCo.IsExternal", out _));
    }

    [Fact]
    public void ThuocTinhThuaKeTuKieuVanTraDuoc()
    {
        // #10 gắn IfcWallType qua IfcRelDefinesByType; #11 thì không.
        Assert.True(Mau().TryProperty(10, "Pset_WallCommon.Reference", out var v));
        Assert.Equal("W-200", v);
        Assert.False(Mau().TryProperty(11, "Pset_WallCommon.Reference", out _));
    }

    [Fact]
    public void PhanLoaiDocTheoMaDinhDanhChuKhongPhaiTen()
    {
        Assert.Equal(new[] { "EF_25_10" }, Mau().ClassificationsOf(10));
        Assert.Empty(Mau().ClassificationsOf(11));
    }

    [Fact]
    public void DemDuocSoLuongTheoTungKieu()
    {
        var counts = Mau().TypeCounts();

        // Nhiều nhất đứng đầu; bằng nhau thì theo tên để bảng không đổi thứ tự giữa hai lần chạy.
        Assert.Equal(2, counts[0].Value);
        Assert.Equal(
            new[] { "IFCPROPERTYSET", "IFCPROPERTYSINGLEVALUE", "IFCWALLSTANDARDCASE" },
            counts.Where(c => c.Value == 2).Select(c => c.Key));
        Assert.Equal(1, counts.Single(c => c.Key == "IFCPROJECT").Value);
    }

    // ── Kiểm cấu trúc ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FileDungThiKhongBaoLoiNao()
    {
        var spec = IfcCheckSpec.FromJson("""
            {
              "schema": "IFC4",
              "minEntities": 10,
              "rules": [
                { "type": "IfcProject", "minCount": 1, "maxCount": 1 },
                { "type": "IfcWallStandardCase", "exactCount": 2, "requireName": true,
                  "requireProperties": ["Pset_WallCommon.IsExternal"] }
              ]
            }
            """);
        var result = IfcChecker.Check(FileMau, spec);
        Assert.True(result.Ok, result.Render());
        Assert.Equal(0, result.ErrorCount);
    }

    [Fact]
    public void LechLuocDoLaLoi()
    {
        var spec = new IfcCheckSpec { Schema = "IFC2X3" };
        var result = IfcChecker.Check(FileMau, spec);
        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("IFC2X3") && f.Message.Contains("IFC4"));
    }

    [Fact]
    public void KhongKhaiLuocDoThiCanhBaoChuKhongChanNop()
    {
        var result = IfcChecker.Check(
            "ISO-10303-21;\nDATA;\n#1=IFCPROJECT('g',$,'P',$,$,$,$,$,$);\nENDSEC;\nEND-ISO-10303-21;\n",
            new IfcCheckSpec { RequireUniqueGlobalId = false });
        Assert.True(result.Ok);
        Assert.Equal(1, result.WarningCount);
    }

    [Fact]
    public void ThamChieuTroToiThucTheKhongTonTaiLaLoi()
    {
        // Bộ xuất bỏ sót một phần tử nhưng vẫn giữ quan hệ trỏ tới nó — file mở ra thiếu mà không báo gì.
        var text = FileMau.Replace("(#10,#11),#21", "(#10,#11,#99),#21");
        var result = IfcChecker.Check(text, new IfcCheckSpec());
        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("#99"));
    }

    [Fact]
    public void MaDinhDanhTrungNhauLaLoi()
    {
        var text = FileMau.Replace("'3Aa$b1cD2eF3gH4iJ5kL6m'", "'2Aa$b1cD2eF3gH4iJ5kL6m'");
        var result = IfcChecker.Check(text, new IfcCheckSpec());
        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("trùng nhau") && f.Message.Contains("#10 và #11"));
    }

    [Fact]
    public void MaDinhDanhRongLaLoi()
    {
        var text = FileMau.Replace("'3Aa$b1cD2eF3gH4iJ5kL6m'", "''");
        var result = IfcChecker.Check(text, new IfcCheckSpec());
        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("rỗng"));
    }

    [Fact]
    public void HaiThuocTinhCungTenOHaiPsetKhongBiBaoLaTrungMaDinhDanh()
    {
        // IFCPROPERTYSINGLEVALUE cũng mở đầu bằng một chuỗi. Coi mọi chuỗi ở vị trí 0 là mã định danh
        // thì file mẫu này — hai Pset cùng tên "Pset_WallCommon" — bị báo trùng mã. Báo sai kiểu đó
        // làm kỹ sư tắt bộ kiểm đi, nên nguy hiểm hơn bỏ sót.
        var text = FileMau.Replace("'Reference'", "'IsExternal'");
        var result = IfcChecker.Check(text, new IfcCheckSpec());
        Assert.True(result.Ok, result.Render());
    }

    [Theory]
    [InlineData("0Aa$b1cD2eF3gH4iJ5kL6m", true)]
    [InlineData("3Aa$b1cD2eF3gH4iJ5kL6m", true)]
    [InlineData("TreadLengthAtInnerSide", false)] // đúng 22 chữ cái nhưng ký tự đầu chỉ chở 2 bit
    [InlineData("9Aa$b1cD2eF3gH4iJ5kL6m", false)]
    [InlineData("0Aa-b1cD2eF3gH4iJ5kL6m", false)] // dấu gạch không nằm trong bảng base64 của IFC
    [InlineData("0Aa$b1cD2eF3gH4iJ5kL6", false)]  // 21 ký tự
    [InlineData("", false)]
    [InlineData(null, false)]
    public void NhanDangMaDinhDanhTheoDungDangNenCuaIfc(string? value, bool mong)
        => Assert.Equal(mong, IfcModel.LooksLikeGlobalId(value));

    [Fact]
    public void FileKhongCoMaDinhDanhNaoDungDangLaLoi()
    {
        var result = IfcChecker.Check(
            "ISO-10303-21;\nDATA;\n#1=IFCPROJECT('qua-ngan',$,'P',$,$,$,$,$,$);\nENDSEC;\nEND-ISO-10303-21;\n",
            new IfcCheckSpec());
        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("Không thực thể nào mang mã định danh"));
    }

    [Fact]
    public void SoHieuKhaiHaiLanLaLoi()
    {
        var text = FileMau.Replace("#11=IFCWALLSTANDARDCASE", "#10=IFCWALLSTANDARDCASE");
        var result = IfcChecker.Check(text, new IfcCheckSpec());
        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("khai hai lần"));
    }

    [Fact]
    public void FileRongVanDungCuPhapThiVanBiChanBoiMinEntities()
    {
        var result = IfcChecker.Check(
            "ISO-10303-21;\nHEADER;\nFILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n",
            IfcCheckSpec.Default());
        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("0 thực thể"));
    }

    // ── Quy tắc theo kiểu ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThieuSoLuongLaLoiVaNoiRoConThieuBaoNhieu()
    {
        var spec = new IfcCheckSpec { RequireUniqueGlobalId = false };
        spec.Rules.Add(new IfcTypeRule { Type = "IfcDoor", MinCount = 4 });
        var result = IfcChecker.Check(FileMau, spec);
        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("IFCDOOR: có 0") && f.Message.Contains("tối thiểu 4"));
    }

    [Fact]
    public void SaiExactCountLaLoiKeCaKhiNhieuHon()
    {
        var spec = new IfcCheckSpec { RequireUniqueGlobalId = false };
        spec.Rules.Add(new IfcTypeRule { Type = "IfcWallStandardCase", ExactCount = 1 });
        var result = IfcChecker.Check(FileMau, spec);
        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("có 2") && f.Message.Contains("đúng 1"));
    }

    [Fact]
    public void ThieuThuocTinhBatBuocLaLoiVaKeTenPhanTu()
    {
        var spec = new IfcCheckSpec { RequireUniqueGlobalId = false };
        spec.Rules.Add(new IfcTypeRule
        {
            Type = "IfcWallStandardCase",
            RequireProperties = { "Pset_WallCommon.Reference" },
        });
        var result = IfcChecker.Check(FileMau, spec);
        Assert.False(result.Ok);

        // #10 thừa kế được từ kiểu nên đạt; chỉ #11 thiếu.
        var finding = result.Findings.Single(f => f.Message.Contains("thiếu thuộc tính"));
        Assert.Contains("1/2", finding.Message);
        Assert.Contains("#11 Tường trong 100", finding.Message);
    }

    [Fact]
    public void ThuocTinhCoMatNhungBoTrongVanLaThieu()
    {
        // "Có mà rỗng" là chỗ bộ kiểm dễ dãi hay bỏ qua — bên nhận đọc file không phân biệt được
        // "chưa điền" với "không có".
        var text = FileMau.Replace("IFCBOOLEAN(.T.)", "$");
        var spec = new IfcCheckSpec { RequireUniqueGlobalId = false };
        spec.Rules.Add(new IfcTypeRule
        {
            Type = "IfcWallStandardCase",
            RequireProperties = { "Pset_WallCommon.IsExternal" },
        });
        var result = IfcChecker.Check(text, spec);
        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("bỏ trống"));
    }

    [Fact]
    public void ThieuTenLaLoiKhiRequireName()
    {
        var text = FileMau.Replace("'Tang 1'", "$");
        var spec = new IfcCheckSpec { RequireUniqueGlobalId = false };
        spec.Rules.Add(new IfcTypeRule { Type = "IfcBuildingStorey", RequireName = true });
        var result = IfcChecker.Check(text, spec);
        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("không có tên"));
    }

    [Fact]
    public void ThieuPhanLoaiLaLoiKhiRequireClassification()
    {
        var spec = new IfcCheckSpec { RequireUniqueGlobalId = false };
        spec.Rules.Add(new IfcTypeRule { Type = "IfcWallStandardCase", RequireClassification = true });
        var result = IfcChecker.Check(FileMau, spec);
        Assert.False(result.Ok);
        Assert.Contains(result.Findings, f => f.Message.Contains("chưa gán mã phân loại") && f.Message.Contains("1/2"));
    }

    [Fact]
    public void QuyTacTrenKieuKhongCoPhanTuNaoThiKhongKiemTungPhanTu()
    {
        // Không có phần tử nào thì chỉ báo thiếu số lượng một lần, không đẻ thêm dòng "thiếu thuộc tính 0/0".
        var spec = new IfcCheckSpec { RequireUniqueGlobalId = false };
        spec.Rules.Add(new IfcTypeRule { Type = "IfcDoor", MinCount = 1, RequireName = true, RequireProperties = { "X" } });
        var result = IfcChecker.Check(FileMau, spec);
        Assert.Single(result.Findings);
    }

    [Fact]
    public void ChiKeTenToiListLimitPhanTuRoiNoiConBaoNhieu()
    {
        // Một file lỗi mapping có hàng nghìn phần tử cùng lỗi; in hết là không ai đọc nổi dòng nào.
        Assert.Equal("a, b … và 3 nữa", IfcChecker.Sample(new[] { "a", "b", "c", "d", "e" }, 2));
        Assert.Equal("a, b", IfcChecker.Sample(new[] { "a", "b" }, 2));
    }

    // ── Bộ quy tắc ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void QuyTacThieuTypeThiBaoLoiKemSoThuTu()
    {
        var ex = Assert.Throws<ArgumentException>(() => IfcCheckSpec.FromJson("""{ "rules": [ { "minCount": 1 } ] }"""));
        Assert.Contains("thứ 1", ex.Message);
    }

    [Fact]
    public void JsonHongThiBaoLoiRoChuKhongNemJsonException()
    {
        Assert.Throws<ArgumentException>(() => IfcCheckSpec.FromJson("{ khong phai json"));
    }

    [Fact]
    public void BoQuyTacMacDinhKhongDoanDuAnCanBaoNhieuBucTuong()
    {
        var spec = IfcCheckSpec.Default();
        Assert.Single(spec.Rules);
        Assert.Equal("IfcProject", spec.Rules[0].Type);
        Assert.True(IfcChecker.Check(FileMau, spec).Ok);
    }

    [Fact]
    public void FileMauTrongConfigsDocDuocVaKhongTuMauThuan()
    {
        // Mẫu trong repo mà hỏng thì kỹ sư chép về sẽ nhận mã thoát 2 và tưởng công cụ hỏng.
        var path = Path.Combine(RepoRoot(), "configs", "ifc-check.sample.json");
        Assert.True(File.Exists(path), "Thiếu configs/ifc-check.sample.json.");
        var spec = IfcCheckSpec.FromJson(File.ReadAllText(path));
        Assert.NotEmpty(spec.Rules);
        Assert.All(spec.Rules, r => Assert.StartsWith("Ifc", r.Type, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BanInNoiRoDatHayKhongDat()
    {
        var ok = IfcChecker.Check(FileMau, IfcCheckSpec.Default()).Render();
        Assert.Contains("IFC4", ok);
        Assert.Contains("Đạt", ok);

        var fail = IfcChecker.Check(FileMau, new IfcCheckSpec { Schema = "IFC2X3" }).Render();
        Assert.Contains("Không đạt", fail);
        Assert.Contains("[Lỗi]", fail);
    }
}
