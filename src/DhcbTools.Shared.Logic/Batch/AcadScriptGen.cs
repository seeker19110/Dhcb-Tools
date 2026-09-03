using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Logic.Batch
{
    /// <summary>
    /// Sinh script <c>.scr</c> cho <c>accoreconsole.exe</c> (AutoCAD Core Console — chạy không UI, có sẵn trong mọi bản
    /// AutoCAD) để batch DWG: NETLOAD plugin → gọi lệnh <c>DHCB_RUN</c> với JSON step → SAVEAS/QUIT.
    /// Đây là cách batch AutoCAD offline không cần license bổ sung. Thuần chuỗi, test được.
    /// </summary>
    public static class AcadScriptGen
    {
        /// <summary>
        /// Một dòng script cho mỗi step. JSON được ghi ra file riêng (đường dẫn truyền vào lệnh) vì dòng lệnh
        /// AutoCAD không chịu dấu nháy/kí tự đặc biệt tốt; DHCB_RUN đọc file đó.
        /// </summary>
        public static string Build(string pluginDllPath, IReadOnlyList<string> stepJsonPaths, string? saveAsPath, string runLogPath, string sourceFile, string? plotScript = null)
        {
            if (string.IsNullOrWhiteSpace(pluginDllPath))
            {
                throw new ArgumentException("Thiếu đường dẫn DhcbTools.AutoCAD.dll.", nameof(pluginDllPath));
            }

            var sb = new StringBuilder();
            sb.Append("FILEDIA 0\n");
            sb.Append("SECURELOAD 0\n");
            sb.Append("NETLOAD \"").Append(Escape(pluginDllPath)).Append("\"\n");
            foreach (var stepPath in stepJsonPaths)
            {
                // MỖI THAM SỐ MỘT DÒNG. Trong script AutoCAD, một dòng = một lần Enter, tức một câu trả
                // lời cho một prompt; DHCB_RUN hỏi ba lần (step JSON, run.jsonl, file nguồn). Bản cũ viết
                // cả ba trên cùng một dòng nên toàn bộ phần còn lại của dòng bị nuốt vào prompt ĐẦU TIÊN,
                // accoreconsole báo "The filename, directory name, or volume label syntax is incorrect"
                // và batch AutoCAD chưa từng chạy trọn một lần nào. Lộ ra khi chạy thật trên AutoCAD 2026
                // ngày 2026-09-03 — cùng họ với lỗi journal của Revit ở giai đoạn 8.4.
                // Không bọc nháy: GetString(AllowSpaces = true) nhận nguyên dòng, nháy sẽ thành ký tự thật.
                sb.Append("DHCB_RUN\n");
                sb.Append(Escape(stepPath)).Append('\n');
                sb.Append(Escape(runLogPath)).Append('\n');
                sb.Append(Escape(sourceFile)).Append('\n');
            }

            if (!string.IsNullOrEmpty(plotScript))
            {
                sb.Append(plotScript);
            }

            if (!string.IsNullOrEmpty(saveAsPath))
            {
                sb.Append("SAVEAS 2018 \"").Append(Escape(saveAsPath!)).Append("\"\n");
            }

            sb.Append("FILEDIA 1\n");
            sb.Append("QUIT Y\n");
            return sb.ToString();
        }

        /// <summary>
        /// Bước in PDF không hộp thoại bằng <c>-PLOT</c> (mục 7.13, thay batch plot): layout, thiết bị "DWG To PDF.pc3",
        /// khổ giấy, hướng, tỉ lệ Fit, vùng in Extents/Layout, plot style. Mỗi tham số một dòng theo đúng thứ tự prompt của
        /// AutoCAD 2018+. Layout rỗng = "Model".
        /// </summary>
        public static string PlotPdf(string outputPdfPath, string layout = "Model", string paperSize = "ISO A3 (420.00 x 297.00 MM)",
            string orientation = "Landscape", string plotArea = "Extents", string plotStyle = "monochrome.ctb", string device = "DWG To PDF.pc3")
        {
            if (string.IsNullOrWhiteSpace(outputPdfPath))
            {
                throw new ArgumentException("Thiếu đường dẫn PDF.", nameof(outputPdfPath));
            }

            var isModel = string.IsNullOrEmpty(layout) || layout.Equals("Model", StringComparison.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            sb.Append("-PLOT\n");
            sb.Append("Y\n");                                   // Detailed plot configuration? Yes
            sb.Append(isModel ? "Model\n" : Escape(layout) + "\n"); // layout name
            sb.Append(Escape(device)).Append("\n");              // output device
            sb.Append(Escape(paperSize)).Append("\n");           // paper size
            sb.Append("M\n");                                   // paper units: Millimeters
            sb.Append(Escape(orientation)).Append("\n");         // orientation
            sb.Append("N\n");                                   // plot upside down? No
            sb.Append(Escape(plotArea)).Append("\n");            // plot area: Extents / Layout / Display
            sb.Append("Fit\n");                                 // scale
            sb.Append("Center\n");                              // plot offset
            sb.Append("Y\n");                                   // plot with plot styles
            sb.Append(Escape(plotStyle)).Append("\n");           // plot style table
            sb.Append("Y\n");                                   // plot with lineweights
            if (!isModel)
            {
                sb.Append("N\n");                               // scale lineweights with plot scale? No
                sb.Append("N\n");                               // plot paper space first? No
                sb.Append("N\n");                               // hide paperspace objects? No
            }
            else
            {
                sb.Append("A\n");                               // shade plot: As displayed
            }
            sb.Append(Escape(outputPdfPath)).Append("\n");       // file name
            sb.Append("N\n");                                   // save changes to page setup? No
            sb.Append("Y\n");                                   // proceed with plot
            return sb.ToString();
        }

        /// <summary>Dòng lệnh accoreconsole cho một file.</summary>
        public static string CommandLine(string accoreconsolePath, string dwgPath, string scriptPath, string? locale = "en-US")
        {
            return "\"" + accoreconsolePath + "\" /i \"" + dwgPath + "\" /s \"" + scriptPath + "\"" + (string.IsNullOrEmpty(locale) ? string.Empty : " /l " + locale);
        }

        /// <summary>Nội dung file JSON mô tả một step cho DHCB_RUN: {"command":..., "config":{...}}.</summary>
        public static string StepJson(string command, string configJson)
        {
            return "{\"command\":" + JsonConvert.ToString(command) + ",\"config\":" + (string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson) + "}";
        }

        private static string Escape(string path) => path.Replace("\"", string.Empty);
    }
}
