using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Giai đoạn 9.3 — thông báo của Core phải bằng tiếng Việt.
/// <para>
/// Không đo được "có phải tiếng Việt không" một cách tổng quát, nên test chốt theo danh sách các mẫu
/// tiếng Anh đã từng lọt ra tận báo cáo kiểm thử chạy thật: <c>[Dry Run] Would create 2 level(s).</c>,
/// <c>[Skip]</c>, <c>Family folder not found</c>, <c>Error: </c>… Mỗi mẫu ở đây là một thứ đã thấy tận
/// mắt trên máy có Revit, không phải suy đoán.
/// </para>
/// </summary>
public class VietnameseMessageTests
{
    private static readonly string[] EnglishMarkers =
    {
        "[Dry Run]",
        "[Skip]",
        "[Create]",
        "[Warn]",
        "[Error]",
        "[DryRun]",
        "Would create",
        "Would load",
        "not found:",
        "Error: \" +",
        "Error setting",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dhcb-Tools.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
    }

    [Theory]
    [InlineData("DhcbTools.Core")]
    [InlineData("DhcbTools.Core.AutoCAD")]
    public void ThongBaoCuaCore_KhongConMauTiengAnh(string projectFolder)
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "src", projectFolder), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

        var hits = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;   // Chú thích tiếng Anh không phải thông báo cho kỹ sư.
                }

                foreach (var marker in EnglishMarkers.Where(m => line.Contains(m, StringComparison.Ordinal)))
                {
                    hits.Add($"{Path.GetFileName(file)}:{i + 1} chứa \"{marker}\"");
                }
            }
        }

        Assert.True(hits.Count == 0, "Thông báo còn tiếng Anh: " + string.Join("; ", hits));
    }
}
