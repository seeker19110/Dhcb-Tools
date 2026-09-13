using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Families
{
    /// <summary>Báo cáo chuẩn hóa tham số Shared Parameter theo tiêu chuẩn IFC và NĐ 207.</summary>
    public sealed class ParameterNormalizationResult
    {
        public ParameterNormalizationResult(
            int totalParametersChecked,
            int normalizedCount,
            IReadOnlyList<(string OriginalName, string StandardizedName, string Reason)> changes)
        {
            TotalParametersChecked = totalParametersChecked;
            NormalizedCount = normalizedCount;
            Changes = changes ?? Array.Empty<(string, string, string)>();
        }

        public int TotalParametersChecked { get; }
        public int NormalizedCount { get; }
        public IReadOnlyList<(string OriginalName, string StandardizedName, string Reason)> Changes { get; }
    }

    /// <summary>Engine chuẩn hóa tên tham số mô hình sang chuẩn IFC/NĐ 207.</summary>
    public static class ParameterNormalizationEngine
    {
        private static readonly Dictionary<string, string> StandardMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Ten_Thiet_Bi", "Pset_Equipment:Name" },
            { "Ma_Hieu", "Pset_Equipment:Tag" },
            { "Nha_San_Xuat", "Pset_Manufacturer:Manufacturer" },
            { "Cong_Suat_KW", "Pset_ElectricalDevice:PowerRating" },
            { "Cao_Do_Day", "Pset_Pipe:BottomElevation" }
        };

        public static ParameterNormalizationResult NormalizeParameters(IEnumerable<string> rawParameterNames)
        {
            if (rawParameterNames == null)
                return new ParameterNormalizationResult(0, 0, Array.Empty<(string, string, string)>());

            var rawList = rawParameterNames.ToList();
            var changes = new List<(string OriginalName, string StandardizedName, string Reason)>();

            foreach (var raw in rawList)
            {
                if (StandardMappings.TryGetValue(raw, out string? std))
                {
                    changes.Add((raw, std, "Chuyển đổi tên thuần sang thuộc tính Pset chuẩn hóa."));
                }
                else if (raw.Contains(" ") || raw.Contains("-"))
                {
                    string cleaned = raw.Replace(" ", "_").Replace("-", "_");
                    changes.Add((raw, cleaned, "Loại bỏ khoảng trắng và ký tự đặc biệt trong tên tham số."));
                }
            }

            return new ParameterNormalizationResult(
                totalParametersChecked: rawList.Count,
                normalizedCount: changes.Count,
                changes: changes);
        }
    }
}
