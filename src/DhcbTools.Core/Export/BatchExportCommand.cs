using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;

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
                    $"[DryRun] Tìm thấy {sheets.Count} bản vẽ, {config.Formats.Count} định dạng.",
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
    private int ExportPdf(Document doc, List<ViewSheet> sheets,
        ExportConfig config, string projectNumber)
    {
        var sheetIds = sheets.Select(s => s.Id).ToList();

        var opts = new PDFExportOptions
        {
            PaperFormat = ExportPaperFormat.ISO_A1,
            ColorDepth = ColorDepthType.Color,
            ZoomType = ZoomType.Zoom,
            ZoomPercentage = 100,
            Combine = false,   // one file per sheet
            FileName = "batch_export",   // will be overridden per-sheet if Combine=false not valid
        };

        // Revit 2022+ bulk export: one call exports all sheets
        doc.Export(config.OutputFolder, sheetIds, opts);
        return sheets.Count;
    }

    // ---- DWG -----------------------------------------------------------------
    private int ExportDwg(Document doc, List<ViewSheet> sheets,
        ExportConfig config, string projectNumber)
    {
        var opts = new DWGExportOptions();

        // Map version string to ACADVersion enum
        ACADVersion acadVer;
        if (!TryParseAcadVersion(config.DwgVersion, out acadVer))
            acadVer = ACADVersion.R2018;
        opts.FileVersion = acadVer;

        var sheetIds = sheets.Select(s => s.Id).ToList();

        // Export: Document.Export(folder, filename-base, viewIds, dwgOpts)
        doc.Export(config.OutputFolder, string.Empty, sheetIds, opts);
        return sheets.Count;
    }

    // ---- IFC -----------------------------------------------------------------
    private int ExportIfc(Document doc, ExportConfig config)
    {
        var opts = new IFCExportOptions();

        IFCVersion ifcVer;
        if (!TryParseIfcVersion(config.IfcVersion, out ifcVer))
            ifcVer = IFCVersion.IFC2x3;
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
    {
        return pattern
            .Replace("{SheetNumber}", SanitizeFileName(sheet.SheetNumber))
            .Replace("{SheetName}", SanitizeFileName(sheet.Name))
            .Replace("{ProjectNumber}", SanitizeFileName(projectNumber));
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "unnamed";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }
        return new string(chars).Trim();
    }

    private static bool TryParseAcadVersion(string version, out ACADVersion result)
    {
        // ACADVersion enum values: R2013, R2018, etc.
        if (version == null) { result = ACADVersion.R2018; return false; }
        if (version.IndexOf("2013", StringComparison.Ordinal) >= 0) { result = ACADVersion.R2013; return true; }
        if (version.IndexOf("2018", StringComparison.Ordinal) >= 0) { result = ACADVersion.R2018; return true; }
        if (version.IndexOf("2010", StringComparison.Ordinal) >= 0) { result = ACADVersion.R2010; return true; }
        if (version.IndexOf("2007", StringComparison.Ordinal) >= 0) { result = ACADVersion.R2007; return true; }
        result = ACADVersion.R2018;
        return false;
    }

    private static bool TryParseIfcVersion(string version, out IFCVersion result)
    {
        if (version == null) { result = IFCVersion.IFC2x3; return false; }
        if (version.IndexOf("4", StringComparison.Ordinal) >= 0) { result = IFCVersion.IFC4; return true; }
        result = IFCVersion.IFC2x3;
        return true;
    }
}
