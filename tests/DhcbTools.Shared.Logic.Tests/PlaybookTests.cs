using System.Text.RegularExpressions;
using DhcbTools.Shared.Logic.Ai;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Giai đoạn 10.3 — playbook trong <c>skills/</c> phải chỉ tới lệnh và truy vấn <b>có thật</b>.
/// <para>
/// Playbook là thứ agent đọc rồi làm theo. Một cái tên lệnh gõ sai trong đó không gây lỗi biên dịch,
/// không ai thấy, cho tới lúc agent gọi và nhận "Lệnh không xác định" giữa phiên làm việc của kỹ sư —
/// tệ hơn là không có playbook, vì nó trông như đã được kiểm.
/// </para>
/// </summary>
public class PlaybookTests
{
    /// <summary>Các truy vấn hợp lệ, theo <c>QueryRequest</c> (Core) và <c>UiQueryHandler</c> (vỏ Revit).</summary>
    private static readonly HashSet<string> QueryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "document_info", "elements", "levels", "views", "sheets", "rooms", "families", "warnings",
        "links", "stats", "element_geometry", "schedule_rows", "parameters_of", "snapshot",
        "selection", "show_elements", "active_view",
    };

    private static string SkillsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dhcb-Tools.sln")))
        {
            dir = dir.Parent;
        }
        var root = dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
        return Path.Combine(root, "skills");
    }

    private static List<(string File, string Text)> Playbooks() =>
        Directory.EnumerateFiles(SkillsFolder(), "SKILL.md", SearchOption.AllDirectories)
            .Select(f => (File: Path.GetFileName(Path.GetDirectoryName(f))!, Text: File.ReadAllText(f)))
            .ToList();

    [Fact]
    public void CoDuPlaybook_VaMoiCaiCoTenVaMoTa()
    {
        var books = Playbooks();
        Assert.True(books.Count >= 5, "Giai đoạn 10.3 cần ít nhất 5 playbook, đang có " + books.Count + ".");

        foreach (var (folder, text) in books)
        {
            Assert.True(text.StartsWith("---", StringComparison.Ordinal), folder + ": thiếu frontmatter.");
            Assert.Contains("name: " + folder, text);       // tên trong frontmatter phải khớp tên thư mục
            Assert.Contains("description:", text);
        }
    }

    [Fact]
    public void MoiLenhNhacTrongPlaybook_DeuCoThat()
    {
        // "exec TenLenh {" — đúng cách playbook viết một bước gọi lệnh.
        var pattern = new Regex(@"\bexec\s+([A-Za-z][A-Za-z0-9]*)", RegexOptions.Compiled);
        var sai = new List<string>();

        foreach (var (folder, text) in Playbooks())
        {
            foreach (Match m in pattern.Matches(text))
            {
                var name = m.Groups[1].Value;
                if (CommandCatalog.Find(CommandCatalog.Revit, name) == null
                    && CommandCatalog.Find(CommandCatalog.AutoCad, name) == null)
                {
                    sai.Add(folder + " → " + name);
                }
            }
        }

        Assert.True(sai.Count == 0, "Playbook gọi lệnh không có trong catalog: " + string.Join(", ", sai));
    }

    [Fact]
    public void MoiTruyVanNhacTrongPlaybook_DeuCoThat()
    {
        var pattern = new Regex(@"\bquery\s+([a-z][a-z_]*)", RegexOptions.Compiled);
        var sai = new List<string>();

        foreach (var (folder, text) in Playbooks())
        {
            foreach (Match m in pattern.Matches(text))
            {
                var name = m.Groups[1].Value;
                if (!QueryNames.Contains(name))
                {
                    sai.Add(folder + " → " + name);
                }
            }
        }

        Assert.True(sai.Count == 0, "Playbook gọi truy vấn không có thật: " + string.Join(", ", sai));
    }

    /// <summary>
    /// Mỗi playbook phải có mục "Không được làm". Đó là phần khác biệt giữa một trang hướng dẫn và một
    /// hàng rào: agent cần biết ranh giới, không chỉ trình tự.
    /// </summary>
    [Fact]
    public void MoiPlaybook_CoMucKhongDuocLam()
    {
        var thieu = Playbooks().Where(b => !b.Text.Contains("Không được làm", StringComparison.Ordinal))
            .Select(b => b.File).ToList();

        Assert.True(thieu.Count == 0, "Playbook thiếu mục \"Không được làm\": " + string.Join(", ", thieu));
    }
}
