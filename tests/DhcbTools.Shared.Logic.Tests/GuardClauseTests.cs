using System.Globalization;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Checks;
using DhcbTools.Shared.Logic.Geometry;
using DhcbTools.Shared.Logic.Mep;
using DhcbTools.Shared.Logic.Progress;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Các nhánh phòng vệ (đối số null/âm) và nhánh mặc định của switch. Chúng là hợp đồng công khai của
/// từng hàm — nếu ai đó lỡ bỏ guard đi, một đối số null sẽ đi sâu vào trong rồi mới nổ ở chỗ khó truy.
/// </summary>
public class GuardClauseTests
{
    [Fact]
    public void MakeUnique_UsedNamesNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => FileNaming.MakeUnique("a.pdf", null!));
    }

    [Fact]
    public void GridClustering_SegmentsNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => GridClustering.Cluster(null!));
    }

    [Fact]
    public void GridNaming_Letter_IndexAm_NemOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GridNaming.Letter(-1));
    }

    /// <summary>Quy tắc đánh trục ngược chiều: trục ngang đánh từ trên xuống thay vì dưới lên.</summary>
    [Fact]
    public void GridNaming_HorizontalTrenXuong_DaoThuTu()
    {
        var grids = new List<GridLine>
        {
            new GridLine(false, 0, 0, 1000, 1),
            new GridLine(false, 5000, 0, 1000, 1),
        };

        GridNaming.Apply(grids, new GridNamingRule { HorizontalBottomToTop = false });

        Assert.Equal("2", grids.Single(g => Math.Abs(g.Position) < 1e-9).Name);
        Assert.Equal("1", grids.Single(g => Math.Abs(g.Position - 5000) < 1e-9).Name);
    }

    /// <summary>Đoạn ngang đi ngược chiều cho atan2 = 180°, phải quy về 0 chứ không phải 180.</summary>
    [Fact]
    public void Segment2D_DoanNgangNguocChieu_Goc0Do()
    {
        Assert.Equal(0.0, new Segment2D(1000, 0, 0, 0).AngleDeg, 9);
    }

    [Fact]
    public void FlowNumbering_EdgesNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => FlowNumbering.Assign<string>(null!, "a"));
    }

    [Fact]
    public void NumberingPlanner_ItemsNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => NumberingPlanner.Order<string>(null!, ScanDirection.LeftToRightThenTopToBottom));
    }

    [Fact]
    public void NumberingPlanner_AssignItemsNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => NumberingPlanner.Assign<string>(null!, "P-", 1, 1, 2));
    }

    [Fact]
    public void PaletteGenerator_IndexAm_NemOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PaletteGenerator.ByIndex(-1));
    }

    [Fact]
    public void CsvText_ReadRecords_ReaderNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => CsvText.ReadRecords((TextReader)null!).ToList());
    }

    [Fact]
    public void Precondition_ChiCoCanhBao_TraVeCanhBao()
    {
        var result = Precondition.First(
            Precondition.NonEmptyInput("KiemTra", "ống", 5, "chọn tầng khác"),
            Precondition.LinkedModels("KiemTra", 2, new[] { "KT.rvt" }, "includeLinkedModels"));

        Assert.True(result.Warns);
        Assert.False(result.Blocks);
    }

    /// <summary>Giai đoạn không có tên chuẩn (ChuaCoDuLieu) trả chuỗi rỗng, không ném.</summary>
    [Fact]
    public void ConstructionStatusValue_GiaiDoanKhongCoTen_TraChuoiRong()
    {
        Assert.Equal(string.Empty, ConstructionStatusValue.CanonicalOf(ConstructionStage.ChuaCoDuLieu));
    }

    [Fact]
    public void StatusRollRow_GiaiDoanChuaCoSoLieu_ChieuDaiBang0()
    {
        Assert.Equal(0.0, new StatusRollRow("Tầng 1").LengthMmOf(ConstructionStage.DaLap));
    }

    [Fact]
    public void StatusItem_GiuLaiElementId()
    {
        Assert.Equal(4242L, new StatusItem("Tầng 1", ConstructionStage.DaLap, elementId: 4242).ElementId);
    }

    [Fact]
    public void ProgressCsv_TryParseDate_ChuoiRong_TraFalse()
    {
        Assert.False(ProgressCsv.TryParseDate("   ", out _));
    }

    [Fact]
    public void MepLayout_DoiFootVuongSangMetVuong()
    {
        Assert.Equal(0.09290304, MepLayout.SquareFeetToSquareMetres(1.0), 9);
    }

    /// <summary>"right:n" cắt n ký tự cuối; định dạng lạ thì trả nguyên giá trị.</summary>
    [Theory]
    [InlineData("right:3", "ABCDEF", "DEF")]
    [InlineData("right:99", "ABC", "ABC")]
    [InlineData("khong-biet", "ABC", "ABC")]
    [InlineData("right:xyz", "ABC", "ABC")]
    public void NamePattern_DinhDangRightVaDinhDangLa(string fmt, string value, string expected)
    {
        var pattern = new NamePattern("{a:" + fmt + "}");

        Assert.Equal(expected, pattern.Apply(0, new Dictionary<string, string> { ["a"] = value }));
    }

    /// <summary>Hai chuỗi số cùng độ dài nhưng khác chữ số: so sánh ordinal quyết định.</summary>
    [Fact]
    public void NaturalComparer_HaiSoCungDoDai_SoSanhTheoChuSo()
    {
        Assert.True(NaturalComparer.Instance.Compare("A12", "A13") < 0);
        Assert.True(NaturalComparer.Instance.Compare("A13", "A12") > 0);
    }
}
