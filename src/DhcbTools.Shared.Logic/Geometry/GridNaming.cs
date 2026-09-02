using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DhcbTools.Shared.Logic.Geometry
{
    /// <summary>Quy tắc đặt tên trục (mục 2.3): trục dọc chữ A,B,C… (bỏ I, O), trục ngang số 1,2,3…; đảo được.</summary>
    public sealed class GridNamingRule
    {
        /// <summary>Trục dọc dùng chữ (mặc định) hay số.</summary>
        public bool VerticalUsesLetters { get; set; } = true;

        /// <summary>Bỏ I và O để không nhầm với 1 và 0.</summary>
        public bool SkipIO { get; set; } = true;

        /// <summary>Trục dọc đánh từ trái sang phải (X tăng); ngang từ dưới lên (Y tăng) — đảo nếu cần.</summary>
        public bool VerticalLeftToRight { get; set; } = true;

        public bool HorizontalBottomToTop { get; set; } = true;

        public string Prefix { get; set; } = string.Empty;
    }

    public static class GridNaming
    {
        /// <summary>Nhãn chữ thứ <paramref name="index"/> (0 → A). Sau Z: AA, AB… (bỏ I/O nếu cấu hình).</summary>
        public static string Letter(int index, bool skipIO = true)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var alphabet = skipIO ? "ABCDEFGHJKLMNPQRSTUVWXYZ" : "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var n = alphabet.Length;
            var label = string.Empty;
            var i = index;
            do
            {
                label = alphabet[i % n] + label;
                i = i / n - 1;
            }
            while (i >= 0);
            return label;
        }

        /// <summary>Gán <see cref="GridLine.Name"/> cho toàn bộ trục theo quy tắc, theo thứ tự vị trí. Trả về chính danh sách.</summary>
        public static List<GridLine> Apply(List<GridLine> grids, GridNamingRule? rule = null)
        {
            rule = rule ?? new GridNamingRule();

            var verticals = grids.Where(g => g.IsVertical).OrderBy(g => g.Position).ToList();
            if (!rule.VerticalLeftToRight)
            {
                verticals.Reverse();
            }

            var horizontals = grids.Where(g => !g.IsVertical).OrderBy(g => g.Position).ToList();
            if (!rule.HorizontalBottomToTop)
            {
                horizontals.Reverse();
            }

            for (var i = 0; i < verticals.Count; i++)
            {
                verticals[i].Name = rule.Prefix + (rule.VerticalUsesLetters ? Letter(i, rule.SkipIO) : (i + 1).ToString(CultureInfo.InvariantCulture));
            }

            for (var i = 0; i < horizontals.Count; i++)
            {
                horizontals[i].Name = rule.Prefix + (rule.VerticalUsesLetters ? (i + 1).ToString(CultureInfo.InvariantCulture) : Letter(i, rule.SkipIO));
            }

            return grids;
        }

        /// <summary>CSV <c>Name,X1,Y1,X2,Y2</c> (mm) — đúng định dạng lệnh <c>GridFromCsv</c> của Revit nhận.</summary>
        public static string ToCsv(IEnumerable<GridLine> grids)
        {
            var lines = new List<string> { "Name,X1,Y1,X2,Y2" };
            foreach (var g in grids)
            {
                var cells = g.IsVertical
                    ? new[] { g.Name, NumericText.Format(g.Position, 1), NumericText.Format(g.Start, 1), NumericText.Format(g.Position, 1), NumericText.Format(g.End, 1) }
                    : new[] { g.Name, NumericText.Format(g.Start, 1), NumericText.Format(g.Position, 1), NumericText.Format(g.End, 1), NumericText.Format(g.Position, 1) };
                lines.Add(CsvText.JoinLine(cells));
            }

            return string.Join("\n", lines) + "\n";
        }

        /// <summary>Đọc lại CSV <c>Name,X1,Y1,X2,Y2</c>; dòng lỗi được ghi vào <paramref name="errors"/> thay vì ném.</summary>
        public static List<GridLine> FromCsv(string csv, List<string> errors)
        {
            var result = new List<GridLine>();
            var lines = csv.Replace("\r\n", "\n").Split('\n');
            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var cells = CsvText.SplitLine(lines[i]);
                if (cells.Count < 5
                    || !NumericText.TryParseDouble(cells[1], out var x1) || !NumericText.TryParseDouble(cells[2], out var y1)
                    || !NumericText.TryParseDouble(cells[3], out var x2) || !NumericText.TryParseDouble(cells[4], out var y2))
                {
                    errors.Add("Dòng " + (i + 1) + ": cần Name,X1,Y1,X2,Y2 dạng số — bỏ qua.");
                    continue;
                }

                var vertical = Math.Abs(x2 - x1) < Math.Abs(y2 - y1);
                var line = vertical
                    ? new GridLine(true, (x1 + x2) / 2.0, Math.Min(y1, y2), Math.Max(y1, y2), 1)
                    : new GridLine(false, (y1 + y2) / 2.0, Math.Min(x1, x2), Math.Max(x1, x2), 1);
                line.Name = cells[0];
                result.Add(line);
            }

            return result;
        }
    }
}
