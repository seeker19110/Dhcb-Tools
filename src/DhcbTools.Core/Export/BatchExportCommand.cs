using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.Export;

/// <summary>
/// Xuất file hàng loạt: PDF, DWG, IFC, NWC.
/// Implements <see cref="ICoreCommand{ExportConfig}"/>.
/// </summary>
public sealed class BatchExportCommand : ICoreCommand<ExportConfig>
{
    public string CommandName => "BatchExport";

    public CommandResult Execute(Document document, ExportConfig config)
    {
        try
        {
            if (!Directory.Exists(config.OutputFolder))
                Directory.CreateDirectory(config.OutputFolder);

            // Collect sheets
            var allSheets = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();

            List<ViewSheet> sheets;
            if (config.SheetNumbers != null && config.SheetNumbers.Count > 0)
            {
                sheets = allSheets
                    .Where(s => config.SheetNumbers.Contains(s.SheetNumber))
                    .ToList();
            }
            else
            {
                sheets = allSheets;
            }

            if (sheets.Count == 0)
                return CommandResult.Fail("Không tìm thấy bản vẽ nào phù hợp để xuất.");

            if (config.DryRun)
            {
                var dryResult = CommandResult.Ok(
                    $"[Xem trước] Tìm thấy {sheets.Count} bản vẽ, {config.Formats.Count} định dạng.",
                    sheets.Count);
                foreach (var s in sheets)
                    dryResult.Messages.Add($"  {s.SheetNumber} - {s.Name}");
                return dryResult;
            }

            var projectNumber = document.ProjectInformation?.Number ?? string.Empty;
            int totalExported = 0;
            var errors = new List<string>();

            foreach (var format in config.Formats)
            {
                try
                {
                    int count = ExportFormat(document, sheets, format, config, projectNumber);
                    totalExported += count;
                }
                catch (System.Exception ex)
                {
                    errors.Add($"[{format}] {ex.Message}");
                }
            }

            string summary = $"Xuất xong {totalExported} file(s) ({sheets.Count} bản vẽ × {config.Formats.Count} định dạng).";
            if (errors.Count > 0)
                summary += $" {errors.Count} lỗi.";

            var result = CommandResult.Ok(summary, totalExported);
            result.Errors.AddRange(errors);
            return result;
        }
        catch (System.Exception ex)
        {
            return CommandResult.Fail($"BatchExport thất bại: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    private int ExportFormat(Document doc, List<ViewSheet> sheets,
        ExportFormat format, ExportConfig config, string projectNumber)
    {
        switch (format)
        {
            case Export.ExportFormat.Pdf:
                return ExportPdf(doc, sheets, config, projectNumber);
            case Export.ExportFormat.Dwg:
                return ExportDwg(doc, sheets, config, projectNumber);
            case Export.ExportFormat.Ifc:
                return ExportIfc(doc, config);
            case Export.ExportFormat.Nwc:
                return ExportNwc(doc, config);
            default:
                throw new System.NotSupportedException($"Định dạng chưa hỗ trợ: {format}");
        }
    }

    // ---- PDF -----------------------------------------------------------------
    // Xuất từng sheet một với Combine=true + FileName: đó là cách duy nhất để Revit đặt tên file theo
    // mẫu của mình. Gọi bulk Export(folder, ids, opts) thì Revit tự đặt "Sheet-<tên>.pdf", bỏ qua
    // FileNamePattern và hai sheet trùng tên ghi đè nhau (lộ ra khi chạy thật trên Revit 2024).
    private int ExportPdf(Document doc, List<ViewSheet> sheets,
        ExportConfig config, string projectNumber)
    {
#if !REVIT2023_OR_GREATER
        // PDFExportOptions chỉ có từ Revit 2022; Directory.Build.props không có hằng riêng cho 2022 nên
        // dùng mốc 2023 — bản 2021/2022 báo rõ thay vì lỗi biên dịch/MissingMethodException lúc chạy.
        throw new NotSupportedException("Xuất PDF cần Revit 2023 trở lên (API PDFExportOptions). Dùng in PDF thủ công hoặc xuất DWG.");
#else
        var used = new HashSet<string>();
        int count = 0;
        foreach (var sheet in sheets)
        {
            string name = FileNaming.MakeUnique(ApplyPattern(config.FileNamePattern, sheet, projectNumber), used);
            var opts = new PDFExportOptions
            {
                PaperFormat = ExportPaperFormat.ISO_A1,
                ColorDepth = ColorDepthType.Color,
                ZoomType = ZoomType.Zoom,
                ZoomPercentage = 100,
                Combine = true,
                FileName = name,
            };
            if (doc.Export(config.OutputFolder, new List<ElementId> { sheet.Id }, opts))
                count++;
        }
        return count;
#endif
    }

    // ---- DWG -----------------------------------------------------------------
    private int ExportDwg(Document doc, List<ViewSheet> sheets,
        ExportConfig config, string projectNumber)
    {
        // Map version string to ACADVersion enum
        if (!TryParseAcadVersion(config.DwgVersion, out var acadVer))
        {
            throw new NotSupportedException(
                $"Không nhận ra phiên bản DWG \"{config.DwgVersion}\". Dùng dạng \"AcadRelease2018\" hoặc \"2013\".");
        }

        var used = new HashSet<string>();
        int count = 0;
        foreach (var sheet in sheets)
        {
            string name = FileNaming.MakeUnique(ApplyPattern(config.FileNamePattern, sheet, projectNumber), used);
            var opts = new DWGExportOptions
            {
                FileVersion = acadVer,
                // Gộp view trên sheet vào một DWG; mặc định Revit tách mỗi view thành một DWG xref riêng.
                MergedViews = true,
            };
            // Xuất một view với tên cụ thể → Revit ghi "<name>.dwg". Xuất cả lô với tên rỗng thì Revit
            // tự đặt "<Tên dự án>-Sheet - <số> - <tên>.dwg", bỏ qua FileNamePattern.
            var before = SnapshotDwgFiles(config.OutputFolder);
            if (doc.Export(config.OutputFolder, name, new List<ElementId> { sheet.Id }, opts))
            {
                count++;
                NormalizeDwgName(config.OutputFolder, name, before);
            }
        }
        return count;
    }

    private static HashSet<string> SnapshotDwgFiles(string folder) =>
        new HashSet<string>(Directory.GetFiles(folder, "*.dwg"), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Một số bản Revit vẫn nối "-Sheet - ..." vào tên khi xuất DWG dù chỉ một view; đổi về đúng "&lt;name&gt;.dwg".
    /// Nhận diện file bằng cách so danh sách .dwg trước/sau lần xuất (đúng file Revit vừa ghi) thay vì
    /// glob "name-*.dwg" — glob đó khiến sheet "A-1" vớ nhầm file của "A-1-XX".
    /// </summary>
    private static void NormalizeDwgName(string folder, string name, HashSet<string> before)
    {
        string wanted = Path.Combine(folder, name + ".dwg");
        if (File.Exists(wanted)) return;
        var created = Directory.GetFiles(folder, "*.dwg").Where(f => !before.Contains(f)).ToList();
        if (created.Count == 1)
        {
            File.Move(created[0], wanted);
            string pcp = Path.ChangeExtension(created[0], ".pcp");
            if (File.Exists(pcp)) File.Move(pcp, Path.Combine(folder, name + ".pcp"));
        }
    }

    // ---- IFC -----------------------------------------------------------------
    private int ExportIfc(Document doc, ExportConfig config)
    {
        var opts = new IFCExportOptions();

        if (!TryParseIfcVersion(config.IfcVersion, out var ifcVer))
        {
            throw new NotSupportedException(
                $"Không nhận ra phiên bản IFC \"{config.IfcVersion}\". Dùng \"IFC2x3\" hoặc \"IFC4\".");
        }
        opts.FileVersion = ifcVer;
        opts.ExportBaseQuantities = true;

        string fileName = SanitizeFileName(doc.Title ?? "export") + ".ifc";
        doc.Export(config.OutputFolder, fileName, opts);
        return 1;
    }

    // ---- NWC -----------------------------------------------------------------
    private int ExportNwc(Document doc, ExportConfig config)
    {
        var opts = new NavisworksExportOptions
        {
            ExportScope = NavisworksExportScope.Model,
        };

        string fileName = SanitizeFileName(doc.Title ?? "export") + ".nwc";
        doc.Export(config.OutputFolder, fileName, opts);
        return 1;
    }

    // ---- Helpers -------------------------------------------------------------
    private string ApplyPattern(string pattern, ViewSheet sheet, string projectNumber)
        => FileNaming.ApplyPattern(pattern, sheet.SheetNumber, sheet.Name, projectNumber);

    private static string SanitizeFileName(string name) => FileNaming.Sanitize(name);

    private static bool TryParseAcadVersion(string version, out ACADVersion result)
    {
        // Ánh xạ chuỗi → tên hằng nằm ở DhcbTools.Shared.Logic (test được không cần Revit);
        // ở đây chỉ đổi tên hằng thành giá trị enum của Revit API.
        var known = ExportVersionMap.TryParseAcadVersion(version, out var enumName);
        result = Enum.TryParse(enumName, out ACADVersion parsed) ? parsed : ACADVersion.R2018;
        return known;
    }

    private static bool TryParseIfcVersion(string version, out IFCVersion result)
    {
        var known = ExportVersionMap.TryParseIfcVersion(version, out var enumName);
        result = Enum.TryParse(enumName, out IFCVersion parsed) ? parsed : IFCVersion.IFC2x3;
        return known;
    }
}
