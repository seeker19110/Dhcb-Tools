using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Mep;

namespace DhcbTools.Core.MEPF;

/// <summary>Mục 3.3 — đề xuất kích thước theo lưu lượng, ghi CSV để kỹ sư duyệt.</summary>
public sealed class SizingProposalConfig
{
    public required string OutputPath { get; init; }

    /// <summary>Ma sát cho phép duct (Pa/m).</summary>
    public double MaxPaPerM { get; init; } = 1.0;

    /// <summary>Vận tốc tối đa duct (m/s).</summary>
    public double MaxDuctVelocityMs { get; init; } = 8.0;

    /// <summary>Vận tốc tối đa pipe (m/s).</summary>
    public double MaxPipeVelocityMs { get; init; } = 2.0;

    /// <summary>Duct chữ nhật: giữ chiều cao hiện tại, chỉ đề xuất chiều rộng.</summary>
    public bool KeepDuctHeight { get; init; } = true;

    /// <summary>Lọc theo System Name chứa chuỗi này (rỗng = tất cả).</summary>
    public string? SystemNameContains { get; init; }

    /// <summary>Chỉ liệt kê đoạn có kích thước đề xuất khác hiện tại.</summary>
    public bool OnlyChanges { get; init; } = false;
}

public sealed class ApplySizingConfig
{
    public required string InputPath { get; init; }

    public bool DryRun { get; init; } = true;
}

/// <summary>Xuất CSV <c>ElementId,Category,SystemName,FlowLps,CurrentSizeMm,SuggestedSizeMm,VelocityMs,Reason</c>.</summary>
public sealed class SizingProposalCommand : ICoreCommand<SizingProposalConfig>
{
    public string CommandName => "SizingProposal";

    private const double Ft3sToLps = 28.316846592;

    public CommandResult Execute(Document document, SizingProposalConfig config)
    {
        var sb = new StringBuilder("ElementId,Category,SystemName,FlowLps,CurrentSizeMm,SuggestedSizeMm,VelocityMs,Reason\n");
        var result = CommandResult.Ok(string.Empty);
        var rows = 0;
        var changes = 0;

        var curves = new FilteredElementCollector(document)
            .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves }))
            .WhereElementIsNotElementType()
            .Cast<MEPCurve>();

        foreach (var curve in curves)
        {
            var systemName = curve.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? string.Empty;
            if (!string.IsNullOrEmpty(config.SystemNameContains) && systemName.IndexOf(config.SystemNameContains!, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var flowParam = curve is Duct ? curve.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM) : curve.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_PARAM);
            if (flowParam == null || !flowParam.HasValue)
            {
                result.Messages.Add($"{curve.Id}: không có lưu lượng — bỏ qua.");
                continue;
            }

            var flowLps = flowParam.AsDouble() * Ft3sToLps;
            if (flowLps <= 0)
            {
                continue;
            }

            string current;
            SizingSuggestion suggestion;
            try
            {
                if (curve is Duct)
                {
                    var dia = curve.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                    if (dia != null && dia.HasValue && dia.AsDouble() > 0)
                    {
                        current = NumericText.Format(RevitCompat.FtToMm(dia.AsDouble()), 0);
                        suggestion = DuctSizing.SuggestRound(flowLps, config.MaxPaPerM, config.MaxDuctVelocityMs);
                    }
                    else
                    {
                        var w = RevitCompat.FtToMm(curve.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0);
                        var h = RevitCompat.FtToMm(curve.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0);
                        current = NumericText.Format(w, 0) + "x" + NumericText.Format(h, 0);
                        suggestion = config.KeepDuctHeight && h > 0
                            ? DuctSizing.SuggestRectangularWidth(flowLps, h, config.MaxPaPerM, config.MaxDuctVelocityMs)
                            : DuctSizing.SuggestRound(flowLps, config.MaxPaPerM, config.MaxDuctVelocityMs);
                        if (config.KeepDuctHeight && h > 0 && suggestion.SuggestedMm > 0)
                        {
                            suggestion = new SizingSuggestion(suggestion.SuggestedMm, suggestion.VelocityMs, suggestion.Reason + " (giữ cao " + NumericText.Format(h, 0) + ")");
                        }
                    }
                }
                else
                {
                    var dia = curve.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                    current = NumericText.Format(RevitCompat.FtToMm(dia?.AsDouble() ?? 0), 0);
                    suggestion = PipeSizing.SuggestDn(flowLps, config.MaxPipeVelocityMs);
                }
            }
            catch (Exception ex)
            {
                result.Messages.Add($"{curve.Id}: {ex.Message}");
                continue;
            }

            var suggestedText = suggestion.SuggestedMm > 0 ? NumericText.Format(suggestion.SuggestedMm, 0) : string.Empty;
            var changed = !current.Split('x')[0].Equals(suggestedText, StringComparison.Ordinal);
            if (changed) changes++;
            if (config.OnlyChanges && !changed)
            {
                continue;
            }

            sb.Append(CsvText.JoinLine(new[]
            {
                RevitCompat.IdValue(curve.Id).ToString(), curve.Category?.Name ?? string.Empty, systemName, NumericText.Format(flowLps, 1),
                current, suggestedText, NumericText.Format(suggestion.VelocityMs, 2), suggestion.Reason,
            })).Append('\n');
            rows++;
        }

        var outputDir = Path.GetDirectoryName(config.OutputPath);
        if (!string.IsNullOrEmpty(outputDir)) Directory.CreateDirectory(outputDir);
        File.WriteAllText(config.OutputPath, sb.ToString(), CsvText.Utf8WithBom);
        result.Summary = $"Đã đề xuất kích thước cho {rows} đoạn ({changes} khác hiện tại) → \"{config.OutputPath}\". Duyệt trong Excel rồi chạy ApplySizing.";
        result.AffectedCount = rows;
        return result;
    }
}

/// <summary>Áp lại CSV đã duyệt: cột SuggestedSizeMm ghi vào đường kính (tròn/ống) hoặc chiều rộng (duct chữ nhật).</summary>
public sealed class ApplySizingCommand : ICoreCommand<ApplySizingConfig>
{
    public string CommandName => "ApplySizing";

    public CommandResult Execute(Document document, ApplySizingConfig config)
    {
        if (!File.Exists(config.InputPath))
        {
            return CommandResult.Fail($"Không tìm thấy file \"{config.InputPath}\".");
        }

        var lines = File.ReadAllLines(config.InputPath, CsvText.Utf8WithBom);
        if (lines.Length < 2)
        {
            return CommandResult.Fail("File CSV chỉ có tiêu đề hoặc rỗng.");
        }

        var header = CsvText.SplitLine(lines[0]);
        var idCol = header.FindIndex(h => h.Equals("ElementId", StringComparison.OrdinalIgnoreCase));
        var sizeCol = header.FindIndex(h => h.Equals("SuggestedSizeMm", StringComparison.OrdinalIgnoreCase));
        if (idCol < 0 || sizeCol < 0)
        {
            return CommandResult.Fail("CSV cần cột ElementId và SuggestedSizeMm.");
        }

        var plan = new List<(MEPCurve Curve, double Mm)>();
        var result = CommandResult.Ok(string.Empty);
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cells = CsvText.SplitLine(lines[i]);
            if (cells.Count <= Math.Max(idCol, sizeCol) || !RevitCompat.TryParseId(cells[idCol], out var id) || !NumericText.TryParseDouble(cells[sizeCol], out var mm) || mm <= 0)
            {
                result.Messages.Add($"Dòng {i + 1}: thiếu ElementId/SuggestedSizeMm hợp lệ — bỏ qua.");
                continue;
            }

            if (document.GetElement(id) is not MEPCurve curve)
            {
                result.Messages.Add($"Dòng {i + 1}: {RevitCompat.IdValue(id)} không phải Duct/Pipe — bỏ qua.");
                continue;
            }

            plan.Add((curve, mm));
        }

        if (config.DryRun)
        {
            result.Summary = $"[Xem trước] Sẽ đổi kích thước {plan.Count} đoạn.";
            result.Messages.AddRange(plan.Select(p => $"{p.Curve.Id}: → {NumericText.Format(p.Mm, 0)} mm"));
            result.AffectedCount = plan.Count;
            return result;
        }

        var applied = 0;
        using var tx = RevitCompat.StartTransaction(document, "DHCB - Áp kích thước");
        foreach (var (curve, mm) in plan)
        {
            try
            {
                var ft = RevitCompat.MmToFt(mm);
                Parameter? target = curve is Pipe
                    ? curve.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)
                    : curve.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM) is { HasValue: true } d && d.AsDouble() > 0
                        ? d
                        : curve.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                if (target == null || target.IsReadOnly)
                {
                    result.Messages.Add($"{curve.Id}: tham số kích thước chỉ đọc — bỏ qua.");
                    continue;
                }

                target.Set(ft);
                applied++;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{curve.Id}: {ex.Message}");
            }
        }

        tx.Commit();
        result.Summary = $"Đã áp kích thước cho {applied}/{plan.Count} đoạn.";
        result.AffectedCount = applied;
        return result;
    }
}
