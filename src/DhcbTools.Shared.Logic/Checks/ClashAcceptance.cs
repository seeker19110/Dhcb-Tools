using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace DhcbTools.Shared.Logic.Checks
{
    /// <summary>Một va chạm đã được kỹ sư chấp nhận (mục 4.3) — khoá ổn định qua các lần chạy đêm.</summary>
    public sealed class AcceptedClash
    {
        [JsonProperty("key")]
        public string Key { get; set; } = string.Empty;

        [JsonProperty("note")]
        public string? Note { get; set; }

        [JsonProperty("acceptedAt")]
        public DateTime AcceptedAt { get; set; } = DateTime.Now;

        [JsonProperty("by")]
        public string? By { get; set; }
    }

    /// <summary>
    /// Khoá va chạm = cặp ElementId (sắp thứ tự) + vị trí tâm giao làm tròn theo lưới <see cref="PositionGridMm"/>,
    /// để cùng một cặp phần tử va chạm ở cùng chỗ luôn ra một khoá dù chạy lại nhiều lần; phần tử bị dời đi chỗ
    /// khác thì khoá đổi và va chạm được báo lại (đúng ý mục 4.3).
    /// </summary>
    public static class ClashAcceptance
    {
        public const double PositionGridMm = 100.0;

        public static string MakeKey(long idA, long idB, double xMm, double yMm, double zMm)
        {
            var lo = Math.Min(idA, idB);
            var hi = Math.Max(idA, idB);
            return lo.ToString(CultureInfo.InvariantCulture) + "-" + hi.ToString(CultureInfo.InvariantCulture)
                   + "@" + Snap(xMm) + "," + Snap(yMm) + "," + Snap(zMm);
        }

        private static string Snap(double v) => Math.Round(v / PositionGridMm).ToString(CultureInfo.InvariantCulture);

        public static HashSet<string> LoadKeys(string? path)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return set;
            }

            var list = JsonConvert.DeserializeObject<List<AcceptedClash>>(File.ReadAllText(path)) ?? new List<AcceptedClash>();
            foreach (var a in list.Where(a => !string.IsNullOrEmpty(a.Key)))
            {
                set.Add(a.Key);
            }
            return set;
        }

        public static void Save(string path, IEnumerable<AcceptedClash> accepted)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, JsonConvert.SerializeObject(accepted.ToList(), Formatting.Indented));
        }
    }
}
