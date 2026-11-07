using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DhcbTools.AutoCAD.UI;
using DhcbTools.Shared.Hosting;
using DhcbTools.Shared.Logic.Ai;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.UiAcceptance;

/// <summary>Windows WPF acceptance. Uses a recording context; actual database operations have separate AutoCAD host suites.</summary>
internal static class Program
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(CadCommandWindow window, string name) => (T)typeof(CadCommandWindow).GetField(name, Private)!.GetValue(window)!;
    private static void Execute(CadCommandWindow window, bool preview) => typeof(CadCommandWindow).GetMethod("Execute", Private)!.Invoke(window, new object[] { preview });
    private static IDictionary Editors(CadCommandWindow window) => Field<IDictionary>(window, "_editors");
    private static Control Editor(CadCommandWindow window, string name)
    {
        var tuple = Editors(window)[name]!;
        return (Control)tuple.GetType().GetField("Item2")!.GetValue(tuple)!;
    }
    private static void Text(CadCommandWindow window, string name, string value) => ((TextBox)Editor(window, name)).Text = value;
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Context : ICadCommandContext
    {
        public string DocumentName => "Windows UI fixture.dwg";
        public string DocumentId => "ui-fixture";
        public long Revision { get; set; }
        public bool IsActive { get; set; } = true;
        public int Calls { get; private set; }
        public bool Partial { get; set; }
        public string Value { get; private set; } = "DHCB-UI-QA-OLD";
        public CommandResult Execute(string command, JObject config)
        {
            Calls++;
            if (command == "LayerExport") File.WriteAllText((string)config["outputPath"]!, "fixture CSV");
            if (config["dryRun"]?.Value<bool>() == false) { Value = (string?)config["replace"] ?? Value; Revision++; }
            var result = CommandResult.Ok("Recording context completed", 1); result.PartialSuccess = Partial; return result;
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        var folder = Path.GetFullPath(args.Length > 0 ? args[0] : "out/ui-acceptance");
        Directory.CreateDirectory(folder);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var context = new Context();
        var results = new List<object>();
        var allPassed = true;
        void Check(string name, Action body)
        {
            try { body(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { allPassed = false; results.Add(new { name, passed = false, error = ex.InnerException?.Message ?? ex.Message }); Console.WriteLine("FAIL " + name + ": " + ex); }
        }
        foreach (var descriptor in CommandCatalog.AllFor(CommandCatalog.AutoCad).Where(c => c.Implemented && !c.Internal))
        {
            Check("form " + descriptor.Name, () =>
            {
                var window = new CadCommandWindow(context, descriptor.Name, Path.Combine(folder, "configs"));
                Assert(Field<CommandDescriptor>(window, "_descriptor").Name == descriptor.Name, "Wrong initial command.");
                Assert(Editors(window).Count > 0, "No editors generated.");
                Assert(!Field<Button>(window, "_run").IsEnabled, "Write enabled before preview."); window.Close();
            });
        }
        var form = new CadCommandWindow(context, "TextReplace", Path.Combine(folder, "configs"));
        Check("preview does not write and enables commit", () =>
        {
            Text(form, "find", "DHCB-UI-QA-OLD"); Text(form, "replace", "DHCB-UI-QA-NEW"); Execute(form, true);
            Assert(context.Value == "DHCB-UI-QA-OLD", "Preview wrote drawing.");
            Assert(Field<Button>(form, "_run").IsEnabled, Field<TextBox>(form, "_result").Text);
        });
        Check("editing invalidates preview and blocks commit", () =>
        {
            Text(form, "replace", "DHCB-UI-QA-UPDATED"); var calls = context.Calls;
            Assert(!Field<Button>(form, "_run").IsEnabled, "Edit retained permission to write.");
            Execute(form, false); Assert(context.Calls == calls, "Stale preview dispatched.");
        });
        Check("model revision invalidates preview", () =>
        {
            Execute(form, true); context.Revision++; var calls = context.Calls;
            Execute(form, false); Assert(context.Calls == calls, "Changed model dispatched.");
        });
        Check("fresh preview commits exactly once", () =>
        {
            Execute(form, true); Execute(form, false);
            Assert(context.Value == "DHCB-UI-QA-UPDATED", Field<TextBox>(form, "_result").Text);
            Assert(!Field<Button>(form, "_run").IsEnabled, "Write stayed enabled after commit.");
            var calls = context.Calls; Execute(form, false); Assert(context.Calls == calls, "Replay dispatched.");
        });
        Check("partial preview cannot commit", () =>
        {
            context.Partial = true; Execute(form, true); context.Partial = false;
            Assert(!Field<Button>(form, "_run").IsEnabled, "Partial preview enabled write.");
        });
        Check("document switch blocks dispatch", () =>
        {
            context.IsActive = false; var calls = context.Calls;
            try { Execute(form, true); throw new Exception("Switched document accepted."); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }
            finally { context.IsActive = true; }
            Assert(context.Calls == calls, "Switched document dispatched.");
        });
        Check("changed input contents require fresh preview", () =>
        {
            var import = new CadCommandWindow(context, "LayerImport", Path.Combine(folder, "configs"));
            var input = Path.Combine(folder, "input.csv"); File.WriteAllText(input, "old"); Text(import, "inputPath", input); Execute(import, true);
            Assert(Field<Button>(import, "_run").IsEnabled, "Import preview failed.");
            var timestamp = File.GetLastWriteTimeUtc(input); File.WriteAllText(input, "new"); File.SetLastWriteTimeUtc(input, timestamp);
            var calls = context.Calls; Execute(import, false); Assert(context.Calls == calls, "Changed input dispatched."); import.Close();
        });
        Check("numeric editor refuses fractional and nonfinite integers", () =>
        {
            var number = new CadCommandWindow(context, "AutoNumbering", Path.Combine(folder, "configs")); Text(number, "blockName", "ANY");
            foreach (var value in new[] { "1,5", "NaN", "Infinity" })
            {
                Text(number, "startNumber", value);
                try { typeof(CadCommandWindow).GetMethod("Collect", Private)!.Invoke(number, null); throw new Exception("Invalid number accepted."); }
                catch (TargetInvocationException ex) when (ex.InnerException is FormatException) { }
            }
            number.Close();
        });
        Check("decimal comma is preserved in Vietnamese locale", () =>
        {
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");
                var number = new CadCommandWindow(context, "AutoNumbering", Path.Combine(folder, "configs"));
                Text(number, "blockName", "ANY"); Text(number, "rowToleranceMm", "1,5");
                var config = (JObject)typeof(CadCommandWindow).GetMethod("Collect", Private)!.Invoke(number, null)!;
                Assert(config["rowToleranceMm"]!.Value<double>() == 1.5, "Decimal comma changed value."); number.Close();
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = original; }
        });
        Check("accent-free search finds Vietnamese tools", () =>
        {
            var search = new CadCommandWindow(context, configDirectory: Path.Combine(folder, "configs"));
            typeof(CadCommandWindow).GetMethod("Filter", Private)!.Invoke(search, new object[] { "danh so" });
            Assert(Field<ListBox>(search, "_commands").Items.Cast<CommandDescriptor>().Any(d => d.Name == "AutoNumbering"), "Search did not fold accents."); search.Close();
        });
        Check("read-only export and remembered configuration", () =>
        {
            var export = new CadCommandWindow(context, "LayerExport", Path.Combine(folder, "configs"));
            var file = Path.Combine(folder, "layers.csv"); Text(export, "outputPath", file); Execute(export, true);
            Assert(File.Exists(file), Field<TextBox>(export, "_result").Text); export.Close();
            export = new CadCommandWindow(context, "LayerExport", Path.Combine(folder, "configs"));
            Assert(((TextBox)Editor(export, "outputPath")).Text == file, "Saved output path not restored."); export.Close();
        });
        Check("render actual Windows form", () =>
        {
            var content = (FrameworkElement)form.Content;
            content.Measure(new Size(1080, 720)); content.Arrange(new Rect(0, 0, 1080, 720)); content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1080, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(folder, "workspace.png")); encoder.Save(stream);
        });
        form.Close();
        File.WriteAllText(Path.Combine(folder, "ui.json"), JsonConvert.SerializeObject(new { allPassed, context = "recording context; AutoCAD host operations verified separately", results }, Formatting.Indented));
        app.Shutdown();
        return allPassed ? 0 : 1;
    }
}
