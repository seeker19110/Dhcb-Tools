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

    // ── Trường config trong khối `exec <Lệnh> { … }` ─────────────────────────────

    private static string RepoRoot() => Path.GetDirectoryName(SkillsFolder())!;

    /// <summary>Playbook + trang agent-khep-vong.md (cũng chứa khối exec mẫu mà agent chép theo).</summary>
    private static List<(string File, string Text)> ExecSources()
    {
        var sources = Playbooks();
        var agentDoc = Path.Combine(RepoRoot(), "docs", "agent-khep-vong.md");
        if (File.Exists(agentDoc))
        {
            sources.Add(("docs/agent-khep-vong.md", File.ReadAllText(agentDoc)));
        }
        return sources;
    }

    /// <summary>
    /// Tìm mọi <c>exec TenLenh</c> rồi lấy khối <c>{ … }</c> đi ngay sau (đếm ngoặc, kể cả nhiều dòng và
    /// dạng <c>--config '{…}'</c> của dòng lệnh). Trả (file, lệnh, JSON).
    /// </summary>
    private static IEnumerable<(string File, string Command, string Json)> ExecBlocks()
    {
        var pattern = new Regex(@"\bexec\s+([A-Za-z][A-Za-z0-9]*)", RegexOptions.Compiled);
        foreach (var (file, text) in ExecSources())
        {
            foreach (Match m in pattern.Matches(text))
            {
                var open = text.IndexOf('{', m.Index + m.Length);
                if (open < 0 || open - (m.Index + m.Length) > 80) continue; // không có khối config đi kèm

                var depth = 0;
                var inString = false;
                for (var i = open; i < text.Length; i++)
                {
                    var c = text[i];
                    if (c == '"' && text[i - 1] != '\\') inString = !inString;
                    if (inString) continue;
                    if (c == '{') depth++;
                    else if (c == '}' && --depth == 0)
                    {
                        yield return (file, m.Groups[1].Value, text.Substring(open, i - open + 1));
                        break;
                    }
                }
            }
        }
    }

    /// <summary>Tên lệnh (viết hoa) → tập property của lớp <c>*Config</c> thật, đọc từ mã nguồn Core như <c>CatalogFieldTests</c>.</summary>
    private static Dictionary<string, HashSet<string>> RealConfigFields()
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (project, table) in new[] { ("DhcbTools.Core", "RevitCommandTable.cs"), ("DhcbTools.Core.AutoCAD", "AcadCommandTable.cs") })
        {
            var folder = Path.Combine(RepoRoot(), "src", project);
            if (!Directory.Exists(folder)) continue;

            var classes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
                foreach (Match cls in Regex.Matches(File.ReadAllText(f), @"class\s+(\w*Config)\b[^{]*\{(.*?)\n\}", RegexOptions.Singleline))
                {
                    var props = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Match p in Regex.Matches(cls.Groups[2].Value, @"public\s+(?:required\s+)?[\w<>?\[\],. ]+\s+(\w+)\s*\{\s*get;"))
                    {
                        props.Add(p.Groups[1].Value);
                    }
                    classes[cls.Groups[1].Value] = props;
                }
            }

            var tablePath = Path.Combine(folder, table);
            if (!File.Exists(tablePath)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(tablePath), @"""([A-Z0-9]+)""\s*=>[^\r\n]*?Deserialize<(\w+)>"))
            {
                if (classes.TryGetValue(m.Groups[2].Value, out var props))
                {
                    // Cùng tên lệnh ở hai nền tảng (AutoNumbering Revit/AutoCAD) → gộp, không ghi đè.
                    if (!result.TryGetValue(m.Groups[1].Value, out var existing))
                    {
                        result[m.Groups[1].Value] = existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }
                    existing.UnionWith(props);
                }
            }
        }
        return result;
    }

    [Fact]
    public void KhoiExecTrongPlaybook_CoItNhatMotKhoi_VaLaJsonHopLe()
    {
        var blocks = ExecBlocks().ToList();
        Assert.True(blocks.Count >= 5, "Playbook phải có khối `exec <Lệnh> { … }` để test này có gì mà kiểm; đang có " + blocks.Count + ".");

        var hong = new List<string>();
        foreach (var (file, command, json) in blocks)
        {
            try
            {
                using var _ = System.Text.Json.JsonDocument.Parse(json,
                    new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
            }
            catch (System.Text.Json.JsonException ex)
            {
                hong.Add(file + " → " + command + ": " + ex.Message);
            }
        }
        Assert.True(hong.Count == 0, "Khối config trong playbook không phải JSON hợp lệ (agent sẽ chép nguyên văn): " + string.Join("; ", hong));
    }

    /// <summary>
    /// Mỗi tên trường trong khối <c>exec</c> phải có thật: trong <see cref="CommandCatalog"/> (trường đã khai) hoặc
    /// trong lớp <c>*Config</c> thật của lệnh (catalog chỉ liệt kê trường chính). Một trường gõ sai — như
    /// <c>digits</c> thay vì <c>padWidth</c> — bị Newtonsoft bỏ qua im lặng và lệnh vẫn báo thành công.
    /// </summary>
    [Fact]
    public void MoiTruongTrongKhoiExec_DeuCoTrongCatalogHoacLopConfig()
    {
        var real = RealConfigFields();
        var sai = new List<string>();

        foreach (var (file, command, json) in ExecBlocks())
        {
            var descriptor = CommandCatalog.Find(CommandCatalog.Revit, command) ?? CommandCatalog.Find(CommandCatalog.AutoCad, command);
            if (descriptor == null) continue; // đã có test riêng bắt lệnh không tồn tại

            var allowed = new HashSet<string>(descriptor.ConfigFields.Keys, StringComparer.OrdinalIgnoreCase) { "dryRun" };
            if (real.TryGetValue(descriptor.Name, out var props)) allowed.UnionWith(props);

            System.Text.Json.JsonDocument doc;
            try { doc = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true }); }
            catch (System.Text.Json.JsonException) { continue; } // test trên đã báo

            using (doc)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!allowed.Contains(prop.Name))
                    {
                        sai.Add(file + " → " + descriptor.Name + "." + prop.Name);
                    }
                }
            }
        }

        Assert.True(sai.Count == 0, "Trường config trong playbook không có trong catalog lẫn lớp Config thật: " + string.Join(", ", sai));
    }
}
