using System.Text.RegularExpressions;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Giai đoạn 9.3 — bảng mã lỗi (<c>docs/ma-loi.md</c>) phải khớp mã nguồn theo <b>cả hai chiều</b>.
/// <para>
/// Một trang tài liệu liệt kê mã lỗi chỉ có giá trị khi nó đầy đủ. Không có test này thì mã thứ năm
/// được thêm vào Core sẽ không bao giờ có mặt trong tài liệu, và người tra cứu tưởng mình gặp lỗi lạ.
/// Chiều ngược lại cũng cần: mã bị xoá khỏi mã nguồn mà vẫn nằm trong bảng là chỉ dẫn sai.
/// </para>
/// </summary>
public class MaLoiTests
{
    private static readonly Regex MaLoi = new(@"E-[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dhcb-Tools.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
    }

    private static HashSet<string> TrongMaNguon()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in new[] { "DhcbTools.Core", "DhcbTools.Core.AutoCAD", "DhcbTools.Shared.Logic", "DhcbTools.Shared.Hosting" })
        {
            var folder = Path.Combine(RepoRoot(), "src", project);
            if (!Directory.Exists(folder)) continue;

            foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                foreach (Match m in MaLoi.Matches(File.ReadAllText(file)))
                {
                    codes.Add(m.Value);
                }
            }
        }
        return codes;
    }

    private static HashSet<string> TrongTaiLieu()
    {
        var doc = Path.Combine(RepoRoot(), "docs", "ma-loi.md");
        Assert.True(File.Exists(doc), "Thiếu trang bảng mã lỗi docs/ma-loi.md.");
        return new HashSet<string>(MaLoi.Matches(File.ReadAllText(doc)).Select(m => m.Value), StringComparer.Ordinal);
    }

    [Fact]
    public void MoiMaLoiTrongMaNguon_DeuCoTrongBang()
    {
        var thieu = TrongMaNguon().Except(TrongTaiLieu()).OrderBy(c => c).ToList();
        Assert.True(thieu.Count == 0, "Mã lỗi có trong mã nguồn nhưng thiếu ở docs/ma-loi.md: " + string.Join(", ", thieu));
    }

    [Fact]
    public void MoiMaTrongBang_DeuConTrongMaNguon()
    {
        var thua = TrongTaiLieu().Except(TrongMaNguon()).OrderBy(c => c).ToList();
        Assert.True(thua.Count == 0, "Mã lỗi còn trong docs/ma-loi.md nhưng đã biến mất khỏi mã nguồn: " + string.Join(", ", thua));
    }

    [Fact]
    public void BangMaLoi_KhongRong()
    {
        Assert.NotEmpty(TrongMaNguon());
    }
}
