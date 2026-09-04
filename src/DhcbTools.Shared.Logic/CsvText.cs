using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DhcbTools.Shared.Logic
{
    /// <summary>
    /// Đọc/ghi CSV theo RFC 4180 (mức đủ dùng cho Excel). Trước đây logic này bị chép ở bốn nơi
    /// (ParameterExport/ParameterImport bên Revit, LayerExport/LayerImport bên AutoCAD); gom về một chỗ
    /// để sửa một lần là cả hai vỏ cùng đúng.
    /// </summary>
    public static class CsvText
    {
        /// <summary>
        /// UTF-8 CÓ BOM. Excel trên Windows đọc file CSV không BOM theo code page hệ thống nên
        /// tên tiếng Việt hiện sai; luôn ghi bằng encoding này.
        /// </summary>
        public static Encoding Utf8WithBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        /// <summary>
        /// Bọc một ô CSV: chỉ thêm dấu nháy khi thật sự cần (có dấu phẩy, nháy kép, CR hoặc LF),
        /// và nhân đôi dấu nháy kép bên trong.
        /// </summary>
        public static string Escape(string? value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            var needsQuote = value.IndexOf(',') >= 0
                             || value.IndexOf('"') >= 0
                             || value.IndexOf('\n') >= 0
                             || value.IndexOf('\r') >= 0;

            if (!needsQuote)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Tách một dòng CSV thành các ô. Dòng rỗng trả về một ô rỗng (đúng ngữ nghĩa "một cột trống"),
        /// nháy kép đôi bên trong ô có nháy được gộp lại thành một.
        /// </summary>
        public static List<string> SplitLine(string? line)
        {
            var cells = new List<string>();
            if (line == null)
            {
                cells.Add(string.Empty);
                return cells;
            }

            var current = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        inQuotes = false;
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    cells.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(c);
                }
            }

            cells.Add(current.ToString());
            return cells;
        }

        /// <summary>Ghép một dòng CSV từ các ô, tự escape từng ô.</summary>
        public static string JoinLine(IEnumerable<string> cells)
        {
            var sb = new StringBuilder();
            var first = true;
            foreach (var cell in cells)
            {
                if (!first)
                {
                    sb.Append(',');
                }
                sb.Append(Escape(cell));
                first = false;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Đọc toàn bộ bản ghi CSV theo RFC 4180: ô có nháy được phép chứa dấu phẩy, nháy kép đôi VÀ xuống dòng.
        /// Trước đây mọi chỗ đọc đều <c>File.ReadAllLines</c> + <see cref="SplitLine"/>, nên một ô Description
        /// nhiều dòng (do chính <see cref="Escape"/> ghi ra) bị cắt thành hai bản ghi hỏng. Dòng trống trả về
        /// một bản ghi có đúng một ô rỗng (giống <see cref="SplitLine"/>); dòng trống cuối file không sinh bản ghi.
        /// </summary>
        public static IEnumerable<string[]> ReadRecords(TextReader reader)
        {
            if (reader == null)
            {
                throw new System.ArgumentNullException(nameof(reader));
            }

            var cells = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;
            var hasContent = false; // đã đọc được ký tự nào của bản ghi hiện tại chưa

            int ch;
            while ((ch = reader.Read()) >= 0)
            {
                var c = (char)ch;
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (reader.Peek() == '"')
                        {
                            reader.Read();
                            current.Append('"');
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        hasContent = true;
                        break;
                    case ',':
                        cells.Add(current.ToString());
                        current.Length = 0;
                        hasContent = true;
                        break;
                    case '\r':
                        if (reader.Peek() == '\n')
                        {
                            reader.Read();
                        }
                        goto case '\n';
                    case '\n':
                        cells.Add(current.ToString());
                        yield return cells.ToArray();
                        cells.Clear();
                        current.Length = 0;
                        hasContent = false;
                        break;
                    default:
                        current.Append(c);
                        hasContent = true;
                        break;
                }
            }

            if (hasContent || cells.Count > 0 || current.Length > 0)
            {
                cells.Add(current.ToString());
                yield return cells.ToArray();
            }
        }

        /// <summary>Đọc file CSV theo <see cref="ReadRecords(TextReader)"/>; nhận cả UTF-8 có/không BOM.</summary>
        public static IEnumerable<string[]> ReadRecords(string path)
        {
            using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                foreach (var record in ReadRecords(reader))
                {
                    yield return record;
                }
            }
        }
    }
}
