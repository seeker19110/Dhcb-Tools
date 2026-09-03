using System;
using System.Collections.Generic;
using System.Globalization;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>
    /// Một dòng CSV layer đã đọc xong (định dạng do <c>LayerExportCommand</c> ghi ra:
    /// <c>Name,Color,Linetype,Lineweight,IsPlottable,Description</c>).
    /// <para>
    /// Đây là phần thuần của <c>LayerImportCommand</c>, tách khỏi vỏ AutoCAD để test được trên CI Linux —
    /// vỏ chỉ còn việc so sánh với drawing và ghi. Ô để trống nghĩa là "giữ nguyên giá trị đang có" nên mọi
    /// thuộc tính đều nullable; riêng Description có ô rỗng vẫn là lệnh xoá mô tả (đúng như bản trước làm).
    /// </para>
    /// </summary>
    public sealed class LayerCsvRow
    {
        private LayerCsvRow(string name)
        {
            Name = name;
        }

        /// <summary>Tên layer (cột 1) — luôn khác rỗng khi <see cref="IsEmpty"/> là false.</summary>
        public string Name { get; }

        /// <summary>Dòng trống hoặc không có tên layer — lệnh gọi bỏ qua, không tính là lỗi.</summary>
        public bool IsEmpty { get; private set; }

        /// <summary>Chỉ số màu ACI (0–256) nếu cột màu ghi theo ACI.</summary>
        public short? ColorAci { get; private set; }

        /// <summary>
        /// Màu true color dạng 0xRRGGBB nếu cột màu ghi theo <c>ColorValue</c> — lệnh xuất ghi kiểu này cho
        /// layer không dùng màu ACI, bản trước không đọc lại được nên màu true color sửa trong Excel bị bỏ im lặng.
        /// </summary>
        public int? ColorRgb { get; private set; }

        /// <summary>Tên linetype cần gán (chưa kiểm tra có trong drawing hay không).</summary>
        public string? Linetype { get; private set; }

        /// <summary>
        /// Giá trị enum <c>LineWeight</c> của AutoCAD: bề rộng theo phần trăm mm (0, 5, 9, … 211)
        /// hoặc -1 ByLayer, -2 ByBlock, -3 ByLineWeightDefault. Vỏ AutoCAD ép kiểu và kiểm tra hợp lệ.
        /// </summary>
        public int? LineWeight { get; private set; }

        /// <summary>Cột IsPlottable.</summary>
        public bool? Plottable { get; private set; }

        /// <summary>Cột Description; chuỗi rỗng vẫn có nghĩa (xoá mô tả), null = CSV không có cột này.</summary>
        public string? Description { get; private set; }

        /// <summary>Cảnh báo từng ô không đọc được — lệnh gọi ghi thẳng vào nhật ký thay vì im lặng bỏ qua.</summary>
        public IReadOnlyList<string> Warnings => _warnings;

        private readonly List<string> _warnings = new List<string>();

        /// <summary>Tên các lineweight đặc biệt mà AutoCAD xuất ra.</summary>
        private static readonly Dictionary<string, int> SpecialLineWeights =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "ByLayer", -1 },
                { "ByBlock", -2 },
                { "ByLineWeightDefault", -3 },
                { "Default", -3 },
            };

        /// <summary>Đọc một dòng CSV. <paramref name="lineNumber"/> chỉ dùng cho câu cảnh báo (1-based như Excel).</summary>
        public static LayerCsvRow Parse(string? line, int lineNumber)
        {
            return FromCells(CsvText.SplitLine(line), lineNumber);
        }

        /// <summary>Đọc một dòng đã tách ô sẵn.</summary>
        public static LayerCsvRow FromCells(IReadOnlyList<string> cells, int lineNumber)
        {
            var name = cells.Count > 0 ? cells[0].Trim() : string.Empty;
            var row = new LayerCsvRow(name);

            if (name.Length == 0)
            {
                row.IsEmpty = true;
                return row;
            }

            row.ParseColor(cells, lineNumber);
            row.Linetype = Cell(cells, 2);
            row.ParseLineWeight(cells, lineNumber);
            row.ParsePlottable(cells, lineNumber);

            if (cells.Count > 5)
            {
                row.Description = cells[5];
            }

            return row;
        }

        private void ParseColor(IReadOnlyList<string> cells, int lineNumber)
        {
            var text = Cell(cells, 1);
            if (text == null)
            {
                return;
            }

            // Lệnh xuất ghi ColorIndex cho màu ACI và ColorValue (số ngoài dải 0–256) cho true color.
            if (short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var aci)
                && aci >= 0 && aci <= 256)
            {
                ColorAci = aci;
                return;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                ColorRgb = value & 0xFFFFFF;
                return;
            }

            _warnings.Add($"Dòng {lineNumber}: màu \"{text}\" không đọc được — giữ nguyên.");
        }

        private void ParseLineWeight(IReadOnlyList<string> cells, int lineNumber)
        {
            var text = Cell(cells, 3);
            if (text == null)
            {
                return;
            }

            if (SpecialLineWeights.TryGetValue(text, out var special))
            {
                LineWeight = special;
                return;
            }

            // Dạng AutoCAD xuất ra: "LineWeight025"; cũng nhận số trần "25" cho người sửa tay trong Excel.
            var digits = text.StartsWith("LineWeight", StringComparison.OrdinalIgnoreCase)
                ? text.Substring("LineWeight".Length)
                : text;

            if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hundredths)
                && hundredths >= 0)
            {
                LineWeight = hundredths;
                return;
            }

            _warnings.Add($"Dòng {lineNumber}: lineweight \"{text}\" không hợp lệ — giữ nguyên.");
        }

        private void ParsePlottable(IReadOnlyList<string> cells, int lineNumber)
        {
            var text = Cell(cells, 4);
            if (text == null)
            {
                return;
            }

            if (bool.TryParse(text, out var plottable))
            {
                Plottable = plottable;
                return;
            }

            _warnings.Add($"Dòng {lineNumber}: cột IsPlottable \"{text}\" không hợp lệ — giữ nguyên.");
        }

        private static string? Cell(IReadOnlyList<string> cells, int index)
        {
            if (cells.Count <= index)
            {
                return null;
            }

            var value = cells[index].Trim();
            return value.Length == 0 ? null : value;
        }
    }
}
