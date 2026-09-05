using DhcbTools.Shared.Logic.Ai;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Chuẩn hoá tên tầng và cảnh báo tầng trùng khi đọc thuyết minh.</summary>
public class SpecTextExtractorGapTests
{
    [Fact]
    public void Extract_VanBanRong_CanhBaoRoRang()
    {
        var result = SpecTextExtractor.Extract("   ");

        Assert.Empty(result.Levels);
        Assert.Contains("Văn bản rỗng.", result.Warnings);
    }

    /// <summary>Cùng một tầng khai hai lần: giữ lần đầu và nói rõ dòng nào bị bỏ, không lặng lẽ ghi đè.</summary>
    [Fact]
    public void Extract_TangTrungTen_GiuLanDauVaCanhBao()
    {
        var result = SpecTextExtractor.Extract("Tầng 2: +3.600\nTầng 2: +7.200");

        var level = Assert.Single(result.Levels);
        Assert.Equal(3600, level.ElevationMm);
        Assert.Contains(result.Warnings, w => w.Contains("xuất hiện nhiều lần"));
    }

    [Theory]
    [InlineData("Sân thượng: +21.600", "Sân thượng")]
    [InlineData("Tầng trệt: +0.000", "Tầng trệt")]
    [InlineData("Tầng lửng: +2.400", "Tầng lửng")]
    public void Extract_ChuanHoaTenTang(string line, string expected)
    {
        Assert.Equal(expected, Assert.Single(SpecTextExtractor.Extract(line).Levels).Name);
    }

    /// <summary>Tên không có số và không khớp quy ước nào thì giữ nguyên như trong thuyết minh.</summary>
    [Fact]
    public void NormalizeLevelName_TenLa_GiuNguyen()
    {
        Assert.Equal("Tầng", SpecTextExtractor.NormalizeLevelName("Tầng"));
    }
}
