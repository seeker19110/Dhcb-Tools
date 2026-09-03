using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DhcbTools.Shared.Logic.Ai;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Đối chiếu <c>docs/mau-phan-hoi-9-4.md</c> với <see cref="CommandCatalog"/> theo **cả hai chiều** — cùng
/// lối đã dùng cho <c>MaLoiTests</c>. Mẫu thu phản hồi mà thiếu một lệnh thì lệnh đó không bao giờ có số
/// liệu, và giai đoạn 10/11 sẽ quyết định trên một bảng khuyết mà không ai biết là khuyết.
/// </summary>
public class PhanHoiFormTests
{
    private static string DocText()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dhcb-Tools.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "docs", "mau-phan-hoi-9-4.md");
        Assert.True(File.Exists(path), $"Không thấy mẫu phản hồi ở {path}");
        return File.ReadAllText(path);
    }

    /// <summary>Tên lệnh trong mẫu nằm trong ô đầu của bảng, viết trong dấu nháy ngược.</summary>
    private static string[] TenLenhTrongMau(string doc, string tieuDeMuc)
    {
        var start = doc.IndexOf(tieuDeMuc, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Không thấy mục '{tieuDeMuc}' trong mẫu phản hồi");
        var end = doc.IndexOf("\n## ", start + tieuDeMuc.Length, StringComparison.Ordinal);
        var block = end < 0 ? doc.Substring(start) : doc.Substring(start, end - start);

        return Regex.Matches(block, @"^\| `([A-Za-z]+)` \|", RegexOptions.Multiline)
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .ToArray();
    }

    [Theory]
    [InlineData(CommandCatalog.Revit, "## Revit —")]
    [InlineData(CommandCatalog.AutoCad, "## AutoCAD —")]
    public void MauPhanHoi_PhuDungCacLenhKySuThayTrongCatalog(string app, string tieuDe)
    {
        var doc = DocText();
        var trongMau = TenLenhTrongMau(doc, tieuDe);
        var trongCatalog = CommandCatalog.For(app).Select(c => c.Name).Distinct().ToArray();

        var thieu = trongCatalog.Except(trongMau).OrderBy(x => x).ToArray();
        Assert.True(thieu.Length == 0, $"Mẫu phản hồi thiếu lệnh: {string.Join(", ", thieu)}");

        var thua = trongMau.Except(trongCatalog).OrderBy(x => x).ToArray();
        Assert.True(thua.Length == 0, $"Mẫu phản hồi có lệnh không còn trong catalog: {string.Join(", ", thua)}");
    }

    /// <summary>
    /// Lệnh nội bộ (<c>RunTests</c>) không được lọt vào mẫu: kỹ sư không có nút đó nên hỏi họ dùng hằng
    /// tuần hay không là câu hỏi vô nghĩa, và một dòng vô nghĩa làm hỏng cả bảng đếm.
    /// </summary>
    [Fact]
    public void LenhNoiBo_KhongCoTrongMau()
    {
        var doc = DocText();
        foreach (var noiBo in CommandCatalog.All.Where(c => c.Internal).Select(c => c.Name).Distinct())
        {
            Assert.DoesNotContain($"| `{noiBo}` |", doc);
        }
    }

    /// <summary>Số lệnh ghi ở tiêu đề mục phải khớp catalog — con số trong tài liệu là thứ trôi nhanh nhất.</summary>
    [Theory]
    [InlineData(CommandCatalog.Revit, "Revit")]
    [InlineData(CommandCatalog.AutoCad, "AutoCAD")]
    public void SoLenhOTieuDe_KhopVoiCatalog(string app, string ten)
    {
        var doc = DocText();
        var so = CommandCatalog.For(app).Select(c => c.Name).Distinct().Count();
        Assert.Contains($"## {ten} — {so} lệnh", doc);
    }
}
