using DhcbTools.Shared.Hosting;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>Công tắc <c>{"bridge": {"enabled": false}}</c> trong settings.json — mặc định BẬT, đọc hỏng thì BẬT kèm cảnh báo.</summary>
public sealed class BridgeSettingsTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "dhcb-settings-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }

    private BridgeSettings With(string json)
    {
        File.WriteAllText(_file, json);
        return BridgeSettings.Load(_file);
    }

    [Fact]
    public void KhongCoFile_BatVaKhongCanhBao()
    {
        var s = BridgeSettings.Load(_file);
        Assert.True(s.Enabled);
        Assert.Null(s.Warning);
        Assert.EndsWith(Path.Combine("DHCB", "settings.json"), BridgeSettings.DefaultPath);
    }

    [Theory]
    [InlineData("{\"bridge\": {\"enabled\": false}}", false)]
    [InlineData("{\"bridge\": {\"enabled\": true}}", true)]
    [InlineData("{\"updaters\": {\"elevation\": true}}", true)]   // file chỉ có công tắc updater
    [InlineData("{\"bridge\": {}}", true)]
    public void DocDungCongTac(string json, bool enabled)
    {
        var s = With(json);
        Assert.Equal(enabled, s.Enabled);
        Assert.Null(s.Warning);
    }

    [Theory]
    [InlineData("{\"bridge\": {\"enabled\": \"false\"}}")]   // chuỗi, không phải boolean
    [InlineData("{\"bridge\": false}")]                      // thiếu tầng "enabled"
    [InlineData("{ hỏng")]
    [InlineData("[1, 2]")]
    public void SaiDang_VanBatVaCanhBao(string json)
    {
        var s = With(json);
        Assert.True(s.Enabled);
        Assert.Contains("Bridge vẫn BẬT", s.Warning);
    }
}
