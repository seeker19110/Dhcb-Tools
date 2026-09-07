using System.Linq;
using DhcbTools.Shared.Logic.AsBuilt;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Mục 11.6: khuôn dấu bản vẽ hoàn công theo Phụ lục IIb, Nghị định 207/2026/NĐ-CP (trang 3–4, đọc từ văn bản
/// gốc trên Công báo Chính phủ 2026-09-07). Test này giữ đúng bố cục pháp luật quy định (số cột, nhãn từng
/// cột, hai mẫu khác nhau ở đâu) — không phải hình học "trông đẹp".
/// </summary>
public class AsBuiltStampLayoutTests
{
    private static AsBuiltStampConfig Mau1() => new AsBuiltStampConfig
    {
        Mau = 1,
        TenNhaThau = "Công ty TNHH Xây dựng ABC",
        NguoiLap = "Nguyễn Văn A",
        ChiHuyTruongHoacGiamDocDuAn = "Trần Văn B",
        TuVanGiamSatTruong = "Lê Văn C",
    };

    private static AsBuiltStampConfig Mau2() => new AsBuiltStampConfig
    {
        Mau = 2,
        TenNhaThau = "Công ty TNHH Xây dựng ABC",
        NguoiLap = "Nguyễn Văn A",
        ChiHuyTruongNhaThauChinh = "Phạm Văn D",
        TuVanGiamSatTruong = "Lê Văn C",
    };

    [Fact]
    public void Validate_Mau1_ThieuChiHuyTruong_BaoThieu()
    {
        var config = Mau1();
        config.ChiHuyTruongHoacGiamDocDuAn = null;
        var missing = AsBuiltStampBuilder.Validate(config);
        Assert.Contains("chiHuyTruongHoacGiamDocDuAn", missing);
        // Trường chỉ dùng ở Mẫu 2 không bị đòi ở Mẫu 1.
        Assert.DoesNotContain("chiHuyTruongNhaThauChinh", missing);
        Assert.DoesNotContain("chiHuyTruongNhaThauPhu", missing);
    }

    [Fact]
    public void Validate_Mau2_ThieuChiHuyTruongNhaThauChinh_BaoThieu()
    {
        var config = Mau2();
        config.ChiHuyTruongNhaThauChinh = null;
        var missing = AsBuiltStampBuilder.Validate(config);
        Assert.Contains("chiHuyTruongNhaThauChinh", missing);
    }

    [Fact]
    public void Validate_Mau2_KhongCoNhaThauPhu_KhongBaoThieu()
    {
        // Hợp đồng thầu chính/EPC/chìa khoá trao tay có thể không có thầu phụ — không được ép có.
        var config = Mau2();
        config.ChiHuyTruongNhaThauPhu = null;
        Assert.Empty(AsBuiltStampBuilder.Validate(config));
    }

    [Fact]
    public void Validate_TrucBonChung_ThieuTuVanGiamSatTruong_BaoCaHaiMau()
    {
        var m1 = Mau1(); m1.TuVanGiamSatTruong = string.Empty;
        var m2 = Mau2(); m2.TuVanGiamSatTruong = string.Empty;
        Assert.Contains("tuVanGiamSatTruong", AsBuiltStampBuilder.Validate(m1));
        Assert.Contains("tuVanGiamSatTruong", AsBuiltStampBuilder.Validate(m2));
    }

    [Fact]
    public void Validate_MauNgoaiPham_BaoRoGiaTri()
    {
        var config = Mau1();
        config.Mau = 3;
        var missing = AsBuiltStampBuilder.Validate(config);
        var only = Assert.Single(missing);
        Assert.Contains("mau", only);
        Assert.Contains("3", only);
    }

    [Fact]
    public void Build_Mau1_BaCot_DungNhanTheoPhuLucIIb()
    {
        var layout = AsBuiltStampBuilder.Build(Mau1());
        var labels = layout.Texts.Where(t => t.Style == StampTextStyle.Bold).Select(t => t.Text).ToList();

        Assert.Contains("Công ty TNHH Xây dựng ABC", labels);
        Assert.Contains("BẢN VẼ HOÀN CÔNG", labels);
        Assert.Contains("Người lập", labels);
        Assert.Contains("Chỉ huy trưởng công trình hoặc giám đốc dự án", labels);
        Assert.Contains("Tư vấn giám sát trưởng", labels);
        // Mẫu 1 không có cột nhà thầu phụ/chính.
        Assert.DoesNotContain(labels, l => l.Contains("nhà thầu"));
    }

    [Fact]
    public void Build_Mau2_BonCot_TachRieng_ThauPhuVaThauChinh()
    {
        var layout = AsBuiltStampBuilder.Build(Mau2());
        var labels = layout.Texts.Where(t => t.Style == StampTextStyle.Bold).Select(t => t.Text).ToList();

        Assert.Contains("Chỉ huy trưởng hoặc giám đốc dự án của nhà thầu phụ", labels);
        Assert.Contains("Chỉ huy trưởng hoặc giám đốc dự án của nhà thầu chính", labels);
        // Mẫu 2 không dùng nhãn gộp của Mẫu 1.
        Assert.DoesNotContain(labels, l => l == "Chỉ huy trưởng công trình hoặc giám đốc dự án");
    }

    [Fact]
    public void Build_KhongCoNhaThauPhu_GiaTriODangCham_KhongBiaTen()
    {
        var config = Mau2();
        config.ChiHuyTruongNhaThauPhu = null;
        var layout = AsBuiltStampBuilder.Build(config);
        // Ba dòng tiêu đề đứng trước, rồi mỗi cột đúng 3 mục (nhãn/chú thích/giá trị) theo thứ tự khai —
        // cột thứ hai (chỉ số 1) của Mẫu 2 là "nhà thầu phụ".
        var column = layout.Texts.Skip(3).Skip(1 * 3).Take(3).ToList();
        Assert.Contains("nhà thầu phụ", column[0].Text);
        Assert.Equal(".....", column[2].Text);
    }

    [Fact]
    public void Build_NgayThangNamRong_GiuChamChoDienTay()
    {
        var layout = AsBuiltStampBuilder.Build(Mau1());
        var dateLine = layout.Texts.Single(t => t.Text.StartsWith("Ngày "));
        Assert.Equal("Ngày ..... tháng ..... năm .....", dateLine.Text);
    }

    [Fact]
    public void Build_NgayThangNamCoGiaTri_ThayDungCho()
    {
        var config = Mau1();
        config.Ngay = "07"; config.Thang = "09"; config.Nam = "2026";
        var layout = AsBuiltStampBuilder.Build(config);
        var dateLine = layout.Texts.Single(t => t.Text.StartsWith("Ngày "));
        Assert.Equal("Ngày 07 tháng 09 năm 2026", dateLine.Text);
    }

    [Fact]
    public void Build_MoiNetVaChu_NamTrongBienKhuonDau()
    {
        var layout = AsBuiltStampBuilder.Build(Mau2());
        Assert.True(layout.WidthMm > 0);
        Assert.True(layout.HeightMm > 0);
        foreach (var line in layout.Lines)
        {
            Assert.InRange(line.X1Mm, -0.01, layout.WidthMm + 0.01);
            Assert.InRange(line.X2Mm, -0.01, layout.WidthMm + 0.01);
            Assert.InRange(line.Y1Mm, -0.01, layout.HeightMm + 0.01);
            Assert.InRange(line.Y2Mm, -0.01, layout.HeightMm + 0.01);
        }

        foreach (var text in layout.Texts)
        {
            Assert.InRange(text.XMm, -0.01, layout.WidthMm + 0.01);
            Assert.InRange(text.YMm, -0.01, layout.HeightMm + 0.01);
            Assert.True(text.WidthMm > 0);
        }
    }

    [Theory]
    [InlineData(0, 0, 50, 0, 50)]
    [InlineData(0, 50, 50, 0, 0)]
    [InlineData(10, 20, 50, 10, 30)]
    public void ToAnchorRelative_LatChieuYSangHeRevit(double xMm, double yMm, double heightMm, double expectU, double expectV)
    {
        var (u, v) = AsBuiltStampBuilder.ToAnchorRelative(xMm, yMm, heightMm);
        Assert.Equal(expectU, u, 6);
        Assert.Equal(expectV, v, 6);
    }

    [Fact]
    public void Build_ConLuoiDoc_ChiaDungSoCot()
    {
        // Mẫu 1: 3 cột → 2 nét dọc chia ô (không tính viền ngoài) ở hàng dữ liệu.
        var layout1 = AsBuiltStampBuilder.Build(Mau1());
        var innerVertical1 = layout1.Lines.Count(l => l.X1Mm == l.X2Mm && l.X1Mm > 0.01 && l.X1Mm < layout1.WidthMm - 0.01);
        Assert.Equal(2, innerVertical1);

        // Mẫu 2: 4 cột → 3 nét dọc chia ô.
        var layout2 = AsBuiltStampBuilder.Build(Mau2());
        var innerVertical2 = layout2.Lines.Count(l => l.X1Mm == l.X2Mm && l.X1Mm > 0.01 && l.X1Mm < layout2.WidthMm - 0.01);
        Assert.Equal(3, innerVertical2);
    }
}
