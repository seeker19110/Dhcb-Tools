using System.Text;
using DhcbTools.Shared.Logic.Batch;
using Newtonsoft.Json.Linq;

namespace DhcbTools.BatchRunner;

/// <summary>Xuất PDF qua file tạm cùng ổ đĩa; chỉ báo thành công khi có PDF mới và lượt chạy sạch.</summary>
public sealed class AutoCadPlot
{
    public string Target { get; }
    public string? Staging { get; }
    public string? Script { get; }
    public string SettingsSummary { get; }

    public AutoCadPlot(JObject config, string defaultTarget, bool dryRun)
    {
        Target = Path.GetFullPath((string?)config["outputPath"] ?? defaultTarget);
        if (!Path.GetExtension(Target).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("PlotPdf yêu cầu outputPath có đuôi .pdf.");
        var pageSetupName = (string?)config["pageSetupName"];
        if (pageSetupName != null && new[] { "paperSize", "orientation", "plotArea", "plotStyle", "plotScale" }.Any(k => config[k] != null))
            throw new ArgumentException("pageSetupName dùng cấu hình in đã lưu; bỏ paperSize/orientation/plotArea/plotStyle/plotScale để tránh ghi đè ngầm.");
        // Kiểm cấu hình cả khi preview, trước khi tạo thư mục/file.
        _ = AcadScriptGen.PlotPdf(Target,
            (string?)config["layout"] ?? "Model", (string?)config["paperSize"] ?? "ISO A3 (420.00 x 297.00 MM)",
            (string?)config["orientation"] ?? "Landscape", (string?)config["plotArea"] ?? "Extents",
            (string?)config["plotStyle"] ?? "monochrome.ctb", plotScale: (string?)config["plotScale"], pageSetupName: pageSetupName);
        var layout = (string?)config["layout"] ?? "Model";
        SettingsSummary = pageSetupName != null ? "layout " + layout + ", page setup " + pageSetupName
            : "layout " + layout + ", tỷ lệ " + AcadScriptGen.NormalizePlotScale((string?)config["plotScale"], layout.Equals("Model", StringComparison.OrdinalIgnoreCase));
        if (dryRun) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        Staging = Path.Combine(Path.GetDirectoryName(Target)!, ".dhcb-plot-" + Guid.NewGuid().ToString("N") + ".pdf");
        Script = AcadScriptGen.PlotPdf(Staging,
            (string?)config["layout"] ?? "Model", (string?)config["paperSize"] ?? "ISO A3 (420.00 x 297.00 MM)",
            (string?)config["orientation"] ?? "Landscape", (string?)config["plotArea"] ?? "Extents",
            (string?)config["plotStyle"] ?? "monochrome.ctb", plotScale: (string?)config["plotScale"], pageSetupName: pageSetupName);
    }

    public RunLogEntry Complete(string source, string? blocker, Action<string, string>? publish = null)
    {
        var result = new RunLogEntry { File = source, Command = "PlotPdf" };
        if (Staging is null)
        {
            result.Success = true;
            result.Summary = "Xem trước: sẽ xuất PDF (" + SettingsSummary + ") → " + Target + "; chưa ghi file.";
            return result;
        }
        try
        {
            if (blocker != null)
            {
                result.Summary = "Không xuất PDF vì " + blocker;
                return result;
            }
            if (!File.Exists(Staging))
            {
                result.Summary = "Không thấy PDF mới sau -PLOT: " + Target;
                return result;
            }
            using (var stream = File.OpenRead(Staging))
            {
                var header = new byte[5];
                if (stream.Read(header, 0, header.Length) != header.Length || Encoding.ASCII.GetString(header) != "%PDF-")
                {
                    result.Summary = "File -PLOT tạo ra không có header PDF: " + Target;
                    return result;
                }
            }
            publish ??= (staging, target) => StagedSave.Promote(staging, target, keepBackup: false);
            for (var attempt = 0; ; attempt++)
            {
                try { publish(Staging, Target); break; }
                catch (IOException ex) when (attempt < 3 && File.Exists(Staging)
                    && ((ex.HResult & 0xffff) is 32 or 33 or 1175))
                {
                    // Windows indexer/antivirus can briefly prevent atomic Replace even after all our handles close.
                    // Retry that native operation only; never delete the destination as a fallback.
                    Thread.Sleep(50 * (attempt + 1));
                }
            }
            result.Success = true;
            result.Summary = "Đã xuất PDF: " + Target + " (" + SettingsSummary + ").";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            result.Summary = "Không xuất được PDF: " + ex.Message;
        }
        finally
        {
            Discard();
        }
        return result;
    }

    public void Discard()
    {
        if (Staging is null) return;
        try { File.Delete(Staging); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { /* giữ file tạm nếu bị khoá */ }
    }
}
