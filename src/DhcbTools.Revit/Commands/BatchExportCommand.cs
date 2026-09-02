using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DhcbTools.Core.Export;

namespace DhcbTools.Revit.Commands;

/// <summary>
/// Vỏ desktop cho <see cref="Core.Export.BatchExportCommand"/>: chọn thư mục rồi xuất PDF + DWG.
/// Dùng WPF VistaFolderBrowserDialog thông qua win32 BROWSEINFO (fallback: Documents subfolder).
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class BatchExportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument.Document;

        // Propose default output folder in Documents
        string defaultFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "DHCB_Export",
            SanitizeFileName(doc.Title ?? "project"));

        // Ask user via TaskDialog with custom input
        string? outputFolder = PromptForFolder(defaultFolder);
        if (outputFolder == null)
            return Result.Cancelled;

        var config = new ExportConfig
        {
            OutputFolder = outputFolder,
            Formats = new List<ExportFormat> { ExportFormat.Pdf, ExportFormat.Dwg },
        };

        var coreCmd = new Core.Export.BatchExportCommand();
        var result = coreCmd.Execute(doc, config);

        Feedback.Show("Xuất file hàng loạt", result);
        return result.Success ? Result.Succeeded : Result.Failed;
    }

    /// <summary>
    /// Show a TaskDialog asking user to confirm/change the output folder.
    /// Returns null if cancelled.
    /// </summary>
    private static string? PromptForFolder(string defaultFolder)
    {
        var dialog = new TaskDialog("DHCB Tools - Xuất file hàng loạt")
        {
            MainInstruction = "Chọn thư mục xuất file",
            MainContent = "File sẽ được xuất vào thư mục sau.\n\n" + defaultFolder +
                          "\n\nNhấn OK để xuất vào thư mục trên, hoặc Cancel để huỷ.",
            CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
            DefaultButton = TaskDialogResult.Ok,
        };

        var taskResult = dialog.Show();
        if (taskResult != TaskDialogResult.Ok)
            return null;

        // Ensure folder exists
        if (!Directory.Exists(defaultFolder))
        {
            try { Directory.CreateDirectory(defaultFolder); }
            catch (System.Exception ex)
            {
                TaskDialog.Show("Lỗi", "Không tạo được thư mục: " + ex.Message);
                return null;
            }
        }

        return defaultFolder;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "project";
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }
        return new string(chars).Trim();
    }
}
