using System.Diagnostics;
using System.Text;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Ai;
using DhcbTools.Shared.Logic.AsBuilt;
using DhcbTools.Shared.Logic.Batch;
using DhcbTools.Shared.Logic.Handover;
using DhcbTools.Shared.Logic.Ids;
using DhcbTools.Shared.Logic.Ifc;
using Newtonsoft.Json.Linq;

namespace DhcbTools.BatchRunner;
/// <summary>Đường AutoCAD: sinh script rồi chạy accoreconsole cho từng DWG.</summary>
public static partial class Program
{
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

        // Ưu tiên vỏ core-only (không AcMgd) — NETLOAD chắc chắn trong accoreconsole; vỏ đầy đủ là dự phòng.
        var plugin = opts.PluginDll
                     ?? new[] { "DhcbTools.AutoCAD.Core.dll", "DhcbTools.AutoCAD.dll" }.Select(n => Path.Combine(AppContext.BaseDirectory, n)).FirstOrDefault(File.Exists)
                     ?? Path.Combine(AppContext.BaseDirectory, "DhcbTools.AutoCAD.Core.dll");
        if (!File.Exists(plugin))
        {
            Console.Error.WriteLine("Không tìm thấy DhcbTools.AutoCAD.Core.dll / DhcbTools.AutoCAD.dll (dùng --plugin-dll).");
            return 2;
        }

        var outputFolder = job.ResolveOutputFolder(runTime);
        if (!string.IsNullOrEmpty(outputFolder)) Directory.CreateDirectory(outputFolder);
        // Thư mục step/script riêng cho từng lần chạy, cùng dấu giờ với run-HHmmss.jsonl.
        var work = Path.Combine(Path.GetDirectoryName(runLog)!, "acad-steps-" + Path.GetFileNameWithoutExtension(runLog).Replace("run-", string.Empty));
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

            // Save = SAVEAS về chính file nguồn (QSAVE không có trong core console); SaveAs = bản sao trong
            // outputFolder. Cả hai đều phải trả lời prompt "replace it?" khi file đích đã có — với Save thì luôn có.
            string? saveAs = opts.DryRun ? null
                : job.SaveMode == SaveMode.SaveAs ? Path.Combine(outputFolder, Path.GetFileName(file.Path))
                : job.SaveMode == SaveMode.Save ? Path.GetFullPath(file.Path)
                : null;
            var saveTargetExists = saveAs is not null && File.Exists(saveAs);

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
            File.WriteAllText(script, AcadScriptGen.Build(plugin, stepPaths, saveAs, Path.GetFullPath(runLog), file.Path, plotScript, job.DwgVersion, saveTargetExists), new UTF8Encoding(false));

            Console.WriteLine($"[{index}/{job.Files.Count}] {file.Path}");
            var psi = new ProcessStartInfo(console, AcadScriptGen.Arguments(file.Path, script))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var startedAt = DateTime.Now;
            using var p = Process.Start(psi);
            if (p is null)
            {
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "Open", Success = false, Summary = "Không khởi động được accoreconsole." });
                anyFailed = true;
                continue;
            }

            // Đọc cả stdout lẫn stderr bất đồng bộ TRƯỚC khi chờ: ReadToEnd() một ống rồi mới WaitForExit
            // treo chết khi ống kia đầy (accoreconsole in khá nhiều ra stderr), và kill-khi-quá-giờ không
            // bao giờ tới lượt vì ReadToEnd chặn vô hạn.
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            var timedOut = !p.WaitForExit((int)Math.Max(60_000, (deadline - DateTime.Now).TotalMilliseconds));
            if (timedOut)
            {
                try { p.Kill(true); } catch { /* ignore */ }
                try { p.WaitForExit(10_000); } catch { /* ignore */ }
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "*", Success = false, Summary = "accoreconsole quá giờ — đã kết thúc." });
                anyFailed = true;
            }

            string output, errors;
            try
            {
                Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 10_000);
                output = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
                errors = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
            }
            catch (Exception)
            {
                output = string.Empty;
                errors = string.Empty;
            }

            File.WriteAllText(Path.Combine(work, $"{index:D3}.log"), output + (errors.Length > 0 ? "\n--- stderr ---\n" + errors : string.Empty), new UTF8Encoding(false));

            var exitCode = timedOut ? -1 : p.ExitCode;
            if (!timedOut && exitCode != 0)
            {
                var tail = Tail(errors.Length > 0 ? errors : output, 5);
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "*", Success = false, Summary = $"accoreconsole thoát mã {exitCode}." + (tail.Length > 0 ? " " + tail : string.Empty) });
                anyFailed = true;
            }

            if (saveAs is not null)
            {
                // Không có kênh nào từ accoreconsole báo "đã lưu": kiểm tra file đích có mới hơn lúc bắt đầu.
                var saved = exitCode == 0 && File.Exists(saveAs) && File.GetLastWriteTime(saveAs) >= startedAt;
                RunLog.Append(runLog, new RunLogEntry
                {
                    File = file.Path,
                    Command = "Save:" + job.SaveMode,
                    Success = saved,
                    Summary = saved
                        ? (job.SaveMode == SaveMode.Save ? "Đã lưu (SAVEAS " + job.DwgVersion + " về chính file)." : "Đã lưu bản sao: " + saveAs)
                        : "Không thấy file được lưu: " + saveAs + " (xem " + Path.Combine(work, $"{index:D3}.log") + ").",
                });
                if (!saved) anyFailed = true;
            }

            if (anyFailed && job.StopOnError) break;
        }

        return anyFailed ? 1 : 0;
    }

    /// <summary>Vài dòng cuối không rỗng của output — đủ để đọc lý do trong report.</summary>
    private static string Tail(string text, int lines)
    {
        var all = text.Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0).ToList();
        return string.Join(" | ", all.Skip(Math.Max(0, all.Count - lines)));
    }
}
