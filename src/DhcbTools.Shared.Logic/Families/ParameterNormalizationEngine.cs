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

    /// <summary>
    /// Engine chuẩn hóa tên tham số mô hình sang chuẩn IFC/NĐ 207. Bảng ánh xạ là ĐẦU VÀO (từ điển dự án,
    /// <c>configs/dictionary</c>), không cứng trong mã — nguyên tắc 7 của roadmap. Bảng mặc định chỉ gồm
    /// vài thuộc tính có tên IFC4 chắc chắn, làm mẫu cho từ điển.
    /// </summary>
    public static class ParameterNormalizationEngine
    {
        /// <summary>Ánh xạ mẫu: tên thuần Việt → thuộc tính/Pset IFC4 có thật.</summary>
        public static readonly IReadOnlyDictionary<string, string> DefaultMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Ten_Thiet_Bi", "IfcRoot:Name" },
            { "Ma_Hieu", "IfcElement:Tag" },
            { "Nha_San_Xuat", "Pset_ManufacturerTypeInformation:Manufacturer" },
            { "Model_Thiet_Bi", "Pset_ManufacturerTypeInformation:ModelLabel" },
            { "Chong_Chay", "Pset_WallCommon:FireRating" },
        };

        public static ParameterNormalizationResult NormalizeParameters(
            IEnumerable<string?>? rawParameterNames,
            IReadOnlyDictionary<string, string>? mappings = null)
        {
            if (rawParameterNames == null)
                return new ParameterNormalizationResult(0, 0, Array.Empty<(string, string, string)>());

            mappings ??= DefaultMappings;
            var rawList = rawParameterNames.ToList();
            var changes = new List<(string OriginalName, string StandardizedName, string Reason)>();

            foreach (var raw in rawList)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var name = raw!.Trim();
                if (mappings.TryGetValue(name, out var std))
                {
                    changes.Add((name, std, "Chuyển đổi tên thuần sang thuộc tính Pset chuẩn hóa."));
                }
                else if (name.IndexOf(' ') >= 0 || name.IndexOf('-') >= 0)
                {
                    var cleaned = name.Replace(' ', '_').Replace('-', '_');
                    changes.Add((name, cleaned, "Loại bỏ khoảng trắng và ký tự đặc biệt trong tên tham số."));
                }
            }

            return new ParameterNormalizationResult(
                totalParametersChecked: rawList.Count,
                normalizedCount: changes.Count,
                changes: changes);
        }
    }
}
