using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DhcbTools.Shared.Logic.Progress
{
    /// <summary>Một dòng CSV hiện trường đã đọc và kiểm.</summary>
    public sealed class ProgressCsvRow
    {
        public long ElementId { get; set; }

        /// <summary>
        /// Mã cấu kiện đúng như hiện trường gõ. Ở chế độ <see cref="ProgressCsvKey.ElementId"/> đây là
        /// chính con số; ở chế độ <see cref="ProgressCsvKey.Text"/> đây là giá trị của một tham số đánh
        /// dấu (Mark…) và <see cref="ElementId"/> để 0 — mô hình mới biết nó ứng với phần tử nào.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        public ConstructionStage Stage { get; set; }

        /// <summary>Tên chuẩn của trạng thái (chuỗi sẽ ghi vào mô hình).</summary>
        public string StatusText => ConstructionStatusValue.CanonicalOf(Stage);

        public DateTime? Date { get; set; }

        public string Person { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;

        /// <summary>Số dòng trong file (1-based, tính cả dòng tiêu đề) — để thông báo lỗi chỉ đúng chỗ.</summary>
        public int Line { get; set; }
    }

    /// <summary>Cột mã cấu kiện của CSV hiện trường trỏ vào cái gì.</summary>
    public enum ProgressCsvKey
    {
        /// <summary>ElementId của đúng file đang mở — chính xác tuyệt đối, nhưng chỉ có nghĩa trong file đó.</summary>
        ElementId = 0,

        /// <summary>
        /// Giá trị một tham số đánh dấu (Mark, số hiệu cấu kiện…). Sống được qua các bản phát hành mô hình
        /// và là thứ hiện trường thật sự cầm trên tay — bảng nghiệm thu ghi "D-102", không ghi 1544489.
        /// </summary>
        Text = 1,
    }

    /// <summary>Kết quả đọc file CSV hiện trường.</summary>
    public sealed class ProgressCsvResult
    {
        public List<ProgressCsvRow> Rows { get; } = new List<ProgressCsvRow>();

        /// <summary>Dòng không dùng được, mỗi cái một câu nói rõ dòng nào và vì sao.</summary>
        public List<string> Errors { get; } = new List<string>();

        /// <summary>Lỗi chặn cả file (thiếu cột bắt buộc, file rỗng); rỗng = đọc được.</summary>
        public string FatalError { get; set; } = string.Empty;

        public bool Ok => FatalError.Length == 0;
    }

    /// <summary>
    /// Đọc CSV trạng thái do hiện trường ghi (mã cấu kiện → trạng thái/ngày/người) và ghi CSV báo cáo
    /// tiến độ. Thuần chuỗi nên test được không cần Revit.
    /// <para>
    /// Tiêu đề cột nhận nhiều cách viết vì file này <b>do người gõ tay ngoài công trường</b>, không phải
    /// do tool sinh ra. Nhưng dòng không đọc được thì <b>báo đúng số dòng</b> chứ không bỏ qua im lặng:
    /// một dòng trạng thái bị nuốt là một cấu kiện biến mất khỏi báo cáo tiến độ mà không ai biết.
    /// </para>
    /// </summary>
    public static class ProgressCsv
    {
        private static readonly string[] IdHeaders = { "ElementId", "Element Id", "Id", "Mã cấu kiện", "Ma cau kien" };
        private static readonly string[] StatusHeaders = { "TrangThai", "Trạng thái", "Trang thai", "Status" };
        private static readonly string[] DateHeaders = { "Ngay", "Ngày", "Date", "Ngày lắp", "Ngay lap" };
        private static readonly string[] PersonHeaders = { "Nguoi", "Người", "Người xác nhận", "Nguoi xac nhan", "By", "Person" };
        private static readonly string[] NoteHeaders = { "GhiChu", "Ghi chú", "Ghi chu", "Note", "Comment" };

        /// <summary>Định dạng ngày nhận được — <b>ngày trước tháng</b>, đúng cách viết ở Việt Nam.</summary>
        private static readonly string[] DateFormats =
        {
            "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy",
            "yyyy-MM-dd", "yyyy/MM/dd",
            "dd/MM/yyyy HH:mm", "yyyy-MM-dd HH:mm",
        };

        public static ProgressCsvResult Read(IEnumerable<string[]> records)
        {
            return Read(records, ProgressCsvKey.ElementId);
        }

        public static ProgressCsvResult Read(IEnumerable<string[]> records, ProgressCsvKey keyKind)
        {
            var result = new ProgressCsvResult();
            var rows = records?.ToList() ?? new List<string[]>();
            if (rows.Count < 2)
            {
                result.FatalError = "File CSV không có dữ liệu (chỉ có dòng tiêu đề hoặc rỗng).";
                return result;
            }

            var header = rows[0];
            var idColumn = IndexOf(header, IdHeaders);
            var statusColumn = IndexOf(header, StatusHeaders);
            if (idColumn < 0 || statusColumn < 0)
            {
                result.FatalError =
                    "File CSV thiếu cột bắt buộc: cần một cột mã cấu kiện (" + string.Join(" / ", IdHeaders)
                    + ") và một cột trạng thái (" + string.Join(" / ", StatusHeaders)
                    + "). Đang thấy: " + string.Join(", ", header.Select(h => "\"" + h + "\"")) + ".";
                return result;
            }

            var dateColumn = IndexOf(header, DateHeaders);
            var personColumn = IndexOf(header, PersonHeaders);
            var noteColumn = IndexOf(header, NoteHeaders);

            // Trùng mã thì lấy dòng SAU CÙNG (hiện trường sửa lại ở cuối file), nhưng vẫn báo — so sánh
            // không phân biệt hoa thường vì "d-102" và "D-102" là cùng một cánh cửa.
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < rows.Count; i++)
            {
                var cells = rows[i];
                var line = i + 1;
                if (cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                var idText = Cell(cells, idColumn).Trim();
                var id = 0L;
                if (keyKind == ProgressCsvKey.ElementId)
                {
                    if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                    {
                        result.Errors.Add($"Dòng {line}: mã cấu kiện \"{idText}\" không phải số.");
                        continue;
                    }
                }
                else if (idText.Length == 0)
                {
                    result.Errors.Add($"Dòng {line}: ô mã cấu kiện để trống — không biết ghi trạng thái vào phần tử nào.");
                    continue;
                }

                var statusText = Cell(cells, statusColumn);
                if (!ConstructionStatusValue.TryParse(statusText, out var stage))
                {
                    result.Errors.Add($"Dòng {line}: " + ConstructionStatusValue.NotRecognised(statusText));
                    continue;
                }

                if (stage == ConstructionStage.ChuaCoDuLieu)
                {
                    result.Errors.Add($"Dòng {line}: ô trạng thái để trống — bỏ qua dòng này (để trống không phải là một trạng thái để ghi vào mô hình).");
                    continue;
                }

                DateTime? date = null;
                var dateText = Cell(cells, dateColumn);
                if (dateText.Trim().Length > 0)
                {
                    if (!TryParseDate(dateText, out var parsed))
                    {
                        result.Errors.Add($"Dòng {line}: ngày \"{dateText.Trim()}\" không đọc được. Dạng nhận: ngày/tháng/năm (03/09/2026) hoặc năm-tháng-ngày (2026-09-03).");
                        continue;
                    }

                    date = parsed;
                }

                if (seen.TryGetValue(idText, out var firstLine))
                {
                    result.Errors.Add($"Dòng {line}: mã cấu kiện {idText} đã có ở dòng {firstLine} — lấy dòng sau cùng.");
                    result.Rows.RemoveAll(r => string.Equals(r.Key, idText, StringComparison.OrdinalIgnoreCase));
                }

                seen[idText] = line;
                result.Rows.Add(new ProgressCsvRow
                {
                    ElementId = id,
                    Key = idText,
                    Stage = stage,
                    Date = date,
                    Person = Cell(cells, personColumn).Trim(),
                    Note = Cell(cells, noteColumn).Trim(),
                    Line = line,
                });
            }

            return result;
        }

        /// <summary>Đọc ngày theo các dạng người Việt hay gõ; ngày đứng trước tháng.</summary>
        public static bool TryParseDate(string? text, out DateTime date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return DateTime.TryParseExact(text!.Trim(), DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date);
        }

        /// <summary>CSV báo cáo: mỗi nhóm một dòng, đủ cột số lượng theo trạng thái và hai cột phần trăm.</summary>
        public static string WriteReport(IReadOnlyList<StatusRollRow> rows, string groupHeader = "Nhóm")
        {
            var sb = new StringBuilder();
            var header = new List<string> { groupHeader, "Tổng" };
            header.AddRange(ConstructionStatusValue.Stages.Select(ConstructionStatusValue.CanonicalOf));
            header.Add("Chưa có dữ liệu");
            header.Add("% đã lắp trở lên");
            header.Add("% đã nghiệm thu");
            header.Add("Tổng chiều dài (m)");
            header.Add("% đã lắp theo chiều dài");
            sb.Append(CsvText.JoinLine(header)).Append("\r\n");

            foreach (var row in rows)
            {
                var cells = new List<string> { row.Group, row.Total.ToString(CultureInfo.InvariantCulture) };
                cells.AddRange(ConstructionStatusValue.Stages.Select(s => row.CountOf(s).ToString(CultureInfo.InvariantCulture)));
                cells.Add(row.NoDataCount.ToString(CultureInfo.InvariantCulture));
                cells.Add(NumericText.Format(row.PercentAtLeast(ConstructionStage.DaLap), 1));
                cells.Add(NumericText.Format(row.PercentAtLeast(ConstructionStage.DaNghiemThu), 1));
                cells.Add(row.HasLength ? NumericText.Format(row.TotalLengthMm / 1000.0, 1) : string.Empty);
                cells.Add(row.HasLength ? NumericText.Format(row.PercentByLengthAtLeast(ConstructionStage.DaLap), 1) : string.Empty);
                sb.Append(CsvText.JoinLine(cells)).Append("\r\n");
            }

            return sb.ToString();
        }

        private static string Cell(string[] cells, int index) =>
            index >= 0 && index < cells.Length ? cells[index] ?? string.Empty : string.Empty;

        private static int IndexOf(string[] header, string[] candidates)
        {
            for (var i = 0; i < header.Length; i++)
            {
                var name = (header[i] ?? string.Empty).Trim();
                if (candidates.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
