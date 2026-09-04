using System;
using System.Collections.Generic;
using System.Text;

namespace DhcbTools.Shared.Logic.Setout
{
    /// <summary>
    /// Mã ngắn theo category cho tên điểm (<c>COL001</c>, <c>SLV012</c>…). Máy toàn đạc thường giới hạn
    /// tên điểm 16 ký tự và không có chỗ cho "Structural Columns", nên mỗi category cần một mã 2–3 chữ.
    /// Category không có trong bảng thì lấy chữ cái đầu mỗi từ (tối đa 3) — không bao giờ trả rỗng.
    /// </summary>
    public static class SetoutCodes
    {
        private static readonly Dictionary<string, string> Table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Structural Columns", "COL" },
            { "Columns", "COL" },
            { "Structural Framing", "BM" },
            { "Structural Foundations", "FDN" },
            { "Walls", "WAL" },
            { "Floors", "FLR" },
            { "Generic Models", "GM" },
            { "Mechanical Equipment", "ME" },
            { "Electrical Equipment", "EE" },
            { "Electrical Fixtures", "EF" },
            { "Lighting Fixtures", "LF" },
            { "Plumbing Fixtures", "PF" },
            { "Sprinklers", "SPR" },
            { "Air Terminals", "AT" },
            { "Pipe Accessories", "PA" },
            { "Pipe Fittings", "PFT" },
            { "Pipes", "PIP" },
            { "Duct Accessories", "DA" },
            { "Duct Fittings", "DFT" },
            { "Ducts", "DCT" },
            { "Cable Trays", "CT" },
            { "Conduits", "CND" },
            { "Doors", "DR" },
            { "Windows", "WIN" },
            { "Grids", "GRD" },
            { "Specialty Equipment", "SE" },
            { "Casework", "CW" },
            { "Furniture", "FUR" },
            { "Stairs", "STR" },
            { "Railings", "RL" },
            { "Rooms", "RM" },
        };

        /// <summary>Mã cho một category; tự sinh từ chữ cái đầu khi không có trong bảng; <c>PT</c> khi rỗng.</summary>
        public static string For(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return "PT";
            }

            if (Table.TryGetValue(category!.Trim(), out var code))
            {
                return code;
            }

            var sb = new StringBuilder();
            foreach (var word in category.Split(new[] { ' ', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var c = word[0];
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToUpperInvariant(c));
                }

                if (sb.Length >= 3)
                {
                    break;
                }
            }

            return sb.Length == 0 ? "PT" : sb.ToString();
        }
    }
}
