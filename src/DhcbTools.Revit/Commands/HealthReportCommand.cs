using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DhcbTools.Core.Health;

namespace DhcbTools.Revit.Commands;

/// <summary>
/// Vỏ desktop cho <see cref="Core.Health.HealthReportCommand"/>: tạo báo cáo HTML rồi mở browser.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class HealthReportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = CommandRunner.RequireDocument(commandData, "Health Report");
        if (doc is null)
        {
            return Result.Cancelled;
        }

        string outputPath = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
            $"DHCB_Health_{SanitizeFileName(doc.Title)}_{DateTime.Now:yyyyMMdd_HHmm}.html");

        var config = new HealthReportConfig { OutputPath = outputPath };
        var coreCmd = new Core.Health.HealthReportCommand();
        var result = coreCmd.Execute(doc, config);

        if (result.Success)
        {
            // .NET (Revit 2025+): Process.Start(string) mặc định UseShellExecute=false → mở .html ném Win32Exception
            // và bị nuốt — báo cáo có nhưng browser không mở, kỹ sư tưởng lệnh không làm gì.
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(outputPath) { UseShellExecute = true }); }
            catch (System.Exception ex)
            {
                DhcbLog.Write("Revit", "Không mở được báo cáo trong browser: " + ex.Message);
                result.Messages.Add("Không tự mở được browser — mở tay file: " + outputPath);
            }
        }

        Feedback.Show("Health Report", result);
        return result.Success ? Result.Succeeded : Result.Failed;
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
