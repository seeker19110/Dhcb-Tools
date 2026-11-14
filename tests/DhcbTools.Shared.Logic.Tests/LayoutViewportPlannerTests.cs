using DhcbTools.Shared.Logic.Cad;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Logic thuần của lệnh <c>LayoutViewport</c> — tạo khung bản vẽ từ layout mẫu (trước đây chỉ làm tay/qua COM).</summary>
public class LayoutViewportPlannerTests
{
    private static readonly string[] Existing = { "B2", "Layout1" };

    private static LayoutSheet Sheet(string? name = "VP-01 MB B1", string? template = "B2", double? scale = 75) => new()
    {
        Name = name,
        Template = template,
        ViewCenter = new[] { -74300.0, 156300 },
        Scale = scale,
    };

    private static LayoutViewportPlan Plan(bool replace = false, params LayoutSheet?[] sheets) =>
        LayoutViewportPlanner.Plan(sheets!, Existing, replace);

    [Fact]
    public void HopLe_DayDuTruong()
    {
        var sheet = Sheet();
        sheet.Title = "MẶT BẰNG HẦM B1";
        sheet.TitleMatch = "CHI TIẾT";
        sheet.ScaleText = "TỶ LỆ 1:75";
        sheet.ViewportHandle = "9E3B0";
        sheet.Lock = false;

        var plan = Plan(false, sheet, Sheet("VP-02"));

        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Empty(plan.Replaced);
    }

    [Fact]
    public void DanhSachRongHoacQuaNhieu_BaoLoi()
    {
        Assert.Contains("rỗng", LayoutViewportPlanner.Plan(null, Existing, false).Errors[0]);
        Assert.Contains("rỗng", LayoutViewportPlanner.Plan(new List<LayoutSheet>(), Existing, false).Errors[0]);
        var many = Enumerable.Range(0, LayoutViewportPlanner.MaxSheets + 1).Select(i => Sheet("S" + i)).ToList();
        Assert.Contains("tách lô", LayoutViewportPlanner.Plan(many, Existing, false).Errors[0]);
    }

    [Theory]
    [InlineData(null, "\"name\" rỗng")]
    [InlineData("Model", "\"name\" rỗng")]
    [InlineData("A|B", "ký tự cấm")]
    public void TenSai_BaoLoi(string? name, string expected)
    {
        var plan = Plan(false, Sheet(name));

        Assert.Contains(plan.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void KhungRong_VaTenTrungTrongDanhSach()
    {
        var plan = Plan(false, null, Sheet("VP"), Sheet("vp"));

        Assert.Contains(plan.Errors, e => e.Contains("#1: khung rỗng", StringComparison.Ordinal));
        Assert.Contains(plan.Errors, e => e.Contains("trùng trong danh sách", StringComparison.Ordinal));
    }

    [Fact]
    public void LayoutDaCo_BaoLoiTruKhiReplaceExisting()
    {
        Assert.Contains(Plan(false, Sheet("layout1")).Errors, e => e.Contains("replaceExisting", StringComparison.Ordinal));

        var replace = Plan(true, Sheet("Layout1"));
        Assert.True(replace.IsValid, string.Join("; ", replace.Errors));
        Assert.Equal(new[] { "Layout1" }, replace.Replaced);
    }

    [Theory]
    [InlineData(null, "thiếu \"template\"")]
    [InlineData("model", "không phải Model")]
    [InlineData("B9", "không có layout mẫu \"B9\". Layout có: B2, Layout1")]
    public void MauSai_BaoLoi(string? template, string expected)
    {
        Assert.Contains(Plan(false, Sheet(template: template)).Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void MauCungLaLayoutSeBiThay_BaoLoi()
    {
        var plan = Plan(true, Sheet("B2", template: "Layout1"), Sheet("X", template: "B2"));

        Assert.Contains(plan.Errors, e => e.Contains("cũng là layout sẽ bị thay", StringComparison.Ordinal));
    }

    [Fact]
    public void TamTiLeHandleSai_BaoTungTruong()
    {
        var bad = Sheet(scale: 0);
        bad.ViewCenter = new[] { double.NaN, 1 };
        bad.ViewportHandle = "XYZ";
        var nan = Sheet("N", scale: double.NaN);
        nan.ViewCenter = null;
        var missing = Sheet("M", scale: null);
        var inf = Sheet("I", scale: double.PositiveInfinity);

        var plan = Plan(false, bad, nan, missing, inf);

        Assert.Contains(plan.Errors, e => e.Contains("\"viewCenter\"", StringComparison.Ordinal));
        Assert.Equal(4, plan.Errors.Count(e => e.Contains("\"scale\"", StringComparison.Ordinal)));
        Assert.Contains(plan.Errors, e => e.Contains("\"viewportHandle\" \"XYZ\"", StringComparison.Ordinal));
    }

    [Fact]
    public void TieuDe_CanTitleMatch_VaKhongRong()
    {
        var noMatch = Sheet("A");
        noMatch.Title = "X";
        var orphan = Sheet("B");
        orphan.TitleMatch = "CHI TIẾT";
        var blank = Sheet("C");
        blank.Title = " ";
        blank.ScaleText = "";
        blank.TitleMatch = "CHI";

        var plan = Plan(false, noMatch, orphan, blank);

        Assert.Contains(plan.Errors, e => e.StartsWith("#1", StringComparison.Ordinal) && e.Contains("cần \"titleMatch\"", StringComparison.Ordinal));
        Assert.Contains(plan.Errors, e => e.StartsWith("#2", StringComparison.Ordinal) && e.Contains("chỉ dùng kèm", StringComparison.Ordinal));
        Assert.Contains(plan.Errors, e => e.Contains("\"title\" rỗng", StringComparison.Ordinal));
        Assert.Contains(plan.Errors, e => e.Contains("\"scaleText\" rỗng", StringComparison.Ordinal));
    }

    [Fact]
    public void TenLayout_VaTiLe()
    {
        Assert.True(LayoutViewportPlanner.IsValidLayoutName(" VP-01 MB B1 "));
        Assert.False(LayoutViewportPlanner.IsValidLayoutName(new string('a', 256)));
        Assert.Equal(1.0 / 75, LayoutViewportPlanner.CustomScale(75), 12);
        Assert.Equal("4.2", LayoutViewportPlanner.Number(4.2));
    }

    private static readonly List<ViewportCandidate> Viewports = new()
    {
        new ViewportCandidate { Index = 0, Handle = 0x9E000, Width = 420, Height = 297, IsOverall = true },
        new ViewportCandidate { Index = 3, Handle = 0x9E3B0, Width = 343, Height = 280 },
        new ViewportCandidate { Index = 5, Handle = 0x9E3C0, Width = 60, Height = 40 },
    };

    [Fact]
    public void ChonViewport_MacDinhLaViewportNoiLonNhat()
    {
        var chosen = LayoutViewportPlanner.ChooseViewport(Viewports, null, "B2", out var error);

        Assert.Null(error);
        Assert.Equal(0x9E3B0, chosen!.Handle);
    }

    [Fact]
    public void ChonViewport_TheoHandle()
    {
        var chosen = LayoutViewportPlanner.ChooseViewport(Viewports, "0x9e3c0", "B2", out var error);

        Assert.Null(error);
        Assert.Equal(5, chosen!.Index);
    }

    [Fact]
    public void ChonViewport_HandleSaiHoacViewportTong_BaoLoi()
    {
        Assert.Null(LayoutViewportPlanner.ChooseViewport(Viewports, "ABC", "B2", out var missing));
        Assert.Contains("không có viewport handle ABC", missing);
        Assert.Contains("9E3B0 (343×280)", missing);

        Assert.Null(LayoutViewportPlanner.ChooseViewport(Viewports, "9E000", "B2", out var overall));
        Assert.Contains("viewport tổng", overall);

        Assert.Null(LayoutViewportPlanner.ChooseViewport(Viewports.Take(1).ToList(), null, "B2", out var none));
        Assert.Contains("không có viewport nổi", none);

        Assert.Null(LayoutViewportPlanner.ChooseViewport(Viewports.Take(1).ToList(), "1", "B2", out var noneListed));
        Assert.Contains("(không có)", noneListed);
    }

    [Fact]
    public void TachMText_GiuMaDinhDangHaiDau()
    {
        var parts = LayoutViewportPlanner.Split("\\pxqc;{\\W0.9;CHI TIẾT LẮP ĐẶT}");

        Assert.Equal("\\pxqc;{\\W0.9;", parts.Prefix);
        Assert.Equal("CHI TIẾT LẮP ĐẶT", parts.Plain);
        Assert.Equal("}", parts.Suffix);
    }

    [Fact]
    public void TachMText_MaBatTat_KyTuThoat_XuongDong_PhanSo()
    {
        var parts = LayoutViewportPlanner.Split("{\\L\\fArial|b1;\\{A\\}\\PB\\S1/2;}\\l");

        Assert.Equal("{\\L\\fArial|b1;", parts.Prefix);
        Assert.Equal("\\{A\\}\\PB\\S1/2;", parts.Plain);
        Assert.Equal("}\\l", parts.Suffix);
    }

    [Fact]
    public void TachMText_KhongCoChu_HoacMaCutCuoi()
    {
        Assert.Equal("{\\H2;}", LayoutViewportPlanner.Split("{\\H2;}").Prefix);
        Assert.Equal(string.Empty, LayoutViewportPlanner.Split(null).Plain);
        Assert.Equal("{\\H2", LayoutViewportPlanner.Split("{\\H2").Prefix);
        Assert.Equal("AB\\S1/2", LayoutViewportPlanner.Split("AB\\S1/2").Plain);
        Assert.Equal("A\\", LayoutViewportPlanner.Split("A\\").Plain);
    }

    [Fact]
    public void ThayChu_ThoatKyTuDacBiet()
    {
        var replaced = LayoutViewportPlanner.ReplacePlain("\\pxqc;{\\W0.9;CHI TIẾT}", "MB {B1}\\A\r\nTẦNG 2");

        Assert.Equal("\\pxqc;{\\W0.9;MB \\{B1\\}\\\\A\\PTẦNG 2}", replaced);
    }

    [Fact]
    public void DongTiLe_BoMaChieuCao()
    {
        Assert.Equal("\\pxqc;{\\W0.9;TỶ LỆ 1:75}", LayoutViewportPlanner.ScaleTextContents("\\pxqc;{\\H3.5;\\W0.9;CHI TIẾT\\H1;}", "TỶ LỆ 1:75"));
    }

    [Theory]
    [InlineData(0, 4.2)]   // Top: cả tiêu đề nằm dưới điểm chèn
    [InlineData(1, 3.9)]   // Middle: 1.5 + 1.2 + 1.2
    [InlineData(2, 3.6)]   // Bottom: 0 + 1.2 + 2.4
    public void DoiXuong_TheoKieuCanLe(int row, double expected)
    {
        Assert.Equal(expected, LayoutViewportPlanner.ScaleTextDrop(row, 3, 2.4), 9);
    }
}
