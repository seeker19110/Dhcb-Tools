using System.Text.RegularExpressions;
using DhcbTools.Shared.Logic.Ai;
using Xunit;

namespace DhcbTools.Shared.Logic.Tests;

/// <summary>
/// Đối chiếu <see cref="CommandCatalog"/> với lớp <c>*Config</c> THẬT của từng lệnh.
/// <para>
/// Vì sao cần: từ giai đoạn 9.1 catalog không còn là tài liệu mô tả nữa mà là thứ dựng ra ô nhập của
/// form động, và là <c>inputSchema</c> của MCP. Một tên trường sai nghĩa là kỹ sư điền vào ô không dây
/// vào đâu cả, hoặc agent gửi config bị Newtonsoft bỏ qua — lệnh vẫn báo thành công. Đúng loại lỗi im
/// lặng mà giai đoạn 8.1 đi dọn.
/// </para>
/// <para>
/// Test đọc thẳng mã nguồn Core (cùng cách <c>RibbonCoverageTests</c> đọc vỏ Revit) nên chạy được trên
/// CI Linux, không cần Revit/AutoCAD API.
/// </para>
/// </summary>
public class CatalogFieldTests
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

    private static IEnumerable<string> SourceFiles(string projectFolder) =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", projectFolder), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    /// <summary>Tên lệnh (chữ hoa, như trong bảng dispatch) → tên lớp config nó deserialize vào.</summary>
    private static Dictionary<string, string> CommandToConfigClass(string projectFolder, string tableFile)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", projectFolder, tableFile));
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(source, @"""([A-Z0-9]+)""\s*=>[^\r\n]*?Deserialize<(\w+)>"))
        {
            map[m.Groups[1].Value] = m.Groups[2].Value;
        }

        return map;
    }

    /// <summary>Tên lớp config → tập property của nó.</summary>
    /// <summary>"Lớp.Property" → kiểu khai trong mã nguồn; dùng để đối chiếu danh sách với danh sách.</summary>
    private static readonly Dictionary<string, string> PropertyTypes = new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, HashSet<string>> ConfigClasses(string projectFolder)
    {
        var classes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in SourceFiles(projectFolder))
        {
            var source = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(source, @"class\s+(\w*Config)\b[^{]*\{(.*?)\n\}", RegexOptions.Singleline))
            {
                var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match p in Regex.Matches(m.Groups[2].Value, @"public\s+(?:required\s+)?([\w<>?\[\],. ]+?)\s+(\w+)\s*\{\s*get;"))
                {
                    properties.Add(p.Groups[2].Value);
                    PropertyTypes[m.Groups[1].Value + "." + p.Groups[2].Value] = p.Groups[1].Value.Trim();
                }
                classes[m.Groups[1].Value] = properties;
            }
        }

        return classes;
    }

    private static (Dictionary<string, string> Map, Dictionary<string, HashSet<string>> Classes) Read(
        string platform, string projectFolder, string tableFile)
    {
        var map = CommandToConfigClass(projectFolder, tableFile);
        var classes = ConfigClasses(projectFolder);

        Assert.True(map.Count > 10, $"Không đọc được bảng dispatch {tableFile} — regex hỏng?");
        Assert.True(classes.Count > 5, $"Không đọc được lớp Config nào từ {projectFolder} — regex hỏng?");

        var missing = CommandCatalog.AllFor(platform)
            .Where(c => c.Implemented && !map.ContainsKey(c.Name.ToUpperInvariant()))
            .Select(c => c.Name)
            .ToList();
        Assert.True(missing.Count == 0, "Lệnh có trong catalog nhưng không đọc được config class: " + string.Join(", ", missing));

        return (map, classes);
    }

    private static void AssertFieldsMatchConfig(string platform, string projectFolder, string tableFile)
    {
        var (map, classes) = Read(platform, projectFolder, tableFile);
        var wrong = new List<string>();

        foreach (var command in CommandCatalog.AllFor(platform).Where(c => c.Implemented))
        {
            if (!map.TryGetValue(command.Name.ToUpperInvariant(), out var configClass)
                || !classes.TryGetValue(configClass, out var properties))
            {
                continue;
            }

            foreach (var field in command.Fields.Where(f => !properties.Contains(f.Name)))
            {
                wrong.Add($"{command.Name}.{field.Name} (không có trong {configClass})");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "Trường trong catalog không có property Config tương ứng — form động dựng ô chết, MCP chào "
                + "trường mà lệnh bỏ qua: " + string.Join("; ", wrong));
    }

    /// <summary>
    /// Lệnh có sửa mô hình thì config PHẢI có <c>DryRun</c>. Không có thì hai lớp khoá của bộ kiểm thử
    /// trong Revit (<c>RunTestsCommand</c> ép <c>dryRun=true</c>) vô hiệu một cách im lặng: JSON có
    /// trường đó nhưng Newtonsoft không tìm thấy property nào để gán, và lệnh ghi thẳng vào model mẫu.
    /// </summary>
    private static void AssertWriteCommandsHaveDryRun(string platform, string projectFolder, string tableFile)
    {
        var (map, classes) = Read(platform, projectFolder, tableFile);

        var missing = CommandCatalog.AllFor(platform)
            .Where(c => c.Implemented && c.WritesModel)
            .Where(c => map.TryGetValue(c.Name.ToUpperInvariant(), out var config)
                        && classes.TryGetValue(config, out var properties)
                        && !properties.Contains("DryRun"))
            .Select(c => c.Name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Lệnh sửa mô hình nhưng config không có DryRun — không xem trước được, và bộ test ép "
                + "dryRun không có tác dụng: " + string.Join(", ", missing));
    }

    [Fact]
    public void TruongCatalogRevit_DeuCoPropertyConfigThat() =>
        AssertFieldsMatchConfig(CommandCatalog.Revit, "DhcbTools.Core", "RevitCommandTable.cs");

    [Fact]
    public void TruongCatalogAutoCad_DeuCoPropertyConfigThat() =>
        AssertFieldsMatchConfig(CommandCatalog.AutoCad, "DhcbTools.Core.AutoCAD", "AcadCommandTable.cs");

    [Fact]
    public void LenhGhiCuaRevit_DeuCoDryRun() =>
        AssertWriteCommandsHaveDryRun(CommandCatalog.Revit, "DhcbTools.Core", "RevitCommandTable.cs");

    [Fact]
    public void LenhGhiCuaAutoCad_DeuCoDryRun() =>
        AssertWriteCommandsHaveDryRun(CommandCatalog.AutoCad, "DhcbTools.Core.AutoCAD", "AcadCommandTable.cs");

    /// <summary>
    /// Trường nào là <b>danh sách</b> trong lớp Config thì <see cref="FieldSpec.IsList"/> cũng phải là
    /// danh sách — và ngược lại.
    /// <para>
    /// Vì sao: form động ghi giá trị theo <c>IsList</c>. Lệch một chỗ là form gửi chuỗi "A; B" vào một
    /// property <c>List&lt;string&gt;</c>, Newtonsoft ném ngay và lệnh <b>không chạy được từ Ribbon</b> —
    /// trong khi bộ ca kiểm (gửi JSON đúng kiểu) vẫn xanh. Đúng cái bắt được khi bấm tay ngày 2026-09-05:
    /// <c>parameterNames</c> chứa chữ "parameter" nên bị đoán là MỘT tham số, mà config là danh sách
    /// (xem docs/bang-chung-test.md §34).
    /// </para>
    /// </summary>
    private static void AssertListFieldsMatchConfig(string platform, string projectFolder, string tableFile)
    {
        var (map, classes) = Read(platform, projectFolder, tableFile);
        var wrong = new List<string>();

        foreach (var command in CommandCatalog.AllFor(platform).Where(c => c.Implemented))
        {
            if (!map.TryGetValue(command.Name.ToUpperInvariant(), out var configClass)
                || !classes.TryGetValue(configClass, out var properties))
            {
                continue;
            }

            foreach (var field in command.Fields.Where(f => properties.Contains(f.Name)))
            {
                if (!PropertyTypes.TryGetValue(configClass + "." + field.Name, out var type))
                {
                    continue;
                }

                // List<LevelDefinition>, List<GridDefinition>… nhận JSON thô: form hiện một ô text để
                // dán nguyên mảng object, và giá trị đi thẳng qua chứ không ghép/tách bằng dấu ";".
                // Chỉ danh sách CHUỖI mới đi qua đường ghép/tách của IsList.
                if (type.Contains("List<", StringComparison.Ordinal)
                    && !type.Contains("List<string>", StringComparison.Ordinal))
                {
                    continue;
                }

                var configIsList = type.Contains("List<", StringComparison.Ordinal)
                                   || type.Contains("[]", StringComparison.Ordinal)
                                   || type.Contains("IEnumerable<", StringComparison.Ordinal);

                if (configIsList != field.IsList)
                {
                    wrong.Add($"{command.Name}.{field.Name}: config là {type}, catalog đoán {field.Kind}"
                              + (configIsList ? " (không phải danh sách)" : " (danh sách)"));
                }
            }
        }

        Assert.True(
            wrong.Count == 0,
            "Kiểu danh sách của catalog lệch với lớp Config — form động gửi sai kiểu và lệnh không chạy "
                + "được từ Ribbon: " + string.Join("; ", wrong));
    }

    /// <summary>
    /// Property có kiểu <b>đối tượng riêng</b> (PointMm, RouteSizeMm, Dictionary…) thì catalog phải để
    /// <see cref="FieldKind.Json"/> — form dựng ô JSON thô để người dùng dán nguyên cấu
    /// trúc. Đoán thành Number/Bool/danh sách là ô nhập KHÔNG THỂ nhập đúng giá trị: bắt được khi bấm
    /// tay 2026-09-05, <c>AutoRoute.startMm</c> là điểm {x,y,z} mà hậu tố "Mm" làm luật số đoán nhầm nên
    /// form đòi một con số (§34).
    /// </summary>
    private static void AssertObjectFieldsAreRawJson(string platform, string projectFolder, string tableFile)
    {
        var (map, classes) = Read(platform, projectFolder, tableFile);
        var simple = new[] { "string", "int", "long", "double", "bool", "decimal", "float" };
        var wrong = new List<string>();

        foreach (var command in CommandCatalog.AllFor(platform).Where(c => c.Implemented))
        {
            if (!map.TryGetValue(command.Name.ToUpperInvariant(), out var configClass)
                || !classes.TryGetValue(configClass, out var properties))
            {
                continue;
            }

            foreach (var field in command.Fields.Where(f => properties.Contains(f.Name)))
            {
                if (!PropertyTypes.TryGetValue(configClass + "." + field.Name, out var type))
                {
                    continue;
                }

                var bare = type.Replace("?", string.Empty).Trim();

                // Danh sách để test danh sách lo (List<enum>, List<long>… gửi mảng chuỗi là Newtonsoft
                // đổi kiểu được). Ở đây chỉ soi property là MỘT đối tượng riêng: ô nhập phải là JSON thô.
                var isOneObject = !bare.StartsWith("List<", StringComparison.Ordinal)
                                  && !bare.EndsWith("[]", StringComparison.Ordinal)
                                  && !simple.Contains(bare, StringComparer.Ordinal);

                if (isOneObject && field.Kind != FieldKind.Json)
                {
                    wrong.Add($"{command.Name}.{field.Name}: config là {type} nhưng catalog đoán {field.Kind}");
                }
            }
        }

        Assert.True(
            wrong.Count == 0,
            "Trường nhận đối tượng riêng phải để FieldKind.Json (ô JSON thô), nếu không form dựng ô "
                + "không nhập nổi giá trị: " + string.Join("; ", wrong));
    }

    [Fact]
    public void TruongDoiTuongCuaRevit_LaOJsonTho() =>
        AssertObjectFieldsAreRawJson(CommandCatalog.Revit, "DhcbTools.Core", "RevitCommandTable.cs");

    [Fact]
    public void TruongDoiTuongCuaAutoCad_LaOJsonTho() =>
        AssertObjectFieldsAreRawJson(CommandCatalog.AutoCad, "DhcbTools.Core.AutoCAD", "AcadCommandTable.cs");

    [Fact]
    public void TruongDanhSachCuaRevit_KhopVoiConfigThat() =>
        AssertListFieldsMatchConfig(CommandCatalog.Revit, "DhcbTools.Core", "RevitCommandTable.cs");

    [Fact]
    public void TruongDanhSachCuaAutoCad_KhopVoiConfigThat() =>
        AssertListFieldsMatchConfig(CommandCatalog.AutoCad, "DhcbTools.Core.AutoCAD", "AcadCommandTable.cs");
}
