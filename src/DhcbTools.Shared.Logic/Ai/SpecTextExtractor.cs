using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Logic.Ai
{
    /// <summary>Tầng trích được từ thuyết minh.</summary>
    public sealed class ExtractedLevel
    {
        public ExtractedLevel(string name, double elevationMm, string sourceLine)
        {
            Name = name;
            ElevationMm = elevationMm;
            SourceLine = sourceLine;
        }

        public string Name { get; }

        public double ElevationMm { get; }

        /// <summary>Đoạn văn bản gốc — hiển thị cho kỹ sư đối chiếu (mục 5.2: "kèm đoạn văn bản gốc, không tự đoán").</summary>
        public string SourceLine { get; }
    }

    public sealed class SpecExtraction
    {
        public List<ExtractedLevel> Levels { get; } = new List<ExtractedLevel>();

        public List<string> Systems { get; } = new List<string>();

        public List<string> Standards { get; } = new List<string>();

        public string? ProjectName { get; set; }

        public string? ProjectNumber { get; set; }

        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Config đúng schema <c>LevelSetupConfig</c> + <c>ProjectInfoConfig</c> mà ProjectInit nhận.</summary>
        public string ToProjectInitJson()
        {
            var obj = new JObject
            {
                ["levelSetup"] = new JObject
                {
                    ["dryRun"] = true,
                    ["skipExisting"] = true,
                    ["levels"] = new JArray(Levels.Select(l => new JObject
                    {
                        ["name"] = l.Name,
                        ["elevationMm"] = l.ElevationMm,
                        ["createFloorPlan"] = true,
                    })),
                },
                ["projectInfo"] = new JObject
                {
                    ["projectName"] = ProjectName,
                    ["projectNumber"] = ProjectNumber,
                    ["dryRun"] = true,
                },
                ["systems"] = new JArray(Systems),
                ["standards"] = new JArray(Standards),
                ["warnings"] = new JArray(Warnings),
            };
            return obj.ToString(Formatting.Indented);
        }
    }

    /// <summary>
    /// Trích cao độ tầng, hệ thống, tiêu chuẩn từ văn bản thuyết minh/spec (mục 5.2) — offline, bằng regex tiếng Việt/Anh.
    /// Nhận văn bản thuần (PDF được đổi sang text bên ngoài bằng <c>pdftotext</c>/<c>scripts/dhcb_ai.py</c>).
    /// Không đoán: dòng nào không khớp mẫu rõ ràng thì bỏ và ghi Warning.
    /// </summary>
    public static class SpecTextExtractor
    {
        // "Tầng 1: +0.000", "Tầng 2 +3.600 m", "Level 3 = 7200", "T3 … +7.20", "Tầng hầm 1 -3.300", "Tầng kỹ thuật +45.5"
        private static readonly Regex LevelLine = new Regex(
            @"(?<name>(?:t[aầ]ng\s*(?:h[aầ]m|k[yỹ]\s*thu[aậ]t|m[aá]i|tr[eệ]t|l[uử]ng)?\s*\d*|level\s*\d+|L\d{1,2}|B\d{1,2}|T\d{1,2}|m[aá]i|s[aâ]n th[uư][oợ]ng|tr[eệ]t))\s*[:=\-–]?\s*(?:cao\s*đ[oộ]|elev(?:ation)?|EL\.?)?\s*[:=]?\s*(?<sign>[+\-−])?\s*(?<num>\d{1,6}(?:[.,]\d{1,3})?)\s*(?<unit>m|mm)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] SystemKeywords =
        {
            "HVAC", "điều hòa", "điều hoà", "thông gió", "cấp nước", "thoát nước", "chữa cháy", "sprinkler", "PCCC", "điện nhẹ", "điện động lực", "chiếu sáng",
            "chống sét", "tiếp địa", "BMS", "camera", "CCTV", "báo cháy", "cấp khí", "gas", "hút mùi", "tăng áp", "hút khói",
        };

        private static readonly Regex StandardPattern = new Regex(
            @"\b(TCVN\s*\d{3,5}(?:[-:]\d{2,4})?|QCVN\s*\d{2}(?:[:/]\d{4})?(?:/BXD|/BCA)?|ASHRAE\s*\d{2,3}(?:\.\d)?|NFPA\s*\d{1,3}|SMACNA|BS\s*EN\s*\d{3,5}|ISO\s*\d{3,5}|IEC\s*\d{4,5})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ProjectName = new Regex(@"(?:t[eê]n\s*d[uự]\s*[aá]n|d[uự]\s*[aá]n|project(?:\s*name)?)\s*[:\-–]\s*(?<v>[^\r\n]{3,120})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ProjectNumber = new Regex(@"(?:m[aã]\s*(?:s[oố]\s*)?d[uự]\s*[aá]n|s[oố]\s*h[iợ]p\s*đ[oồ]ng|project\s*(?:no|number|code)\.?)\s*[:\-–]\s*(?<v>[A-Za-z0-9\-/_.]{2,40})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static SpecExtraction Extract(string text)
        {
            var result = new SpecExtraction();
            if (string.IsNullOrWhiteSpace(text))
            {
                result.Warnings.Add("Văn bản rỗng.");
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                foreach (Match m in LevelLine.Matches(line))
                {
                    var name = NormalizeLevelName(m.Groups["name"].Value);
                    var numText = m.Groups["num"].Value.Replace(',', '.');
                    if (!double.TryParse(numText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        continue;
                    }

                    var unit = m.Groups["unit"].Value;
                    // Không có đơn vị: |giá trị| < 100 coi là mét (cao độ 3.6, 45.5), còn lại là mm.
                    var mm = unit.Equals("mm", StringComparison.OrdinalIgnoreCase) ? value
                        : unit.Equals("m", StringComparison.OrdinalIgnoreCase) ? value * 1000
                        : Math.Abs(value) < 100 ? value * 1000 : value;

                    var sign = m.Groups["sign"].Value;
                    if (sign == "-" || sign == "−")
                    {
                        mm = -mm;
                    }

                    if (!seen.Add(name))
                    {
                        result.Warnings.Add("Tầng \"" + name + "\" xuất hiện nhiều lần — giữ lần đầu. Dòng: " + line);
                        continue;
                    }

                    result.Levels.Add(new ExtractedLevel(name, Math.Round(mm, 1), line));
                }

                foreach (var kw in SystemKeywords)
                {
                    if (line.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 && !result.Systems.Contains(kw, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Systems.Add(kw);
                    }
                }

                foreach (Match m in StandardPattern.Matches(line))
                {
                    var std = Regex.Replace(m.Value.ToUpperInvariant(), @"\s+", " ");
                    if (!result.Standards.Contains(std))
                    {
                        result.Standards.Add(std);
                    }
                }

                if (result.ProjectName == null)
                {
                    var pn = ProjectName.Match(line);
                    if (pn.Success)
                    {
                        result.ProjectName = pn.Groups["v"].Value.Trim().TrimEnd('.', ';');
                    }
                }

                if (result.ProjectNumber == null)
                {
                    var pn = ProjectNumber.Match(line);
                    if (pn.Success)
                    {
                        result.ProjectNumber = pn.Groups["v"].Value.Trim();
                    }
                }
            }

            result.Levels.Sort((a, b) => a.ElevationMm.CompareTo(b.ElevationMm));
            if (result.Levels.Count == 0)
            {
                result.Warnings.Add("Không tìm thấy dòng cao độ tầng nào theo mẫu \"Tầng N: +x.xxx\" / \"Level N = x\".");
            }

            for (var i = 1; i < result.Levels.Count; i++)
            {
                var gap = result.Levels[i].ElevationMm - result.Levels[i - 1].ElevationMm;
                if (gap < 2000 || gap > 8000)
                {
                    result.Warnings.Add("Chiều cao tầng giữa \"" + result.Levels[i - 1].Name + "\" và \"" + result.Levels[i].Name + "\" là " + NumericText.Format(gap, 0) + " mm — kiểm tra lại.");
                }
            }

            return result;
        }

        /// <summary>"tầng 3" / "Tang 3" / "L3" / "Level 3" → "Tầng 3"; "tầng hầm 1"/"B1" → "Tầng hầm 1"; "mái" → "Mái".</summary>
        public static string NormalizeLevelName(string raw)
        {
            var t = Regex.Replace(raw.Trim(), @"\s+", " ");
            var lower = LayerMappingSuggester.RemoveDiacritics(t).ToLowerInvariant();

            var digits = Regex.Match(lower, @"\d+").Value;
            if (lower.StartsWith("tang ham") || (lower.StartsWith("b") && digits.Length > 0 && lower.Length <= 3))
            {
                return "Tầng hầm " + digits;
            }

            if (lower.StartsWith("tang ky thuat"))
            {
                return "Tầng kỹ thuật" + (digits.Length > 0 ? " " + digits : string.Empty);
            }

            if (lower.StartsWith("tang mai") || lower == "mai")
            {
                return "Mái";
            }

            if (lower.StartsWith("san thuong"))
            {
                return "Sân thượng";
            }

            if (lower.StartsWith("tang tret") || lower == "tret")
            {
                return "Tầng trệt";
            }

            if (lower.StartsWith("tang lung"))
            {
                return "Tầng lửng";
            }

            if (digits.Length > 0)
            {
                return "Tầng " + digits.TrimStart('0').PadLeft(1, '0');
            }

            return t;
        }
    }
}
