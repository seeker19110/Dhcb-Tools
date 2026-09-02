using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DhcbTools.Core.AutoCAD;
using DhcbTools.Shared.Logic.Batch;
using Newtonsoft.Json.Linq;

[assembly: CommandClass(typeof(DhcbTools.AutoCAD.Core.RunCommand))]

namespace DhcbTools.AutoCAD.Core;

/// <summary>
/// <c>DHCB_RUN "step.json" "run.jsonl" "source.dwg"</c> — bản core-only cho accoreconsole (P2 giai đoạn 7): cùng hành vi với
/// DHCB_RUN trong DhcbTools.AutoCAD nhưng assembly này không tham chiếu AcMgd nên NETLOAD được trong Core Console.
/// Batch runner trỏ <c>--plugin-dll</c> vào DhcbTools.AutoCAD.Core.dll.
/// </summary>
public sealed class RunCommand
{
    [CommandMethod("DHCB_RUN", CommandFlags.Modal)]
    public void RunStep()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;

        var stepPath = Ask(ed, "File step JSON");
        var logPath = Ask(ed, "File run.jsonl");
        var source = Ask(ed, "File nguồn") ?? doc.Name;
        if (string.IsNullOrWhiteSpace(stepPath) || string.IsNullOrWhiteSpace(logPath)) return;

        var sw = Stopwatch.StartNew();
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
            ed.WriteMessage($"\n{(result.Success ? "✓" : "✗")} {result.Summary}\n");
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

    private static string? Ask(Editor ed, string prompt)
    {
        var r = ed.GetString(new PromptStringOptions("\n" + prompt + ": ") { AllowSpaces = true });
        return r.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(r.StringResult) ? r.StringResult : null;
    }
}
