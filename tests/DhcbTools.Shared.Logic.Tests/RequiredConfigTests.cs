using DhcbTools.Shared.Hosting;
using Newtonsoft.Json;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Chốt chặn cho lỗi lộ ra ở vòng kiểm thử MEP đầu tiên trong Revit (2026-09-03): gọi
/// <c>SystemColor</c> mà không kèm <c>colors</c> làm lệnh ném <c>NullReferenceException</c> trần trụi.
/// <para>
/// Nguyên nhân: <c>required</c> của C# chỉ được compiler kiểm khi dùng object initializer; Newtonsoft
/// dựng object bằng reflection nên đi vòng qua nó. Người gọi (kỹ sư qua form, hay agent qua MCP) nhận
/// một stack trace .NET thay vì "thiếu trường colors".
/// </para>
/// </summary>
public class RequiredConfigTests
{
    private sealed class DemoConfig
    {
        public required Dictionary<string, string> Colors { get; init; }

        public required string OutputPath { get; init; }

        public string? ViewTemplateName { get; init; }

        public int SpacingMm { get; init; } = 3000;
    }

    [Fact]
    public void ThieuTruongBatBuoc_LietKeDuTen()
    {
        var config = JsonConvert.DeserializeObject<DemoConfig>("{}")!;

        var missing = RequiredConfig.MissingMembers(config);

        Assert.Equal(new[] { "colors", "outputPath" }, missing.OrderBy(m => m, StringComparer.Ordinal));
    }

    [Fact]
    public void DuTruong_ThiKhongBaoGi()
    {
        var config = JsonConvert.DeserializeObject<DemoConfig>(
            @"{ ""colors"": { ""MEC"": ""#FF0000"" }, ""outputPath"": ""D:/a.csv"" }")!;

        Assert.Empty(RequiredConfig.MissingMembers(config));
    }

    /// <summary>Trường không bắt buộc và trường kiểu giá trị thiếu thì nhận mặc định, không phải lỗi.</summary>
    [Fact]
    public void TruongTuyChon_KhongBiTinhLaThieu()
    {
        var config = JsonConvert.DeserializeObject<DemoConfig>(
            @"{ ""colors"": {}, ""outputPath"": ""D:/a.csv"" }")!;

        Assert.Empty(RequiredConfig.MissingMembers(config));
        Assert.Null(config.ViewTemplateName);
        Assert.Equal(3000, config.SpacingMm);
    }

    [Fact]
    public void NemConfigException_CoMaLoiVaTenTruong()
    {
        var config = JsonConvert.DeserializeObject<DemoConfig>(@"{ ""outputPath"": ""D:/a.csv"" }")!;

        var ex = Assert.Throws<ConfigException>(() => RequiredConfig.ThrowIfIncomplete(config, "DemoConfig"));

        Assert.Contains("E-CONFIG-MISSING", ex.Message, StringComparison.Ordinal);
        Assert.Contains("colors", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("outputPath", ex.Message, StringComparison.Ordinal);
    }
}
