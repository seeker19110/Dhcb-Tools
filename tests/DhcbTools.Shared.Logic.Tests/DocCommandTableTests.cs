using Xunit;
using System.Text.RegularExpressions;
using DhcbTools.Shared.Logic.Ai;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Giữ bảng lệnh trong tài liệu không trôi khỏi <see cref="CommandCatalog"/>. Cùng khuôn với
/// <c>RibbonCoverageTests</c>/<c>SuiteCoverageTests</c>: đối chiếu mã nguồn với mã nguồn thay vì
/// tin vào việc đọc lại bằng mắt.
///
/// Vì sao cần: README và docs/progress.md viết tay, và đã từng trôi thật — dòng "Phần AutoCAD
/// chưa có mã" trong progress.md sống sót nhiều tháng trong khi bốn lệnh (LayerTranslate,
/// DrawingCompare, BlockQuantity, AttributeIncrement) đã có lớp *Command và đã dây vào
/// AcadCommandTable; chỉ phát hiện khi đối chiếu tay ngày 2026-09-06 (§47).
/// </summary>
public class DocCommandTableTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dhcb-Tools.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
    }

    private static string Doc(string name) => File.ReadAllText(Path.Combine(RepoRoot(), name));

    /// <summary>Phần "## Lệnh" của README — chỉ bảng lệnh, không lấy nhầm phần cấu trúc solution.</summary>
    private static string ReadmeCommandSection()
    {
        var text = Doc("README.md");
        var start = text.IndexOf("\n## Lệnh", StringComparison.Ordinal);
        Assert.True(start >= 0, "README.md không còn mục '## Lệnh' — đổi tiêu đề thì sửa test này theo.");
        var end = text.IndexOf("\n## ", start + 8, StringComparison.Ordinal);
        return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
    }

    /// <summary>
    /// Tên trong dấu backtick trông giống tên lệnh Core: PascalCase, không gạch dưới.
    /// Loại DHCB_* (tên lệnh dòng lệnh AutoCAD, khái niệm khác) và các từ khoá cấu hình viết thường.
    /// </summary>
    private static HashSet<string> CommandLikeTokens(string markdown)
    {
        var tokens = Regex.Matches(markdown, "`([A-Za-z][A-Za-z0-9]*)`")
            .Select(m => m.Groups[1].Value)
            .Where(t => char.IsUpper(t[0]))
            .Where(t => t.Any(char.IsLower));   // bỏ "PDF", "DWG", "IFC", "NWC", "CSV", "HTML"…
        return new HashSet<string>(tokens, StringComparer.Ordinal);
    }

    /// <summary>Từ trong backtick ở mục "## Lệnh" nhưng cố ý không phải tên lệnh.</summary>
    private static readonly HashSet<string> NotACommand = new(StringComparer.Ordinal)
    {
        "CommandCatalog",   // trỏ tới chính lớp danh mục
        "CommandResult",    // kiểu trả về chung của mọi lệnh
        "Document",         // chữ ký lệnh
        "Database",         // chữ ký lệnh
    };

    private static IEnumerable<CommandDescriptor> PublicCommands() =>
        CommandCatalog.AllFor(CommandCatalog.Revit)
            .Concat(CommandCatalog.AllFor(CommandCatalog.AutoCad))
            .Where(c => !c.Internal);   // RunTests là công cụ nội bộ, cố ý không lên bảng tài liệu.

    /// <summary>Lệnh mới thêm vào catalog phải có mặt trong bảng lệnh README, không im lặng lọt.</summary>
    [Fact]
    public void MoiLenhTrongCatalog_DeuCoTrongBangLenhReadme()
    {
        var section = ReadmeCommandSection();

        var missing = PublicCommands()
            .Select(c => c.Name)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !section.Contains("`" + name + "`", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Lệnh có trong CommandCatalog nhưng thiếu ở bảng '## Lệnh' của README.md: "
                + string.Join(", ", missing));
    }

    /// <summary>
    /// Chiều ngược lại — bắt tên lệnh đã đổi/xoá mà tài liệu còn giữ. Đây là chiều mà việc đọc
    /// bằng mắt hay bỏ sót nhất: thêm lệnh thì người viết nhớ, bỏ lệnh thì không.
    /// </summary>
    [Fact]
    public void MoiTenLenhTrongReadme_DeuCoThatTrongCatalog()
    {
        var known = new HashSet<string>(PublicCommands().Select(c => c.Name), StringComparer.Ordinal);

        var unknown = CommandLikeTokens(ReadmeCommandSection())
            .Where(t => !known.Contains(t))
            .Where(t => !NotACommand.Contains(t))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unknown.Count == 0,
            "README.md nhắc tên lệnh không có trong CommandCatalog (đã đổi tên hay đã bỏ?): "
                + string.Join(", ", unknown)
                + ". Nếu đó không phải tên lệnh, thêm vào NotACommand kèm lý do.");
    }

    /// <summary>
    /// docs/progress.md là nơi trả lời "cái gì đã có mã nguồn" — nếu nó bỏ sót một lệnh thì
    /// người đọc kết luận sai đúng theo kiểu §47.
    /// </summary>
    [Fact]
    public void MoiLenhTrongCatalog_DeuDuocNhacTrongProgress()
    {
        var progress = Doc(Path.Combine("docs", "progress.md"));

        var missing = PublicCommands()
            .Select(c => c.Name)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !progress.Contains(name, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Lệnh có mã nguồn nhưng docs/progress.md không nhắc tới: " + string.Join(", ", missing));
    }
}
