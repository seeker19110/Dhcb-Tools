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
        var installation = AutoCadInstallationResolver.Resolve(opts.AccoreConsole, opts.PluginDll,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppContext.BaseDirectory);
        if (!installation.Success)
        {
            Console.Error.WriteLine(installation.Error);
            return 2;
        }
        var console = installation.ConsolePath!;
        var plugin = installation.PluginPath!;
        if (installation.Warning is not null) Console.Error.WriteLine(installation.Warning);
        Console.WriteLine("AutoCAD: " + console + " · DLL: " + plugin);

        var outputFolder = job.ResolveOutputFolder(runTime);
        if (!string.IsNullOrEmpty(outputFolder)) Directory.CreateDirectory(outputFolder);
        // Thư mục step/script riêng cho từng lần chạy, cùng dấu giờ với run-HHmmss.jsonl.
        var work = Path.Combine(Path.GetDirectoryName(runLog)!, "acad-steps-" + Path.GetFileNameWithoutExtension(runLog).Replace("run-", string.Empty));
        Directory.CreateDirectory(work);

        var deadline = runTime.AddMinutes(opts.MaxMinutes);
        var anyFailed = false;
        var stop = false;
        var index = 0;
        foreach (var file in job.Files)
        {
            index++;
            if (DateTime.Now > deadline)
            {
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "*", Skipped = true, Summary = "Hết --max-minutes." });
                continue;
            }

            if (stop)
            {
                // Như batch Revit: file không được chạy vẫn phải có dòng trong log, nếu không report.html chỉ liệt kê
                // các file đã chạy và người đọc không biết còn file nào bị bỏ lại.
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "*", Skipped = true, Summary = "Dừng vì stopOnError sau lỗi ở file trước." });
                continue;
            }

            if (!File.Exists(file.Path))
            {
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "Open", Success = false, Summary = "Không tìm thấy file." });
                anyFailed = true;
                stop = job.StopOnError;
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
            string? saveTarget = opts.DryRun ? null
                : job.SaveMode == SaveMode.SaveAs ? Path.Combine(outputFolder, Path.GetFileName(file.Path))
                : job.SaveMode == SaveMode.Save ? Path.GetFullPath(file.Path)
                : null;
            // saveOnError=false (mặc định): SAVEAS vào file tạm cạnh đích, chỉ thay đích khi log của file này sạch —
            // script không tự bỏ được dòng SAVEAS khi một DHCB_RUN lỗi (xem StagedSave).
            var saveAs = saveTarget is null || job.SaveOnError ? saveTarget : StagedSave.StagingPath(saveTarget, runTime);
            var saveTargetExists = saveAs is not null && File.Exists(saveAs);

            // -PLOT không ghi log từ add-in: giữ file đích cho tới khi xác minh kết quả thật.
            var plots = new List<AutoCadPlot>();
            string? plotScript = null;
            try
            {
                foreach (var step in job.StepsFor(file).Where(st => st.Command.Equals("PlotPdf", StringComparison.OrdinalIgnoreCase)))
                {
                    var cfg = JObject.Parse(job.ExpandStepConfig(step, outputFolder, file.Path, runTime));
                    var plot = new AutoCadPlot(cfg, Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(file.Path) + ".pdf"), opts.DryRun);
                    plots.Add(plot);
                    plotScript += plot.Script;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is Newtonsoft.Json.JsonException)
            {
                foreach (var plot in plots) plot.Discard();
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "PlotPdf", Summary = "Lỗi cấu hình xuất PDF: " + ex.Message });
                anyFailed = true;
                stop = job.StopOnError;
                continue;
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
            var linesBefore = File.Exists(runLog) ? File.ReadAllLines(runLog).Length : 0;
            Process? startedProcess;
            try { startedProcess = Process.Start(psi); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is IOException || ex is InvalidOperationException)
            {
                foreach (var plot in plots) plot.Discard();
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "Open", Summary = "Không khởi động được accoreconsole: " + ex.Message });
                anyFailed = true;
                stop = job.StopOnError;
                continue;
            }
            using var p = startedProcess;
            if (p is null)
            {
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "Open", Success = false, Summary = "Không khởi động được accoreconsole." });
                anyFailed = true;
                foreach (var plot in plots) plot.Discard();
                stop = job.StopOnError;
                continue;
            }

            // Đọc cả stdout lẫn stderr bất đồng bộ TRƯỚC khi chờ: ReadToEnd() một ống rồi mới WaitForExit
            // treo chết khi ống kia đầy (accoreconsole in khá nhiều ra stderr), và kill-khi-quá-giờ không
            // bao giờ tới lượt vì ReadToEnd chặn vô hạn.
            var stdoutTask = ReadConsoleAsync(p.StandardOutput.BaseStream);
            var stderrTask = ReadConsoleAsync(p.StandardError.BaseStream);
            var timedOut = !p.WaitForExit((int)Math.Min(int.MaxValue, Math.Max(60_000, (deadline - DateTime.Now).TotalMilliseconds)));
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
            // accoreconsole thoát 0 kể cả khi NETLOAD thất bại và mọi DHCB_RUN là "Unknown command": không dòng
            // nào vào run.jsonl, báo cáo "0 OK, 0 lỗi" trông như chưa chạy gì (§57). Bắt bằng hai dấu hiệu:
            // dòng "Unable to load" trong output, hoặc số dòng log của file này không tăng dù có step.
            var netload = AcadScriptGen.NetloadFailure(output + "\n" + errors);
            var logLines = File.Exists(runLog) ? File.ReadAllLines(runLog) : Array.Empty<string>();
            var linesAfter = logLines.Length;
            // Dòng DHCB_RUN của RIÊNG file này (ghi trong lúc accoreconsole chạy) — căn cứ để quyết định lưu.
            var stepEntries = logLines.Skip(linesBefore).Select(RunLog.Deserialize).OfType<RunLogEntry>().ToList();
            anyFailed |= stepEntries.Any(e => !e.IsComplete) || stepEntries.Count < stepPaths.Count;
            var netloadFailed = netload != null || (stepPaths.Count > 0 && linesAfter == linesBefore);
            if (!timedOut && netloadFailed)
            {
                RunLog.Append(runLog, new RunLogEntry
                {
                    File = file.Path,
                    Command = "NETLOAD",
                    Success = false,
                    Summary = (netload ?? $"Không ca nào ghi vào run.jsonl dù script có {stepPaths.Count} step — NETLOAD thất bại?")
                              + " DLL: " + plugin + " (xem " + Path.Combine(work, $"{index:D3}.log") + ").",
                });
                anyFailed = true;
            }

            if (!timedOut && exitCode != 0)
            {
                var tail = Tail(errors.Length > 0 ? errors : output, 5);
                RunLog.Append(runLog, new RunLogEntry { File = file.Path, Command = "*", Success = false, Summary = $"accoreconsole thoát mã {exitCode}." + (tail.Length > 0 ? " " + tail : string.Empty) });
                anyFailed = true;
            }

            var plotBlocker = StagedSave.Blocker(timedOut, exitCode, netloadFailed, stepPaths.Count, stepEntries);
            foreach (var plot in plots)
            {
                var plotEntry = plot.Complete(file.Path, plotBlocker);
                RunLog.Append(runLog, plotEntry);
                stepEntries.Add(plotEntry);
                anyFailed |= !plotEntry.IsComplete;
            }

            if (saveTarget is not null && saveAs is not null)
            {
                var saveEntry = SaveEntry(job, file.Path, saveTarget, saveAs, startedAt, exitCode,
                    StagedSave.Blocker(timedOut, exitCode, netloadFailed, stepPaths.Count, stepEntries),
                    Path.Combine(work, $"{index:D3}.log"));
                RunLog.Append(runLog, saveEntry);
                if (!saveEntry.Success) anyFailed = true;
            }

            if (anyFailed && job.StopOnError) stop = true;
        }

        return anyFailed ? 1 : 0;
    }

    /// <summary>
    /// Dòng log "Save:&lt;mode&gt;" của một file. <paramref name="saveAs"/> là đường script đã SAVEAS tới: chính
    /// <paramref name="saveTarget"/> khi <c>saveOnError=true</c> (như bản cũ), file tạm cạnh đích khi không —
    /// khi đó chỉ thay đích nếu <paramref name="blocker"/> là null, còn lại bỏ file tạm và ghi rõ vì sao không lưu.
    /// </summary>
    private static RunLogEntry SaveEntry(BatchJob job, string source, string saveTarget, string saveAs, DateTime startedAt,
        int exitCode, string? blocker, string consoleLog)
    {
        var entry = new RunLogEntry { File = source, Command = "Save:" + job.SaveMode };
        // Không có kênh nào từ accoreconsole báo "đã lưu": kiểm tra file script vừa SAVEAS có mới hơn lúc bắt đầu.
        var written = File.Exists(saveAs) && File.GetLastWriteTime(saveAs) >= startedAt;
        var savedText = job.SaveMode == SaveMode.Save ? "Đã lưu (SAVEAS " + job.DwgVersion + " về chính file)." : "Đã lưu bản sao: " + saveTarget;

        if (job.SaveOnError)
        {
            // Người dùng đã chọn giữ cả phần làm được của file lỗi: script SAVEAS thẳng vào đích như bản cũ.
            entry.Success = exitCode == 0 && written;
            entry.Summary = entry.Success ? savedText : "Không thấy file được lưu: " + saveAs + " (xem " + consoleLog + ").";
            return entry;
        }

        if (blocker != null)
        {
            TryDelete(saveAs);
            entry.Skipped = true;
            entry.Summary = "Không lưu vì " + blocker + " (đặt saveOnError=true nếu vẫn muốn lưu).";
            return entry;
        }

        if (!written)
        {
            entry.Summary = "Không thấy file được lưu: " + saveAs + " (xem " + consoleLog + ").";
            return entry;
        }

        try
        {
            StagedSave.Promote(saveAs, saveTarget, keepBackup: job.SaveMode == SaveMode.Save);
            entry.Success = true;
            entry.Summary = savedText + (job.SaveMode == SaveMode.Save ? " Bản trước giữ ở " + Path.ChangeExtension(saveTarget, ".bak") + "." : string.Empty);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // File đích đang mở ở máy khác, bị khoá quyền… — việc của đêm nay vẫn còn nguyên trong file tạm.
            entry.Summary = "Không thay được " + saveTarget + " (" + ex.Message + "). Bản đã lưu nằm ở " + saveAs + ".";
        }

        return entry;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { /* file tạm, dọn tay */ }
    }

    /// <summary>Đọc hai pipe đồng thời và giải mã output native theo byte.</summary>
    private static async Task<string> ReadConsoleAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return AcadScriptGen.DecodeConsole(buffer.ToArray());
    }

    /// <summary>Vài dòng cuối không rỗng của output — đủ để đọc lý do trong report.</summary>
    private static string Tail(string text, int lines)
    {
        var all = text.Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0).ToList();
        return string.Join(" | ", all.Skip(Math.Max(0, all.Count - lines)));
    }
}
