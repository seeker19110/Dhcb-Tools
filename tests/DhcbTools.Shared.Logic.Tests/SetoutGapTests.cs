using DhcbTools.Shared.Logic.Setout;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Các cột và nhánh xuất CSV định vị ít dùng: Code/Level/ElementId, số lẻ âm, format null,
/// và loại điểm không nằm trong bảng thứ tự.
/// </summary>
public class SetoutGapTests
{
    [Fact]
    public void Write_FormatNull_NemArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => SetoutCsv.Write(Array.Empty<SetoutPoint>(), null!));
    }

    [Theory]
    [InlineData(SetoutColumn.Code, "Code")]
    [InlineData(SetoutColumn.Level, "Level")]
    [InlineData(SetoutColumn.ElementId, "ElementId")]
    public void HeaderOf_CotPhu_DungTieuDeASCII(SetoutColumn column, string expected)
    {
        Assert.Equal(expected, SetoutColumns.HeaderOf(column));
    }

    /// <summary>Giá trị enum ngoài bảng vẫn ra một tiêu đề đọc được, không ném và không rỗng.</summary>
    [Fact]
    public void HeaderOf_CotLa_TraTenEnum()
    {
        Assert.Equal("999", SetoutColumns.HeaderOf((SetoutColumn)999));
    }

    [Fact]
    public void CellOf_CotCodeVaLevel_LayDungGiaTriDaLamSach()
    {
        var point = new SetoutPoint("P1", 1000, 2000, 3000)
        {
            Code = "TIM",
            Level = "Tầng  1\nkhối A",
        };

        Assert.Equal("TIM", SetoutCsv.CellOf(point, SetoutColumn.Code, metres: true, decimals: 3));
        Assert.Equal("Tầng 1 khối A", SetoutCsv.CellOf(point, SetoutColumn.Level, metres: true, decimals: 3));
    }

    [Fact]
    public void CellOf_CotLa_TraChuoiRong()
    {
        var point = new SetoutPoint("P1", 1000, 2000, 3000);

        Assert.Equal(string.Empty, SetoutCsv.CellOf(point, (SetoutColumn)999, metres: true, decimals: 3));
    }

    /// <summary>Số lẻ âm là cấu hình hỏng; làm tròn về số nguyên thay vì để Math.Round ném.</summary>
    [Fact]
    public void FormatCoordinate_SoLeAm_LamTronVeSoNguyen()
    {
        Assert.Equal("1235", SetoutCsv.FormatCoordinate(1234.6, metres: false, decimals: -1));
    }

    /// <summary>Danh sách nguồn rỗng cho ra kế hoạch rỗng, không ném.</summary>
    [Fact]
    public void Plan_KhongCoNguon_TraKeHoachRong()
    {
        Assert.Empty(SetoutPlanner.Plan(Array.Empty<SetoutSource>()).Points);
        Assert.Empty(SetoutPlanner.Plan(null!).Points);
    }

    [Fact]
    public void Collapse_ChuoiRong_TraChuoiRong()
    {
        Assert.Equal(string.Empty, SetoutPlanner.Collapse("   "));
    }

    /// <summary>Thứ tự điểm trên cùng một phần tử: tim → đầu → giữa → cuối → tâm hộp bao; loại lạ xuống cuối.</summary>
    [Fact]
    public void Plan_ThuTuTheoLoaiDiem_LoaiLaXuongCuoi()
    {
        var sources = new[]
        {
            new SetoutSource("loại lạ", 0, 0, 0) { ElementId = 7 },
            new SetoutSource("tâm hộp bao", 1, 0, 0) { ElementId = 7 },
            new SetoutSource("giữa", 2, 0, 0) { ElementId = 7 },
            new SetoutSource("tim", 3, 0, 0) { ElementId = 7 },
        };

        var kinds = SetoutPlanner.Plan(sources).Points.Select(p => p.Kind).ToList();

        Assert.Equal(new[] { "tim", "giữa", "tâm hộp bao", "loại lạ" }, kinds);
    }
}
