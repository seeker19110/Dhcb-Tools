using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.Testing;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Mọi lệnh Revit phải có ít nhất một ca kiểm chạy THẬT trong Revit (giai đoạn 8.3/8.4).
/// <para>
/// Chỉ số của lộ trình là "42/42 lệnh có test chạy trong Revit trước v1.0". Không có test này thì con số
/// đó chỉ nằm trong tài liệu: thêm một lệnh mới mà quên bộ ca kiểm sẽ không ai biết. Test cũng bắt lỗi
/// cú pháp/tên lệnh sai trong file JSON của bộ ca kiểm — trước đây chỉ phát hiện khi Revit đã mở xong.
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

    private static List<TestSuite> Suites() =>
        Directory.EnumerateFiles(SuiteFolder(), "revit-*.json").Select(TestSuite.Load).ToList();

    [Fact]
    public void MoiBoCaKiem_DocDuocVaCoCa()
    {
        var suites = Suites();
        Assert.True(suites.Count >= 2, "Cần ít nhất bộ smoke (kiến trúc) và bộ mep.");
        Assert.All(suites, s => Assert.NotEmpty(s.Cases));
    }

    [Fact]
    public void MoiCaKiem_TroToiMotLenhCoThat()
    {
        var unknown = Suites()
            .SelectMany(s => s.Cases)
            .Select(c => c.Command)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => CommandCatalog.Find(CommandCatalog.Revit, name) == null)
            .ToList();

        Assert.True(unknown.Count == 0, "Ca kiểm gọi lệnh không có trong catalog: " + string.Join(", ", unknown));
    }

    [Fact]
    public void MoiLenhRevit_DeuCoItNhatMotCaKiem()
    {
        var covered = new HashSet<string>(
            Suites().SelectMany(s => s.Cases).Select(c => c.Command),
            StringComparer.OrdinalIgnoreCase);

        var missing = CommandCatalog.AllFor(CommandCatalog.Revit)
            .Where(c => c.Implemented && !c.Internal)   // RunTests là chính bộ chạy, không tự kiểm mình.
            .Select(c => c.Name)
            .Where(name => !covered.Contains(name))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"Lệnh chưa có ca kiểm nào chạy trong Revit ({missing.Count}): " + string.Join(", ", missing));
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
