using System.Diagnostics;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Batch;

namespace DhcbTools.Core.Batch;

/// <summary>
/// Giai đoạn 1 — chạy một <see cref="BatchJob"/> bên trong Revit: mở → chạy step qua <see cref="RevitCommandTable"/> →
/// lưu theo saveMode → đóng, ghi <c>run.jsonl</c>. Không UI; được kích hoạt bởi vỏ (journal/pending-job) hoặc Bridge.
/// </summary>
public sealed class BatchJobRunner
{
    private readonly Application _app;

    public BatchJobRunner(Application app)
    {
        _app = app;
    }

    /// <summary>Ghi log từng step (vỏ có thể in ra journal/console).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Chạy toàn bộ job. Trả mã thoát theo mục 1.4 (0/1). Không ném — mọi lỗi vào log.</summary>
    public int Run(BatchJob job, string runLogPath, int maxMinutes = 480, bool forceDryRun = false)
    {
        var runTime = DateTime.Now;
        var outputFolder = job.ResolveOutputFolder(runTime);
        if (!string.IsNullOrEmpty(outputFolder))
        {
            try { Directory.CreateDirectory(outputFolder); } catch (Exception ex) { Log?.Invoke("Không tạo được outputFolder: " + ex.Message); }
        }

        var deadline = runTime.AddMinutes(Math.Max(1, maxMinutes));
        var entries = new List<RunLogEntry>();
        var stop = false;

        foreach (var file in job.Files)
        {
            if (DateTime.Now > deadline)
            {
                var skip = new RunLogEntry { File = file.Path, Command = "*", Skipped = true, Success = false, Summary = "Hết --max-minutes, chưa kịp chạy." };
                RunLog.Append(runLogPath, skip);
                entries.Add(skip);
                continue;
            }

            if (stop)
            {
                var skip = new RunLogEntry { File = file.Path, Command = "*", Skipped = true, Success = false, Summary = "Dừng vì stopOnError sau lỗi ở file trước." };
                RunLog.Append(runLogPath, skip);
                entries.Add(skip);
                continue;
            }

            Document? doc = null;
            var swOpen = Stopwatch.StartNew();
            try
            {
                doc = Open(file);
                Log?.Invoke($"Mở {file.Path} ({swOpen.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                var entry = new RunLogEntry { File = file.Path, Command = "Open", Success = false, Summary = "Không mở được file: " + ex.Message, ElapsedMs = swOpen.ElapsedMilliseconds };
                RunLog.Append(runLogPath, entry);
                entries.Add(entry);
                if (job.StopOnError) stop = true;
                continue;
            }

            var previousFailed = false;
            try
            {
                foreach (var step in job.StepsFor(file))
                {
                    if (step.SkipIfPreviousFailed && previousFailed)
                    {
                        var skip = new RunLogEntry { File = file.Path, Command = step.Command, Skipped = true, Success = false, Summary = "Bỏ qua vì step trước lỗi." };
                        RunLog.Append(runLogPath, skip);
                        entries.Add(skip);
                        continue;
                    }

                    var configJson = job.ExpandStepConfig(step, outputFolder, file.Path, runTime);
                    if (forceDryRun)
                    {
                        configJson = ForceDryRun(configJson);
                    }

                    var sw = Stopwatch.StartNew();
                    CommandResult result;
                    try
                    {
                        using var _ = CoreContext.Use(FailurePolicy.Silent);
                        CoreContext.SuppressedWarnings.Clear();
                        result = RevitCommandTable.Dispatch(doc, step.Command, configJson);
                        foreach (var warning in CoreContext.SuppressedWarnings)
                        {
                            result.Messages.Add("[Cảnh báo Revit bỏ qua] " + warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        result = CommandResult.Fail("Exception: " + ex.Message);
                    }
                    sw.Stop();

                    var entry = new RunLogEntry
                    {
                        File = file.Path,
                        Command = step.Command,
                        Success = result.Success,
                        Affected = result.AffectedCount,
                        Summary = result.Summary,
                        Messages = result.Messages.Take(2000).ToList(),
                        Errors = result.Errors,
                        ElapsedMs = sw.ElapsedMilliseconds,
                    };
                    RunLog.Append(runLogPath, entry);
                    entries.Add(entry);
                    Log?.Invoke($"  {(result.Success ? "OK " : "ERR")} {step.Command}: {result.Summary}");
                    previousFailed = !result.Success;
                    if (!result.Success && job.StopOnError)
                    {
                        stop = true;
                        break;
                    }
                }

                Save(doc, job, file, outputFolder, runLogPath, entries, forceDryRun);
            }
            finally
            {
                try { doc.Close(false); } catch { /* đã đóng */ }
            }
        }

        return RunLog.ExitCode(entries);
    }

    private Document Open(BatchJobFile file)
    {
        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(file.Path);
        var options = new OpenOptions { Audit = false };
        if (file.DetachFromCentral)
        {
            options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
        }

        if (file.Worksets.Count > 0)
        {
            var config = new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);
            var ids = WorksharingUtils.GetUserWorksetInfo(modelPath)
                .Where(w => file.Worksets.Any(n => string.Equals(n, w.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(w => w.Id).ToList();
            config.Open(ids);
            options.SetOpenWorksetsConfiguration(config);
        }

        return _app.OpenDocumentFile(modelPath, options);
    }

    private void Save(Document doc, BatchJob job, BatchJobFile file, string outputFolder, string runLogPath, List<RunLogEntry> entries, bool forceDryRun)
    {
        if (forceDryRun || job.SaveMode == SaveMode.None)
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        var entry = new RunLogEntry { File = file.Path, Command = "Save:" + job.SaveMode };
        try
        {
            if (job.SaveMode == SaveMode.Save)
            {
                if (doc.IsWorkshared)
                {
                    doc.SynchronizeWithCentral(new TransactWithCentralOptions(), new SynchronizeWithCentralOptions { Comment = "DHCB batch " + job.Name, SaveLocalBefore = true, SaveLocalAfter = true });
                }
                else
                {
                    doc.Save();
                }
                entry.Summary = "Đã lưu.";
            }
            else
            {
                var target = Path.Combine(outputFolder, Path.GetFileName(file.Path));
                var opts = new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 };
                if (doc.IsWorkshared)
                {
                    opts.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
                }
                doc.SaveAs(target, opts);
                entry.Summary = "Đã lưu bản sao: " + target;
            }
            entry.Success = true;
        }
        catch (Exception ex)
        {
            entry.Success = false;
            entry.Summary = "Lưu thất bại: " + ex.Message;
        }
        entry.ElapsedMs = sw.ElapsedMilliseconds;
        RunLog.Append(runLogPath, entry);
        entries.Add(entry);
    }

    private static string ForceDryRun(string configJson)
    {
        try
        {
            var obj = Newtonsoft.Json.Linq.JObject.Parse(configJson);
            obj["dryRun"] = true;
            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }
        catch
        {
            return configJson;
        }
    }
}
