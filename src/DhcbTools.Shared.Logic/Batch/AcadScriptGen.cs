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
        public static string Build(string pluginDllPath, IReadOnlyList<string> stepJsonPaths, string? saveAsPath, string runLogPath, string sourceFile)
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
                sb.Append("DHCB_RUN \"").Append(Escape(stepPath)).Append("\" \"").Append(Escape(runLogPath)).Append("\" \"").Append(Escape(sourceFile)).Append("\"\n");
            }

            if (!string.IsNullOrEmpty(saveAsPath))
            {
                sb.Append("SAVEAS 2018 \"").Append(Escape(saveAsPath!)).Append("\"\n");
            }

            sb.Append("FILEDIA 1\n");
            sb.Append("QUIT Y\n");
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
