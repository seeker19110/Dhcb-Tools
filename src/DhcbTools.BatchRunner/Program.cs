using System.Diagnostics;
using System.Text;
using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.Batch;
using Newtonsoft.Json.Linq;

namespace DhcbTools.BatchRunner;

/// <summary>
/// DhcbTools.BatchRunner.exe --job jobs/nightly.json [--dry-run] [--log-dir logs] [--max-minutes 480]
///                           [--revit-exe "C:\Program Files\Autodesk\Revit 2024\Revit.exe"]
///                           [--accoreconsole "C:\Program Files\Autodesk\AutoCAD 2024\accoreconsole.exe"] [--plugin-dll path]
///                           [--report-only] [--analyze]
/// Mã thoát: 0 mọi step OK · 1 có step lỗi/bỏ qua · 2 lỗi cấu hình.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var opts = Options.Parse(args);
        if (opts is null)
        {
            Console.Error.WriteLine(Options.Usage);
            return 2;
        }

        BatchJob job;
        try
        {
            job = BatchJob.Load(opts.JobPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Lỗi file job: " + ex.Message);
            return 2;
        }

        var runTime = DateTime.Now;
        var logDir = Path.Combine(opts.LogDir, runTime.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(logDir);
        var runLog = Path.Combine(logDir, "run.jsonl");
        var report = Path.Combine(logDir, "report.html");

        if (!opts.ReportOnly)
        {
            var launched = job.App.Equals("autocad", StringComparison.OrdinalIgnoreCase)
                ? RunAutoCad(job, opts, runLog, runTime)
                : RunRevit(job, opts, runLog);
            if (launched != 0 && !File.Exists(runLog))
            {
                return launched;
            }
        }

        var entries = RunLog.ReadAll(runLog);
        File.WriteAllText(report, BatchReport.Render(job.Name, entries, DateTime.Now), new UTF8Encoding(false));
        Console.WriteLine($"Báo cáo: {report}");

        if (opts.Analyze)
        {
            var groups = WarningAnalyzer.Analyze(entries);
            var summary = WarningAnalyzer.Summarize(groups, job.Name);
            var summaryPath = Path.Combine(logDir, "warnings-summary.md");
            File.WriteAllText(summaryPath, summary, new UTF8Encoding(false));
            Console.WriteLine(summary);
            Console.WriteLine($"Tóm tắt: {summaryPath}");
        }

        var code = entries.Count == 0 ? 1 : RunLog.ExitCode(entries);
        Console.WriteLine($"Kết thúc, mã thoát {code}: {entries.Count(e => e.Success && !e.Skipped)} OK, {entries.Count(e => !e.Success && !e.Skipped)} lỗi, {entries.Count(e => e.Skipped)} bỏ qua.");
        return code;
    }

    // ── Revit: pending-job + journal → Revit.exe → add-in chạy → batch-done.json ───────────────────────

    /// <summary>Mục 7.13 (RevitBatchProcessor): phiên bản Revit theo header file — nhiều phiên bản khác nhau → dùng cao nhất và cảnh báo.</summary>
    internal static int ResolveRevitVersion(BatchJob job, Action<string> log)
    {
        var detected = job.Files.Select(f => (f.Path, Version: RvtFileInfo.DetectVersion(f.Path))).Where(t => t.Version.HasValue).Select(t => (t.Path, t.Version!.Value)).ToList();
        if (detected.Count == 0)
        {
            return job.RevitVersion;
        }

        var max = detected.Max(d => d.Item2);
        foreach (var (path, v) in detected.Where(d => d.Item2 != max))
        {
            log($"Cảnh báo: {path} lưu bằng Revit {v}, sẽ mở bằng Revit {max} (nâng cấp trong phiên, không ghi ngược nếu saveMode=None/SaveAs).");
        }

        if (max != job.RevitVersion)
        {
            log($"Phiên bản Revit theo file: {max} (job ghi {job.RevitVersion}).");
        }

        return max;
    }

    private static int RunRevit(BatchJob job, Options opts, string runLog)
    {
        var version = opts.AutoDetectVersion ? ResolveRevitVersion(job, Console.WriteLine) : job.RevitVersion;
        var revitExe = opts.RevitExe ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk", "Revit " + version, "Revit.exe");
        if (!File.Exists(revitExe))
        {
            Console.Error.WriteLine("Không tìm thấy Revit.exe: " + revitExe + " (dùng --revit-exe).");
            return 2;
        }

        var dhcbDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHCB");
        Directory.CreateDirectory(dhcbDir);
        var pending = Path.Combine(dhcbDir, "pending-job.json");
        var done = Path.Combine(dhcbDir, "batch-done.json");
        File.Delete(done);
        File.WriteAllText(pending, new JObject
        {
            ["jobPath"] = Path.GetFullPath(opts.JobPath),
            ["runLogPath"] = Path.GetFullPath(runLog),
            ["maxMinutes"] = opts.MaxMinutes,
            ["dryRun"] = opts.DryRun,
        }.ToString());

        var journal = Path.Combine(dhcbDir, "dhcb-batch.txt");
        File.WriteAllText(journal, RevitJournal(), new UTF8Encoding(false));

        Console.WriteLine($"Mở Revit: {revitExe}");
        using var process = Process.Start(new ProcessStartInfo(revitExe, "\"" + journal + "\" /nosplash") { UseShellExecute = false });
        if (process is null)
        {
            Console.Error.WriteLine("Không khởi động được Revit.");
            return 2;
        }

        var deadline = DateTime.Now.AddMinutes(opts.MaxMinutes + 15);
        while (!process.HasExited && !File.Exists(done) && DateTime.Now < deadline)
        {
            Thread.Sleep(5000);
        }

        if (!process.HasExited)
        {
            if (!process.WaitForExit(60_000))
            {
                Console.Error.WriteLine("Revit không thoát sau khi xong — kết thúc tiến trình.");
                try { process.Kill(true); } catch { /* ignore */ }
            }
        }

        if (!File.Exists(done))
        {
            Console.Error.WriteLine("Add-in không báo hoàn thành (batch-done.json). Kiểm tra add-in đã cài cho Revit " + version + " và batch-error.txt.");
            File.Delete(pending);
            return 1;
        }

        var exit = (int?)JObject.Parse(File.ReadAllText(done))["exitCode"] ?? 1;
        Console.WriteLine($"Revit báo mã thoát {exit}.");
        return exit;
    }

    /// <summary>Journal tối giản: tắt hộp thoại hỏi khi lỗi để chạy không người trực. Add-in tự làm phần còn lại.</summary>
    internal static string RevitJournal()
    {
        return string.Join("\r\n",
            "' DHCB Tools batch journal",
            "Dim Jrn",
            "Set Jrn = CrsJournalScript",
            "Jrn.Directive \"DebugMode\", \"PerformAutomaticActionInErrorDialog\", 1",
            "Jrn.Directive \"DebugMode\", \"PermissiveJournal\", 1",
            "Jrn.Directive \"DocSymbol\", \"[]\"",
            string.Empty);
    }

    // ── AutoCAD: accoreconsole cho từng DWG ─────────────────────────────────────────────────────────

    private static int RunAutoCad(BatchJob job, Options opts, string runLog, DateTime runTime)
    {
        var console = opts.AccoreConsole ?? Directory.GetDirectories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk"), "AutoCAD *")
            .Select(d => Path.Combine(d, "accoreconsole.exe")).FirstOrDefault(File.Exists);
        if (console is null || !File.Exists(console))
        {
            Console.Error.WriteLine("Không tìm thấy accoreconsole.exe (dùng --accoreconsole).");
            return 2;
        }

        var plugin = opts.PluginDll ?? Path.Combine(AppContext.BaseDirectory, "DhcbTools.AutoCAD.dll");
        if (!File.Exists(plugin))
        {
            Console.Error.WriteLine("Không tìm thấy DhcbTools.AutoCAD.dll (dùng --plugin-dll).");
            return 2;
        }

        var outputFolder = job.ResolveOutputFolder(runTime);
        if (!string.IsNullOrEmpty(outputFolder)) Directory.CreateDirectory(outputFolder);
        var work = Path.Combine(Path.GetDirectoryName(runLog)!, "acad-steps");
        Directory.CreateDirectory(work);

        var deadline = runTime.AddMinutes(opts.MaxMinutes);
        var anyFailed = false;
        var index = 0;
        foreach (var file in job.Files)
        {
            index++;
            if (DateTime.Now > deadline)
            {
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "*", Skipped = true, Summary = "Hết --max-minutes." });
                continue;
            }

            if (!File.Exists(file.Path))
            {
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "Open", Success = false, Summary = "Không tìm thấy file." });
                anyFailed = true;
                if (job.StopOnError) break;
                continue;
            }

            var stepPaths = new List<string>();
            var s = 0;
            foreach (var step in job.StepsFor(file).Where(st => !st.Command.Equals("PlotPdf", StringComparison.OrdinalIgnoreCase)))
            {
                var cfg = job.ExpandStepConfig(step, outputFolder, file.Path, runTime);
                if (opts.DryRun)
                {
                    var o = JObject.Parse(cfg);
                    o["dryRun"] = true;
                    cfg = o.ToString(Newtonsoft.Json.Formatting.None);
                }

                var stepPath = Path.Combine(work, $"{index:D3}-{s++:D2}-{step.Command}.json");
                File.WriteAllText(stepPath, AcadScriptGen.StepJson(step.Command, cfg), new UTF8Encoding(false));
                stepPaths.Add(stepPath);
            }

            string? saveAs = job.SaveMode == SaveMode.SaveAs && !opts.DryRun ? Path.Combine(outputFolder, Path.GetFileName(file.Path)) : null;

            // Step đặc biệt "PlotPdf" (mục 7.13): không phải lệnh Core — sinh -PLOT trong script accoreconsole.
            string? plotScript = null;
            foreach (var step in job.StepsFor(file).Where(st => st.Command.Equals("PlotPdf", StringComparison.OrdinalIgnoreCase)))
            {
                var cfg = JObject.Parse(job.ExpandStepConfig(step, outputFolder, file.Path, runTime));
                var pdf = (string?)cfg["outputPath"] ?? Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(file.Path) + ".pdf");
                plotScript = (plotScript ?? string.Empty) + AcadScriptGen.PlotPdf(pdf,
                    (string?)cfg["layout"] ?? "Model", (string?)cfg["paperSize"] ?? "ISO A3 (420.00 x 297.00 MM)",
                    (string?)cfg["orientation"] ?? "Landscape", (string?)cfg["plotArea"] ?? "Extents", (string?)cfg["plotStyle"] ?? "monochrome.ctb");
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "PlotPdf", Success = true, Summary = "Đã xếp lệnh -PLOT → " + pdf + " (kết quả thật xem file PDF)." });
            }

            var script = Path.Combine(work, $"{index:D3}.scr");
            File.WriteAllText(script, AcadScriptGen.Build(plugin, stepPaths, saveAs, Path.GetFullPath(runLog), file.Path, plotScript), new UTF8Encoding(false));

            Console.WriteLine($"[{index}/{job.Files.Count}] {file.Path}");
            var psi = new ProcessStartInfo(console, $"/i \"{file.Path}\" /s \"{script}\" /l en-US") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            if (p is null)
            {
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "Open", Success = false, Summary = "Không khởi động được accoreconsole." });
                anyFailed = true;
                continue;
            }

            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit((int)Math.Max(60_000, (deadline - DateTime.Now).TotalMilliseconds));
            if (!p.HasExited)
            {
                try { p.Kill(true); } catch { /* ignore */ }
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "*", Success = false, Summary = "accoreconsole quá giờ — đã kết thúc." });
                anyFailed = true;
            }

            File.WriteAllText(Path.Combine(work, $"{index:D3}.log"), output, new UTF8Encoding(false));
            if (job.SaveMode == SaveMode.Save && !opts.DryRun)
            {
                // accoreconsole với /i mở file gốc; SAVEAS về chính nó tương đương Save.
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "Save:Save", Success = true, Summary = "Lưu bằng script (QSAVE không có trong core console; dùng SaveAs cùng đường dẫn nếu cần)." });
            }
        }

        return anyFailed ? 1 : 0;
    }
}

internal sealed class Options
{
    public string JobPath { get; private set; } = string.Empty;
    public bool DryRun { get; private set; }
    public string LogDir { get; private set; } = "logs";
    public int MaxMinutes { get; private set; } = 480;
    public string? RevitExe { get; private set; }
    public string? AccoreConsole { get; private set; }
    public string? PluginDll { get; private set; }
    public bool ReportOnly { get; private set; }
    public bool Analyze { get; private set; }
    public bool AutoDetectVersion { get; private set; } = true;

    public const string Usage = """
        DhcbTools.BatchRunner --job <job.json> [--dry-run] [--log-dir logs] [--max-minutes 480]
                              [--revit-exe <Revit.exe>] [--accoreconsole <accoreconsole.exe>] [--plugin-dll <DhcbTools.AutoCAD.dll>]
                              [--report-only] [--analyze] [--no-autodetect]
        (Revit: phiên bản tự nhận từ header .rvt; step "PlotPdf" trong job AutoCAD sinh -PLOT ra PDF)
        """;

    public static Options? Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Thiếu giá trị cho " + args[i]);
            try
            {
                switch (args[i])
                {
                    case "--job": o.JobPath = Next(); break;
                    case "--dry-run": o.DryRun = true; break;
                    case "--log-dir": o.LogDir = Next(); break;
                    case "--max-minutes": o.MaxMinutes = int.Parse(Next()); break;
                    case "--revit-exe": o.RevitExe = Next(); break;
                    case "--accoreconsole": o.AccoreConsole = Next(); break;
                    case "--plugin-dll": o.PluginDll = Next(); break;
                    case "--report-only": o.ReportOnly = true; break;
                    case "--analyze": o.Analyze = true; break;
                    case "--no-autodetect": o.AutoDetectVersion = false; break;
                    case "-h": case "--help": return null;
                    default:
                        Console.Error.WriteLine("Tham số không biết: " + args[i]);
                        return null;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return null;
            }
        }

        return string.IsNullOrEmpty(o.JobPath) ? null : o;
    }
}
