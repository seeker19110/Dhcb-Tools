using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DhcbTools.Shared.Logic
{
    /// <summary>
    /// Từ điển tên tham số và family theo từng công ty/ngôn ngữ giao diện (giai đoạn 9.2).
    /// <para>
    /// Vấn đề đang chữa: Core từng gọi thẳng <c>LookupParameter("Level")</c>, <c>"Outer Diameter"</c>,
    /// <c>"Nominal Width"</c>, và mặc định family <c>"M_Generic Model"</c> — toàn tên của thư viện mét
    /// bản tiếng Anh. Trên Revit giao diện tiếng Việt hoặc dự án dùng thư viện riêng, mọi tra cứu này
    /// trả null và lệnh <b>không làm gì mà vẫn báo thành công</b>.
    /// </para>
    /// <para>
    /// Cách chữa: mỗi khoá logic (ví dụ <c>level</c>, <c>diameter</c>) có một danh sách tên đồng nghĩa,
    /// nạp từ <c>%APPDATA%\DHCB\dictionary.json</c> và ghép với danh sách mặc định trong mã. Tra không
    /// thấy thì lệnh phải báo lỗi rõ kèm danh sách đã thử — không bao giờ im lặng.
    /// </para>
    /// <para>Thuần, không tham chiếu Revit — nên có test chạy trên CI.</para>
    /// </summary>
    public sealed class ParameterDictionary
    {
        /// <summary>Tên đồng nghĩa dựng sẵn: tiếng Anh (thư viện mét/imperial) + tiếng Việt hay gặp.</summary>
        private static readonly Dictionary<string, string[]> Builtin =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["level"] = new[] { "Level", "Reference Level", "Schedule Level", "Cao độ", "Tầng" },
                ["mark"] = new[] { "Mark", "Ký hiệu", "Số hiệu" },
                ["comments"] = new[] { "Comments", "Ghi chú" },
                ["diameter"] = new[] { "Outer Diameter", "Outside Diameter", "Diameter", "Nominal Diameter", "Đường kính" },
                ["width"] = new[] { "Width", "Nominal Width", "Chiều rộng", "Bề rộng" },
                ["height"] = new[] { "Height", "Nominal Height", "Chiều cao" },
                ["department"] = new[] { "Department", "Bộ phận", "Khu vực" },
                ["occupancy"] = new[] { "Occupancy", "Công năng", "Chức năng" },
                ["bottomElevation"] = new[] { "DHCB_Bottom_Elevation", "Bottom Elevation", "Cao độ đáy" },
                ["topElevation"] = new[] { "DHCB_Top_Elevation", "Top Elevation", "Cao độ đỉnh" },
                ["centreElevation"] = new[] { "DHCB_Center_Elevation", "Center Elevation", "Cao độ tim" },

                // Trạng thái thi công (B1) — tham số do dự án tự đặt, nên tên dựng sẵn chỉ là phỏng đoán
                // hay gặp; DictionaryLearn soi tên thật của mô hình và ghi đè lên đầu danh sách.
                ["constructionStatus"] = new[] { "DHCB_Trang_Thai", "Trạng thái thi công", "Trạng thái", "Construction Status", "Status" },
                ["constructionDate"] = new[] { "DHCB_Ngay_Lap", "Ngày lắp đặt", "Ngày lắp", "Install Date", "Construction Date" },
                ["constructionBy"] = new[] { "DHCB_Nguoi_Xac_Nhan", "Người xác nhận", "Người lắp đặt", "Installed By", "Verified By" },
            };

        private readonly Dictionary<string, List<string>> _synonyms =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private ParameterDictionary()
        {
            foreach (var pair in Builtin)
            {
                _synonyms[pair.Key] = pair.Value.ToList();
            }
        }

        /// <summary>Đường dẫn mặc định của file từ điển do công ty tự sửa.</summary>
        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHCB", "dictionary.json");

        /// <summary>Chỉ có tên dựng sẵn, không đọc file — dùng cho test và khi không có file.</summary>
        public static ParameterDictionary BuiltinOnly() => new ParameterDictionary();

        /// <summary>
        /// Nạp từ file JSON dạng <c>{ "parameters": { "level": ["Tầng", ...] }, "families": { "sleeve": "..." } }</c>.
        /// Tên trong file được đặt <b>lên trước</b> tên dựng sẵn (ưu tiên quy ước của công ty), không thay thế
        /// hẳn — để dự án dùng thư viện chuẩn vẫn chạy được.
        /// </summary>
        public static ParameterDictionary Parse(string json)
        {
            var dictionary = new ParameterDictionary();
            if (string.IsNullOrWhiteSpace(json))
            {
                return dictionary;
            }

            var root = JObject.Parse(json);

            if (root["parameters"] is JObject parameters)
            {
                foreach (var property in parameters.Properties())
                {
                    if (property.Name.StartsWith("_", StringComparison.Ordinal))
                    {
                        // "_comment" trong file mẫu là chú thích cho người đọc, không phải một khoá logic.
                        continue;
                    }

                    var names = property.Value is JArray array
                        ? array.Select(v => v.ToString()).ToList()
                        : new List<string> { property.Value.ToString() };

                    var merged = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
                    if (dictionary._synonyms.TryGetValue(property.Name, out var existing))
                    {
                        merged.AddRange(existing.Where(e => !merged.Contains(e, StringComparer.OrdinalIgnoreCase)));
                    }

                    dictionary._synonyms[property.Name] = merged;
                }
            }

            if (root["families"] is JObject families)
            {
                foreach (var property in families.Properties())
                {
                    if (property.Name.StartsWith("_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var value = property.Value.ToString().Trim();
                    if (value.Length > 0)
                    {
                        dictionary.Families[property.Name] = value;
                    }
                }
            }

            return dictionary;
        }

        /// <summary>Nạp từ <paramref name="path"/> (mặc định <see cref="DefaultPath"/>); không có file thì dùng tên dựng sẵn.</summary>
        public static ParameterDictionary Load(string? path = null)
        {
            var file = path ?? DefaultPath;
            try
            {
                return File.Exists(file) ? Parse(File.ReadAllText(file)) : BuiltinOnly();
            }
            catch (Exception)
            {
                // File hỏng không được phép chặn lệnh — quay về tên dựng sẵn, lệnh sẽ báo nếu tra không ra.
                return BuiltinOnly();
            }
        }

        /// <summary>
        /// Mọi khoá logic từ điển đang biết — tên dựng sẵn cộng tên khai trong file.
        /// <see cref="Ai.DictionarySuggester"/> duyệt danh sách này để soi mô hình.
        /// </summary>
        public IReadOnlyList<string> Keys => _synonyms.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Tên family mặc định theo khoá (ví dụ <c>sleeve</c>, <c>hanger</c>) do công ty khai báo.</summary>
        public Dictionary<string, string> Families { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Danh sách tên sẽ thử cho một khoá logic, theo thứ tự ưu tiên.
        /// <paramref name="preferred"/> (tên người dùng nhập trong config) luôn đứng đầu.
        /// </summary>
        public IReadOnlyList<string> NamesFor(string key, string? preferred = null)
        {
            var names = new List<string>();
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                names.Add(preferred!.Trim());
            }

            if (_synonyms.TryGetValue(key, out var synonyms))
            {
                names.AddRange(synonyms.Where(s => !names.Contains(s, StringComparer.OrdinalIgnoreCase)));
            }
            else if (names.Count == 0)
            {
                // Khoá lạ mà người gọi không đưa tên nào: dùng chính khoá đó, còn hơn trả rỗng.
                names.Add(key);
            }

            return names;
        }

        /// <summary>Thông báo lỗi chuẩn khi tra không ra — nêu rõ đã thử những tên nào.</summary>
        public string NotFoundMessage(string key, string? preferred = null) =>
            $"E-PARAM-MISSING: không tìm thấy tham số cho \"{key}\". Đã thử: "
            + string.Join(", ", NamesFor(key, preferred).Select(n => "\"" + n + "\""))
            + $". Thêm tên đúng của dự án vào {DefaultPath} (mục \"parameters\").";
    }
}
