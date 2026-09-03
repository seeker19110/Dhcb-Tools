using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.Testing;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Mọi lệnh Revit <b>và AutoCAD</b> phải có ít nhất một ca kiểm chạy THẬT trong phần mềm tương ứng
/// (giai đoạn 8.3/8.4).
/// <para>
/// Chỉ số của lộ trình là "42/42 lệnh có test chạy trong Revit trước v1.0". Không có test này thì con số
/// đó chỉ nằm trong tài liệu: thêm một lệnh mới mà quên bộ ca kiểm sẽ không ai biết. Test cũng bắt lỗi
/// cú pháp/tên lệnh sai trong file JSON của bộ ca kiểm — trước đây chỉ phát hiện khi Revit đã mở xong.
/// </para>
/// <para>
/// Phía AutoCAD dùng cùng cơ chế: `RunTests` của Core.AutoCAD chạy qua accoreconsole
/// (`scripts/run-in-autocad-tests.ps1`), cùng tầng đánh giá <c>Shared.Logic/Testing</c>.
/// </para>
/// </summary>
public class SuiteCoverageTests
{
    private static string SuiteFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dhcb-Tools.sln")))
        {
            dir = dir.Parent;
        }
        var root = dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
        return Path.Combine(root, "tests", "suites");
    }

    private static List<TestSuite> Suites(string prefix) =>
        Directory.EnumerateFiles(SuiteFolder(), prefix + "-*.json").Select(TestSuite.Load).ToList();

    private static List<TestSuite> Suites() =>
        Suites("revit").Concat(Suites("autocad")).ToList();

    [Fact]
    public void MoiBoCaKiem_DocDuocVaCoCa()
    {
        Assert.True(Suites("revit").Count >= 3, "Cần ít nhất ba bộ Revit: kiến trúc, MEP, cấp thoát nước.");
        Assert.True(Suites("autocad").Count >= 1, "Cần ít nhất một bộ AutoCAD.");
        Assert.All(Suites(), s => Assert.NotEmpty(s.Cases));
    }

    [Theory]
    [InlineData("revit", CommandCatalog.Revit)]
    [InlineData("autocad", CommandCatalog.AutoCad)]
    public void MoiCaKiem_TroToiMotLenhCoThat(string prefix, string platform)
    {
        var unknown = Suites(prefix)
            .SelectMany(s => s.Cases)
            .Select(c => c.Command)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => CommandCatalog.Find(platform, name) == null)
            .ToList();

        Assert.True(unknown.Count == 0, "Ca kiểm gọi lệnh không có trong catalog: " + string.Join(", ", unknown));
    }

    [Theory]
    [InlineData("revit", CommandCatalog.Revit)]
    [InlineData("autocad", CommandCatalog.AutoCad)]
    public void MoiLenh_DeuCoItNhatMotCaKiem(string prefix, string platform)
    {
        var covered = new HashSet<string>(
            Suites(prefix).SelectMany(s => s.Cases).Select(c => c.Command),
            StringComparer.OrdinalIgnoreCase);

        var missing = CommandCatalog.AllFor(platform)
            .Where(c => c.Implemented && !c.Internal)   // RunTests là chính bộ chạy, không tự kiểm mình.
            .Select(c => c.Name)
            .Where(name => !covered.Contains(name))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"Lệnh {platform} chưa có ca kiểm nào chạy thật ({missing.Count}): " + string.Join(", ", missing));
    }

    /// <summary>
    /// Ca <c>skip</c> phải nói lý do. Một ca skip không lý do là chỗ để lệnh hỏng nằm im mãi mãi —
    /// báo cáo vẫn xanh vì nó "bỏ qua".
    /// </summary>
    [Fact]
    public void CaBoQua_PhaiCoLyDo()
    {
        var noReason = Suites()
            .SelectMany(s => s.Cases)
            .Where(c => c.Skip && string.IsNullOrWhiteSpace(c.SkipReason))
            .Select(c => c.DisplayName)
            .ToList();

        Assert.True(noReason.Count == 0, "Ca bỏ qua mà không ghi lý do: " + string.Join(", ", noReason));
    }
}
