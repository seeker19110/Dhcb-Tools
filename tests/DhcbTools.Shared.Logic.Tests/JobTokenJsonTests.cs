using DhcbTools.Shared.Logic.Batch;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Thay token bên trong config JSON của một step/ca kiểm.
/// <para>
/// Lỗi có thật (vòng chạy trong Revit 2026-09-03): <c>RunTestsCommand</c> thay token trên chuỗi JSON đã
/// serialize rồi mới parse lại. Token <c>{suiteFolder}</c> trả về đường dẫn Windows
/// <c>C:\Users\…</c>, mà <c>\U</c> không phải escape hợp lệ trong JSON, nên cả config vỡ với
/// "Bad JSON escape sequence: \U" — trong khi file bộ ca kiểm hoàn toàn đúng. Cách đúng là đi theo từng
/// giá trị của cây JSON, đó là việc của <see cref="JobTokens.ExpandIn"/>.
/// </para>
/// </summary>
public class JobTokenJsonTests
{
    private static readonly DateTime RunTime = new(2026, 9, 3, 10, 35, 0);

    private static JobTokenContext WindowsContext()
    {
        var context = new JobTokenContext("D:/out", "KT-01", RunTime);
        context.Extra["suiteFolder"] = @"C:\Users\liend\Dhcb Tools\tests\suites";
        return context;
    }

    [Fact]
    public void DuongDanWindows_KhongLamVoConfig()
    {
        var config = JObject.Parse(
            @"{ ""gridCsvPath"": ""{suiteFolder}/fixtures/grids.csv"", ""outputPath"": ""{outputFolder}/ket-qua.csv"" }");

        JobTokens.ExpandIn(config, WindowsContext());

        Assert.Equal(@"C:\Users\liend\Dhcb Tools\tests\suites/fixtures/grids.csv", (string?)config["gridCsvPath"]);
        Assert.Equal("D:/out/ket-qua.csv", (string?)config["outputPath"]);
    }

    [Fact]
    public void DiSauVaoObjectVaMang()
    {
        var config = JObject.Parse(
            @"{ ""nested"": { ""files"": [ ""{suiteFolder}/a.csv"", ""{outputFolder}/b.csv"" ] } }");

        JobTokens.ExpandIn(config, WindowsContext());

        var files = (JArray)config["nested"]!["files"]!;
        Assert.Equal(@"C:\Users\liend\Dhcb Tools\tests\suites/a.csv", (string?)files[0]);
        Assert.Equal("D:/out/b.csv", (string?)files[1]);
    }

    /// <summary>Giá trị không phải chuỗi phải giữ nguyên kiểu — form động và lệnh đều dựa vào kiểu JSON.</summary>
    [Fact]
    public void GiuNguyenKieuSoVaBoolean()
    {
        var config = JObject.Parse(@"{ ""spacingMm"": 3000, ""dryRun"": true, ""tyLe"": 1.5 }");

        JobTokens.ExpandIn(config, WindowsContext());

        Assert.Equal(JTokenType.Integer, config["spacingMm"]!.Type);
        Assert.Equal(JTokenType.Boolean, config["dryRun"]!.Type);
        Assert.Equal(JTokenType.Float, config["tyLe"]!.Type);
    }

    /// <summary>Chuỗi có dấu nháy kép cũng không được làm hỏng JSON — cùng gốc lỗi với dấu gạch chéo ngược.</summary>
    [Fact]
    public void GiaTriCoDauNhayKep_VanAnToan()
    {
        var context = new JobTokenContext("D:/out", "KT-01", RunTime);
        context.Extra["ten"] = "Nhà \"A\"";

        var config = JObject.Parse(@"{ ""projectName"": ""{ten} - giai đoạn 1"" }");
        JobTokens.ExpandIn(config, context);

        Assert.Equal("Nhà \"A\" - giai đoạn 1", (string?)config["projectName"]);
    }
}
