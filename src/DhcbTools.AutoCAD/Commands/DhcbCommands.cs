using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DhcbTools.Core.AutoCAD;
using DhcbTools.Core.AutoCAD.AutoNumbering;
using DhcbTools.Core.AutoCAD.DrawingCleanup;
using DhcbTools.Core.AutoCAD.LayerSync;
using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.Batch;
using Newtonsoft.Json.Linq;

namespace DhcbTools.AutoCAD.Commands;

/// <summary>
/// Vỏ mỏng gọi vào Core. Lệnh tương tác hỏi tham số trên dòng lệnh; lệnh nâng cao đọc config JSON ở
/// <c>%APPDATA%\DHCB\configs\autocad\&lt;CommandName&gt;.json</c> (DHCB_CFG tạo file mẫu). DHCB_RUN dùng cho batch
/// (accoreconsole, mục 1). DHCB_AI dịch câu tiếng Việt sang lệnh — hiển thị đề xuất, không tự chạy (mục 5.4).
/// </summary>
public sealed class DhcbCommands
{
    [CommandMethod("DHCB", CommandFlags.Modal)]
    public void Help()
    {
        var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
        if (ed is null) return;
        ed.WriteMessage("\n[DHCB Tools] Lệnh:\n");
        ed.WriteMessage("  DHCB_LAYER_EXPORT / DHCB_LAYER_IMPORT / DHCB_CLEANUP / DHCB_AUTONUMBER\n");
        ed.WriteMessage("  DHCB_ATTR_EXPORT / DHCB_ATTR_IMPORT / DHCB_TEXT_REPLACE / DHCB_XREF_AUDIT\n");
        ed.WriteMessage("  DHCB_GRID_EXTRACT (trục AXIS → CSV cho Revit) / DHCB_LAYER_CHECK / DHCB_LAYERMAP (AI offline)\n");
        ed.WriteMessage("  DHCB_EXEC <Lệnh> — chạy lệnh Core bất kỳ với config JSON trong %APPDATA%\\DHCB\\configs\\autocad\\\n");
        ed.WriteMessage("  DHCB_CFG <Lệnh> — tạo file config mẫu · DHCB_AI — ra lệnh bằng tiếng Việt · DHCB_RUN — batch\n");
        foreach (var c in CommandCatalog.For(CommandCatalog.AutoCad))
        {
            ed.WriteMessage($"    {c.Name,-20} {c.Description}\n");
        }
    }

    [CommandMethod("DHCB_LAYER_EXPORT", CommandFlags.Modal)]
    public void LayerExport()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;

        var output = AskString(ed, "Đường dẫn file CSV xuất ra", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dhcb_layers.csv"));
        if (output is null) return;

        PrintResult(ed, new LayerExportCommand().Execute(doc.Database, new LayerExportConfig { OutputPath = output }));
    }

    [CommandMethod("DHCB_LAYER_IMPORT", CommandFlags.Modal)]
    public void LayerImport()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;

        var input = AskString(ed, "Đường dẫn file CSV đầu vào", null);
        if (string.IsNullOrWhiteSpace(input)) return;

        var config = new LayerImportConfig { InputPath = input!, DryRun = AskDryRun(ed), CreateMissing = true };
        PrintResult(ed, new LayerImportCommand().Execute(doc.Database, config));
    }

    [CommandMethod("DHCB_CLEANUP", CommandFlags.Modal)]
    public void DrawingCleanup()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;

        var config = new CleanupConfig { DryRun = AskDryRun(ed), RemoveEmptyLayers = true, PurgeUnusedBlocks = true, PurgeUnusedLinetypes = true };
        PrintResult(ed, new DrawingCleanupCommand().Execute(doc.Database, config));
    }

    [CommandMethod("DHCB_AUTONUMBER", CommandFlags.Modal)]
    public void AutoNumber()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;

        var block = AskString(ed, "Tên Block cần đánh số", null);
        if (string.IsNullOrWhiteSpace(block)) return;
        var attr = AskString(ed, "Tên Attribute Tag", "MARK") ?? "MARK";
        var prefix = AskString(ed, "Tiền tố", string.Empty) ?? string.Empty;

        var config = new AutoNumberingConfig { BlockName = block!, AttributeTag = attr, Prefix = prefix, DryRun = AskDryRun(ed) };
        PrintResult(ed, new AutoNumberingCommand().Execute(doc.Database, config));
    }

    [CommandMethod("DHCB_ATTR_EXPORT", CommandFlags.Modal)]
    public void AttributeExport()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var block = AskString(ed, "Tên Block (Enter = mọi block có attribute)", string.Empty);
        var output = AskString(ed, "File CSV xuất ra", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dhcb_attributes.csv"));
        if (output is null) return;
        Run(doc, "AttributeExport", new JObject { ["blockName"] = string.IsNullOrWhiteSpace(block) ? null : block, ["outputPath"] = output });
    }

    [CommandMethod("DHCB_ATTR_IMPORT", CommandFlags.Modal)]
    public void AttributeImport()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var input = AskString(ed, "File CSV đầu vào", null);
        if (string.IsNullOrWhiteSpace(input)) return;
        Run(doc, "AttributeImport", new JObject { ["inputPath"] = input, ["dryRun"] = AskDryRun(ed) });
    }

    [CommandMethod("DHCB_TEXT_REPLACE", CommandFlags.Modal)]
    public void TextReplace()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var find = AskString(ed, "Chuỗi cần tìm", null);
        if (string.IsNullOrEmpty(find)) return;
        var replace = AskString(ed, "Thay bằng", string.Empty) ?? string.Empty;
        var regex = AskKeyword(ed, "Dùng regex", new[] { "Không", "Có" }, "Không") == "Có";
        Run(doc, "TextReplace", new JObject { ["find"] = find, ["replace"] = replace, ["useRegex"] = regex, ["dryRun"] = AskDryRun(ed) });
    }

    [CommandMethod("DHCB_XREF_AUDIT", CommandFlags.Modal)]
    public void XrefAudit()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        Run(doc, "XrefAudit", new JObject());
    }

    [CommandMethod("DHCB_GRID_EXTRACT", CommandFlags.Modal)]
    public void GridExtract()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var layer = AskString(ed, "Layer trục", "AXIS") ?? "AXIS";
        var output = AskString(ed, "File CSV xuất ra (nhập vào Revit bằng GridFromCsv)", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dhcb_grids.csv"));
        if (output is null) return;
        var unit = AskKeyword(ed, "Đơn vị bản vẽ", new[] { "mm", "m" }, "mm");
        Run(doc, "GridExtract", new JObject { ["gridLayer"] = layer, ["outputPath"] = output, ["unitToMm"] = unit == "m" ? 1000 : 1 });
    }

    [CommandMethod("DHCB_LAYER_CHECK", CommandFlags.Modal)]
    public void LayerCheck()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var rules = AskString(ed, "File JSON quy tắc", Path.Combine(ConfigStore.Directory, "layer-rules.json"));
        if (string.IsNullOrWhiteSpace(rules)) return;
        var output = AskString(ed, "File HTML báo cáo", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DHCB_LayerCheck.html"));
        if (output is null) return;
        Run(doc, "LayerStandardCheck", new JObject { ["rulesPath"] = rules, ["outputPath"] = output });
    }

    [CommandMethod("DHCB_LAYERMAP", CommandFlags.Modal)]
    public void LayerMap()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var types = AskString(ed, "File .txt danh sách Revit type (mỗi dòng \"Family: Type\")", null);
        if (string.IsNullOrWhiteSpace(types)) return;
        var output = AskString(ed, "File CSV mapping", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dhcb_layer_map.csv"));
        if (output is null) return;
        var ollama = AskKeyword(ed, "Dùng model local (Ollama)", new[] { "Không", "Có" }, "Không") == "Có";
        Run(doc, "CadLayerMap", new JObject { ["revitTypesPath"] = types, ["outputPath"] = output, ["useOllama"] = ollama });
    }

    /// <summary>Chạy lệnh Core bất kỳ với config JSON lưu sẵn.</summary>
    [CommandMethod("DHCB_EXEC", CommandFlags.Modal)]
    public void Exec()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var name = AskString(ed, "Tên lệnh Core (" + string.Join("/", CommandCatalog.Names(CommandCatalog.AutoCad)) + ")", null);
        if (string.IsNullOrWhiteSpace(name)) return;

        var descriptor = CommandCatalog.Find(CommandCatalog.AutoCad, name!);
        if (descriptor is null)
        {
            ed.WriteMessage($"\n✗ Không có lệnh \"{name}\".\n");
            return;
        }

        var configJson = ConfigStore.Load(descriptor.Name, out var path);
        if (configJson is null)
        {
            ed.WriteMessage($"\n✗ Chưa có config: {path}. Gõ DHCB_CFG {descriptor.Name} để tạo file mẫu.\n");
            return;
        }

        using var lock_ = doc.LockDocument();
        PrintResult(ed, AcadCommandTable.Dispatch(doc.Database, descriptor.Name, configJson));
    }

    [CommandMethod("DHCB_CFG", CommandFlags.Modal)]
    public void CreateConfig()
    {
        var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
        if (ed is null) return;
        var name = AskString(ed, "Tên lệnh Core", null);
        if (string.IsNullOrWhiteSpace(name)) return;
        var descriptor = CommandCatalog.Find(CommandCatalog.AutoCad, name!);
        if (descriptor is null)
        {
            ed.WriteMessage($"\n✗ Không có lệnh \"{name}\".\n");
            return;
        }

        var path = ConfigStore.WriteTemplate(descriptor);
        ed.WriteMessage($"\n✓ Đã tạo {path} — sửa rồi chạy DHCB_EXEC {descriptor.Name}.\n");
    }

    /// <summary>Mục 5.4 — ra lệnh bằng tiếng Việt: hiện đề xuất, xác nhận rồi chạy xem trước, xác nhận lần hai chạy thật.</summary>
    [CommandMethod("DHCB_AI", CommandFlags.Modal)]
    public void Ai()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var text = AskString(ed, "Bạn muốn làm gì? (tiếng Việt)", null);
        if (string.IsNullOrWhiteSpace(text)) return;

        var intent = CommandIntentParser.Parse(text!, CommandCatalog.AutoCad);
        if (intent.Command is null)
        {
            ed.WriteMessage("\n✗ " + intent.Explanation + "\n");
            return;
        }

        ed.WriteMessage($"\n→ {intent.Explanation}\n  Độ tin cậy: {intent.Confidence:F2}\n  Config: {intent.Config.ToString(Newtonsoft.Json.Formatting.None)}\n");
        if (intent.Alternatives.Count > 0)
        {
            ed.WriteMessage("  Lệnh khác có thể: " + string.Join(", ", intent.Alternatives) + "\n");
        }

        if (AskKeyword(ed, "Chạy xem trước", new[] { "Có", "Không" }, "Có") != "Có") return;

        using var lock_ = doc.LockDocument();
        var preview = AcadCommandTable.Dispatch(doc.Database, intent.Command, intent.Config.ToString(Newtonsoft.Json.Formatting.None));
        PrintResult(ed, preview);

        var descriptor = CommandCatalog.Find(CommandCatalog.AutoCad, intent.Command);
        if (preview.Success && descriptor?.WritesModel == true && AskKeyword(ed, "Chạy THẬT với config trên", new[] { "Không", "Có" }, "Không") == "Có")
        {
            intent.Config["dryRun"] = false;
            PrintResult(ed, AcadCommandTable.Dispatch(doc.Database, intent.Command, intent.Config.ToString(Newtonsoft.Json.Formatting.None)));
        }
    }

    /// <summary>
    /// Batch (mục 1, accoreconsole): <c>DHCB_RUN "step.json" "run.jsonl" "source.dwg"</c>. Đọc step JSON {command, config},
    /// chạy, ghi một dòng vào run.jsonl. Không hỏi gì — chạy được không người trực.
    /// </summary>
    [CommandMethod("DHCB_RUN", CommandFlags.Modal)]
    public void RunStep()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;

        var stepPath = AskString(ed, "File step JSON", null);
        var logPath = AskString(ed, "File run.jsonl", null);
        var source = AskString(ed, "File nguồn", doc.Name) ?? doc.Name;
        if (string.IsNullOrWhiteSpace(stepPath) || string.IsNullOrWhiteSpace(logPath)) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var entry = new RunLogEntry { File = source };
        try
        {
            var step = JObject.Parse(File.ReadAllText(stepPath!));
            entry.Command = step["command"]?.ToString() ?? string.Empty;
            using var lock_ = doc.LockDocument();
            var result = AcadCommandTable.Dispatch(doc.Database, entry.Command, step["config"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "{}");
            entry.Success = result.Success;
            entry.Affected = result.AffectedCount;
            entry.Summary = result.Summary;
            entry.Messages = result.Messages.Take(2000).ToList();
            entry.Errors = result.Errors;
            PrintResult(ed, result);
        }
        catch (System.Exception ex)
        {
            entry.Success = false;
            entry.Summary = "Exception: " + ex.Message;
            ed.WriteMessage("\n✗ " + ex.Message + "\n");
        }
        finally
        {
            entry.ElapsedMs = sw.ElapsedMilliseconds;
            RunLog.Append(logPath!, entry);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void Run(Document doc, string command, JObject config)
    {
        using var lock_ = doc.LockDocument();
        PrintResult(doc.Editor, AcadCommandTable.Dispatch(doc.Database, command, config.ToString(Newtonsoft.Json.Formatting.None)));
    }

    private static string? AskString(Editor ed, string prompt, string? defaultValue)
    {
        var opt = new PromptStringOptions($"\n{prompt}" + (defaultValue is null ? ": " : $" [Enter = {defaultValue}]: ")) { AllowSpaces = true };
        if (defaultValue is not null) opt.DefaultValue = defaultValue;
        var r = ed.GetString(opt);
        if (r.Status != PromptStatus.OK) return null;
        return string.IsNullOrWhiteSpace(r.StringResult) ? defaultValue : r.StringResult;
    }

    private static string AskKeyword(Editor ed, string prompt, string[] keywords, string defaultKeyword)
    {
        var opt = new PromptKeywordOptions($"\n{prompt} [{string.Join("/", keywords)}] <{defaultKeyword}>: ") { AllowNone = true };
        foreach (var k in keywords) opt.Keywords.Add(k);
        var r = ed.GetKeywords(opt);
        return r.Status == PromptStatus.OK && !string.IsNullOrEmpty(r.StringResult) ? r.StringResult : defaultKeyword;
    }

    private static bool AskDryRun(Editor ed) => AskKeyword(ed, "Chế độ", new[] { "Xemtrước", "Thật" }, "Xemtrước") != "Thật";

    private static void PrintResult(Editor ed, CommandResult result)
    {
        ed.WriteMessage($"\n{(result.Success ? "✓" : "✗")} {result.Summary}\n");
        foreach (var msg in result.Messages.Take(300)) ed.WriteMessage($"  • {msg}\n");
        if (result.Messages.Count > 300) ed.WriteMessage($"  … còn {result.Messages.Count - 300} dòng.\n");
        foreach (var err in result.Errors) ed.WriteMessage($"  ! {err}\n");
    }
}

/// <summary>Config JSON theo tên lệnh ở %APPDATA%\DHCB\configs\autocad\.</summary>
internal static class ConfigStore
{
    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHCB", "configs", "autocad");

    public static string? Load(string commandName, out string path)
    {
        path = Path.Combine(Directory, commandName + ".json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static string WriteTemplate(CommandDescriptor descriptor)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, descriptor.Name + ".json");
        if (!File.Exists(path))
        {
            var obj = new JObject { ["_description"] = descriptor.Description };
            foreach (var f in descriptor.ConfigFields)
            {
                obj[f.Key] = f.Key.Equals("dryRun", StringComparison.OrdinalIgnoreCase) ? true : (JToken)("<" + f.Value + ">");
            }
            File.WriteAllText(path, obj.ToString(Newtonsoft.Json.Formatting.Indented));
        }
        return path;
    }
}
