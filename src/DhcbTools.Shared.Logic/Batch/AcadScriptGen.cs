using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// Dòng "Unable to load … assembly." trong output của accoreconsole, hoặc <c>null</c>. Khi NETLOAD thất bại
        /// thì mọi <c>DHCB_RUN</c> sau đó là "Unknown command" và không dòng nào được ghi vào run.jsonl — runner dùng
        /// hàm này để biến "0 OK, 0 lỗi" câm thành một dòng lỗi có tên DLL (đóng vai kỹ sư AutoCAD, §57).
        /// </summary>
        public static string? NetloadFailure(string? consoleOutput)
        {
            if (string.IsNullOrEmpty(consoleOutput)) return null;
            foreach (var raw in consoleOutput!.Split('\n'))
            {
                var line = raw.Trim('\r', ' ');
                if (line.StartsWith("Unable to load", StringComparison.OrdinalIgnoreCase) && line.IndexOf("assembly", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return line;
                }
            }

            return null;
        }

        /// <summary>
        /// Một dòng script cho mỗi step. JSON được ghi ra file riêng (đường dẫn truyền vào lệnh) vì dòng lệnh
        /// AutoCAD không chịu dấu nháy/kí tự đặc biệt tốt; DHCB_RUN đọc file đó.
        /// </summary>
        /// <param name="pluginDllPath">DLL NETLOAD (ưu tiên DhcbTools.AutoCAD.Core.dll).</param>
        /// <param name="stepJsonPaths">File JSON từng step cho DHCB_RUN.</param>
        /// <param name="saveAsPath">Đường dẫn lưu; null = đóng không lưu. Với saveMode=Save truyền chính file nguồn
        /// (QSAVE không có trong core console — SAVEAS về cùng đường dẫn tương đương).</param>
        /// <param name="runLogPath">run.jsonl mà DHCB_RUN ghi vào.</param>
        /// <param name="sourceFile">File DWG nguồn (ghi vào cột file của log).</param>
        /// <param name="plotScript">Chuỗi -PLOT (từ <see cref="PlotPdf"/>) chèn trước SAVEAS, hoặc null.</param>
        /// <param name="dwgVersion">Từ khoá phiên bản DWG cho SAVEAS (2000/2004/2007/2010/2013/2018), mặc định 2018.</param>
        /// <param name="saveTargetExists">File đích đã tồn tại → AutoCAD hỏi "replace it?"; thêm dòng <c>Y</c> để trả lời.
        /// Không có dòng này thì prompt nuốt luôn lệnh kế tiếp và bản vẽ không được lưu. Với saveMode=Save luôn là true.</param>
        public static string Build(string pluginDllPath, IReadOnlyList<string> stepJsonPaths, string? saveAsPath, string runLogPath, string sourceFile,
            string? plotScript = null, string? dwgVersion = null, bool saveTargetExists = false)
        {
            if (string.IsNullOrWhiteSpace(pluginDllPath))
            {
                throw new ArgumentException("Thiếu đường dẫn DhcbTools.AutoCAD.dll.", nameof(pluginDllPath));
            }

            var sb = new StringBuilder();
            sb.Append("FILEDIA 0\n");
            // SECURELOAD 0 để NETLOAD nạp được DLL nằm ngoài TRUSTEDPATHS mà không có hộp thoại (core console
            // không có ai bấm "Always Load"). Chấp nhận được vì script này chỉ nạp đúng một DLL do runner chỉ
            // định, trong một tiến trình accoreconsole chạy riêng cho batch rồi thoát. Cách chặt hơn (SECURELOAD 1 +
            // ghi thư mục DLL vào TRUSTEDPATHS) đòi ghi đè biến hệ thống lưu theo profile của user — ảnh hưởng cả
            // AutoCAD tương tác — nên không làm ở đây.
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
                sb.Append("SAVEAS ").Append(NormalizeDwgVersion(dwgVersion)).Append(" \"").Append(Escape(saveAsPath!)).Append("\"\n");
                if (saveTargetExists)
                {
                    sb.Append("Y\n"); // "A drawing with this name already exists. Do you want to replace it?"
                }
            }

            sb.Append("FILEDIA 1\n");
            sb.Append("QUIT Y\n");
            return sb.ToString();
        }

        /// <summary>
        /// Bước in PDF không hộp thoại bằng <c>-PLOT</c> (mục 7.13, thay batch plot): layout, thiết bị "DWG To PDF.pc3",
        /// khổ giấy, hướng, tỷ lệ cấu hình, vùng in Extents/Layout, plot style. Mỗi tham số một dòng theo đúng thứ tự prompt của
        /// AutoCAD 2018+. Layout rỗng = "Model".
        /// </summary>
        public static string PlotPdf(string outputPdfPath, string layout = "Model", string paperSize = "ISO A3 (420.00 x 297.00 MM)",
            string orientation = "Landscape", string plotArea = "Extents", string plotStyle = "monochrome.ctb", string device = "DWG To PDF.pc3",
            string? plotScale = null, string? pageSetupName = null)
        {
            if (string.IsNullOrWhiteSpace(outputPdfPath))
            {
                throw new ArgumentException("Thiếu đường dẫn PDF.", nameof(outputPdfPath));
            }

            var isModel = string.IsNullOrEmpty(layout) || layout.Equals("Model", StringComparison.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            if (pageSetupName == null)
            {
                var area = plotArea.ToUpperInvariant();
                if (area != "EXTENTS" && area != "DISPLAY" && area != "LIMITS" && area != "LAYOUT")
                    throw new ArgumentException("plotArea hỗ trợ Extents/Display/Limits/ Layout; Window và View cần thêm prompt chưa hỗ trợ.");
                if ((isModel && area == "LAYOUT") || (!isModel && area == "LIMITS"))
                    throw new ArgumentException("Layout chỉ dùng với paper space; Limits chỉ dùng với Model.");
            }
            var scale = NormalizePlotScale(plotScale, isModel);
            if (pageSetupName != null && (string.IsNullOrWhiteSpace(pageSetupName) || pageSetupName.IndexOfAny(new[] { '\r', '\n' }) >= 0))
                throw new ArgumentException("Tên page setup không được rỗng hoặc có ký tự xuống dòng.", nameof(pageSetupName));
            sb.Append("-PLOT\n");
            if (pageSetupName != null)
            {
                sb.Append("N\n").Append(isModel ? "Model" : Escape(layout)).Append('\n');
                sb.Append(Escape(pageSetupName)).Append('\n').Append(Escape(device)).Append('\n');
                sb.Append(Escape(outputPdfPath)).Append("\nN\nY\n");
                return sb.ToString();
            }
            sb.Append("Y\n");                                   // Detailed plot configuration? Yes
            sb.Append(isModel ? "Model\n" : Escape(layout) + "\n"); // layout name
            sb.Append(Escape(device)).Append("\n");              // output device
            sb.Append(Escape(paperSize)).Append("\n");           // paper size
            sb.Append("M\n");                                   // paper units: Millimeters
            sb.Append(Escape(orientation)).Append("\n");         // orientation
            sb.Append("N\n");                                   // plot upside down? No
            sb.Append(Escape(plotArea)).Append("\n");            // plot area: Extents / Layout / Display
            sb.Append(scale).Append("\n");                       // scale
            sb.Append(plotArea.Equals("Layout", StringComparison.OrdinalIgnoreCase) ? "0,0\n" : "Center\n"); // paper origin
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

        /// <summary>Tỷ lệ mm trên giấy = đơn vị bản vẽ; Layout mặc định 1:1, Model giữ Fit.</summary>
        public static string NormalizePlotScale(string? scale, bool isModel)
        {
            if (scale == null) return isModel ? "Fit" : "1=1";
            scale = scale.Trim();
            if (scale.Equals("Fit", StringComparison.OrdinalIgnoreCase)) return "Fit";
            var parts = scale.Replace(':', '=').Split('=');
            if (parts.Length < 1 || parts.Length > 2) throw new ArgumentException("plotScale cần Fit, số dương hoặc tỷ lệ 1=100.");
            for (var i = 0; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                    || double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                    throw new ArgumentException("plotScale cần Fit, số dương hoặc tỷ lệ 1=100.");
                parts[i] = value.ToString("G", CultureInfo.InvariantCulture);
            }
            return string.Join("=", parts);
        }

        /// <summary>Core Console thường ghi UTF-16LE không BOM; host giả và bản khác có thể dùng UTF-8.</summary>
        public static string DecodeConsole(byte[] bytes)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            var pairs = Math.Min(bytes.Length / 2, 256);
            var oddZeros = 0;
            var evenZeros = 0;
            for (var i = 0; i < pairs; i++)
            {
                if (bytes[i * 2] == 0) evenZeros++;
                if (bytes[i * 2 + 1] == 0) oddZeros++;
            }
            if (pairs >= 4 && oddZeros > pairs / 2 && evenZeros < pairs / 4)
                return Encoding.Unicode.GetString(bytes);
            if (pairs >= 4 && evenZeros > pairs / 2 && oddZeros < pairs / 4)
                return Encoding.BigEndianUnicode.GetString(bytes);
            return Encoding.UTF8.GetString(bytes).TrimStart('\ufeff');
        }

        /// <summary>Từ khoá phiên bản DWG hợp lệ cho SAVEAS; sai/trống → 2018.</summary>
        public static string NormalizeDwgVersion(string? version)
        {
            var v = (version ?? string.Empty).Trim();
            switch (v)
            {
                case "2000":
                case "2004":
                case "2007":
                case "2010":
                case "2013":
                case "2018":
                    return v;
                default:
                    return "2018";
            }
        }

        /// <summary>Dòng lệnh accoreconsole cho một file (exe + tham số).</summary>
        public static string CommandLine(string accoreconsolePath, string dwgPath, string scriptPath, string? locale = "en-US")
        {
            return "\"" + Escape(accoreconsolePath) + "\" " + Arguments(dwgPath, scriptPath, locale);
        }

        /// <summary>
        /// Phần tham số cho <c>ProcessStartInfo.Arguments</c>: <c>/i "dwg" /s "scr" /l en-US</c>. Bọc nháy cả hai
        /// đường dẫn (có dấu cách là thường) và bỏ ký tự phá dòng lệnh; locale chỉ nhận chữ, số, gạch nối.
        /// </summary>
        public static string Arguments(string dwgPath, string scriptPath, string? locale = "en-US")
        {
            var sb = new StringBuilder();
            sb.Append("/i \"").Append(Escape(dwgPath)).Append("\" /s \"").Append(Escape(scriptPath)).Append('"');
            if (!string.IsNullOrEmpty(locale))
            {
                var clean = new StringBuilder();
                foreach (var c in locale!)
                {
                    if (char.IsLetterOrDigit(c) || c == '-' || c == '_') clean.Append(c);
                }

                if (clean.Length > 0) sb.Append(" /l ").Append(clean);
            }

            return sb.ToString();
        }

        /// <summary>Nội dung file JSON mô tả một step cho DHCB_RUN: {"command":..., "config":{...}}.</summary>
        public static string StepJson(string command, string configJson)
        {
            return "{\"command\":" + JsonConvert.ToString(command) + ",\"config\":" + (string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson) + "}";
        }

        /// <summary>
        /// Bỏ dấu nháy và ký tự xuống dòng khỏi mọi giá trị chèn vào script: một <c>\n</c> lọt vào tên layout
        /// hay đường dẫn là một lần Enter thừa — dòng còn lại của giá trị thành lệnh mới cho AutoCAD.
        /// </summary>
        private static string Escape(string value) =>
            (value ?? string.Empty).Replace("\"", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
    }
}
