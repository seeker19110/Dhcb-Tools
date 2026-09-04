using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Setout
{
    /// <summary>Một cột trong file CSV cho máy toàn đạc.</summary>
    public enum SetoutColumn
    {
        /// <summary>P — tên điểm.</summary>
        Name,

        /// <summary>N — Bắc (Y).</summary>
        North,

        /// <summary>E — Đông (X).</summary>
        East,

        /// <summary>Z — cao độ.</summary>
        Elevation,

        /// <summary>D — mô tả.</summary>
        Description,

        /// <summary>C — mã ngắn.</summary>
        Code,

        /// <summary>L — tầng.</summary>
        Level,

        /// <summary>I — ElementId để truy ngược về mô hình.</summary>
        ElementId,
    }

    /// <summary>
    /// Thứ tự cột viết bằng chữ cái, theo cách phần mềm trắc đạc gọi: <c>PNEZD</c> (Trimble, Leica mặc định),
    /// <c>PENZD</c> (nhiều máy Topcon/Sokkia), thêm <c>C</c>/<c>L</c>/<c>I</c> khi cần. Đây là toàn bộ "định
    /// dạng theo máy": không có bảng mẫu máy nào phải bảo trì, kỹ sư gõ đúng thứ tự cột máy mình nhận.
    /// </summary>
    public static class SetoutColumns
    {
        public const string Default = "PNEZD";

        /// <summary>Đọc chuỗi chữ cái thành danh sách cột; sai một chữ là báo lỗi rõ chứ không đoán.</summary>
        public static bool TryParse(string? letters, out List<SetoutColumn> columns, out string error)
        {
            columns = new List<SetoutColumn>();
            error = string.Empty;

            var text = (letters ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                text = Default;
            }

            foreach (var raw in text)
            {
                if (raw == ' ' || raw == ',' || raw == ';' || raw == '-' || raw == '|')
                {
                    continue;
                }

                var c = char.ToUpperInvariant(raw);
                SetoutColumn column;
                switch (c)
                {
                    case 'P': column = SetoutColumn.Name; break;
                    case 'N': column = SetoutColumn.North; break;
                    case 'E': column = SetoutColumn.East; break;
                    case 'Z': column = SetoutColumn.Elevation; break;
                    case 'D': column = SetoutColumn.Description; break;
                    case 'C': column = SetoutColumn.Code; break;
                    case 'L': column = SetoutColumn.Level; break;
                    case 'I': column = SetoutColumn.ElementId; break;
                    default:
                        error = "Cột \"" + raw + "\" trong \"" + text + "\" không hợp lệ. Hợp lệ: P (tên), N (Bắc), E (Đông), Z (cao độ), D (mô tả), C (mã), L (tầng), I (ElementId) — ví dụ PNEZD hoặc PENZD.";
                        columns.Clear();
                        return false;
                }

                if (columns.Contains(column))
                {
                    error = "Cột \"" + raw + "\" lặp lại trong \"" + text + "\".";
                    columns.Clear();
                    return false;
                }

                columns.Add(column);
            }

            if (!columns.Contains(SetoutColumn.North) || !columns.Contains(SetoutColumn.East))
            {
                error = "Thứ tự cột \"" + text + "\" thiếu N hoặc E — file không có toạ độ thì máy toàn đạc không dùng được.";
                columns.Clear();
                return false;
            }

            if (!columns.Contains(SetoutColumn.Name))
            {
                error = "Thứ tự cột \"" + text + "\" thiếu P (tên điểm) — không có tên thì không chọn được điểm trên máy.";
                columns.Clear();
                return false;
            }

            return true;
        }

        /// <summary>Tiêu đề cột — ASCII có chủ ý: dòng tiêu đề nằm trong file cho máy, không phải cho người đọc.</summary>
        public static string HeaderOf(SetoutColumn column)
        {
            switch (column)
            {
                case SetoutColumn.Name: return "Name";
                case SetoutColumn.North: return "N";
                case SetoutColumn.East: return "E";
                case SetoutColumn.Elevation: return "Z";
                case SetoutColumn.Description: return "Desc";
                case SetoutColumn.Code: return "Code";
                case SetoutColumn.Level: return "Level";
                case SetoutColumn.ElementId: return "ElementId";
                default: return column.ToString();
            }
        }
    }
}
