using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Ai
{
    /// <summary>
    /// Kiểu của một trường config — quyết định form động dựng ô nhập nào (giai đoạn 9.1) và giúp MCP
    /// sinh JSON Schema đúng kiểu thay vì để mọi thứ là chuỗi.
    /// </summary>
    public enum FieldKind
    {
        /// <summary>Chuỗi tự do.</summary>
        Text,

        /// <summary>Số (nguyên hoặc thực).</summary>
        Number,

        /// <summary>Bật/tắt → checkbox.</summary>
        Bool,

        /// <summary>Đường dẫn file → ô nhập kèm nút Chọn file.</summary>
        FilePath,

        /// <summary>Đường dẫn thư mục → ô nhập kèm nút Chọn thư mục.</summary>
        FolderPath,

        /// <summary>Danh sách chuỗi, nhập ngăn bằng dấu phẩy.</summary>
        TextList,

        /// <summary>Category Revit → combo lấy từ mô hình đang mở.</summary>
        Category,

        /// <summary>Tên tham số → combo lấy từ mô hình đang mở.</summary>
        Parameter,

        /// <summary>Tên Level → combo lấy từ mô hình đang mở.</summary>
        Level,

        /// <summary>Tên view / view template → combo lấy từ mô hình đang mở.</summary>
        View,

        /// <summary>Tên family/type → combo lấy từ mô hình đang mở.</summary>
        FamilyType,
    }

    /// <summary>Một trường config: tên khoá JSON, mô tả cho người dùng, và kiểu để dựng ô nhập.</summary>
    public sealed class FieldSpec
    {
        public FieldSpec(string name, string description, FieldKind kind)
        {
            Name = name;
            Description = description;
            Kind = kind;
        }

        public string Name { get; }

        public string Description { get; }

        public FieldKind Kind { get; }

        /// <summary>Kiểu này có phải là danh sách nhiều giá trị không (ảnh hưởng cách ghi JSON).</summary>
        public bool IsList => Kind == FieldKind.TextList || Kind == FieldKind.Category;

        public override string ToString() => Name + ":" + Kind;
    }

    /// <summary>
    /// Đoán kiểu trường từ tên khoá. 107 trường trong <see cref="CommandCatalog"/> mà chú thích tay từng
    /// cái thì vừa lâu vừa dễ quên khi thêm lệnh; quy ước đặt tên trong repo đã đủ đều để đoán đúng, và
    /// chỗ nào đoán sai thì khai báo thẳng kiểu ở <c>.Field(name, desc, FieldKind.X)</c>.
    /// <para>Đây là hàm thuần nên có test — sai một luật là form dựng sai ô nhập cho cả nhóm lệnh.</para>
    /// </summary>
    public static class FieldKindGuess
    {
        // Đoán theo tên chính xác trước — những cái mà luật hậu tố sẽ đoán sai.
        private static readonly Dictionary<string, FieldKind> Exact =
            new Dictionary<string, FieldKind>(StringComparer.OrdinalIgnoreCase)
            {
                ["dryRun"] = FieldKind.Bool,
                ["target"] = FieldKind.Text,           // "Sheets" | "Views", không phải đường dẫn
                ["find"] = FieldKind.Text,
                ["replace"] = FieldKind.Text,
                ["prefix"] = FieldKind.Text,
                ["pattern"] = FieldKind.Text,
                ["namePattern"] = FieldKind.Text,      // mẫu tên, không phải tên view
                ["renamePattern"] = FieldKind.Text,
                ["numberPattern"] = FieldKind.Text,
                ["zone"] = FieldKind.Text,
                ["discipline"] = FieldKind.Text,
                ["formats"] = FieldKind.TextList,
                ["kinds"] = FieldKind.TextList,
                ["worksets"] = FieldKind.TextList,
                ["names"] = FieldKind.TextList,

                // Trường nhận object/mảng-object JSON: form hiện ô JSON thô, không phải combo chọn.
                ["levels"] = FieldKind.Text,           // [{name, elevationMm}]
                ["grids"] = FieldKind.Text,            // [{name, positionMm, orientation}]
                ["colors"] = FieldKind.Text,           // {tên hệ: #RRGGBB}
                ["roomFilter"] = FieldKind.Text,

                // Bool mà luật tiền tố không bắt được (chữ sau tiền tố không viết hoa).
                ["create3dView"] = FieldKind.Bool,     // "create" + '3'
                ["remove"] = FieldKind.Bool,           // "bỏ thay vì gán"
                ["reset"] = FieldKind.Bool,            // "xoá override"

                // Chuỗi lọc, không phải bool — "keep" + chữ hoa nên luật tiền tố đoán nhầm.
                ["keepNameContains"] = FieldKind.Text,
                ["lowerEnd"] = FieldKind.Text,         // "End|Start"
                ["onlyCommands"] = FieldKind.TextList,
                ["outputFolder"] = FieldKind.FolderPath,
                ["elementId"] = FieldKind.Number,
                ["sourceElementId"] = FieldKind.Number,
                ["startNumber"] = FieldKind.Number,
                ["padWidth"] = FieldKind.Number,
                ["digits"] = FieldKind.Number,
                ["revisionSequence"] = FieldKind.Number,
                ["spoolParameter"] = FieldKind.Parameter,
                ["groupByAttribute"] = FieldKind.Text, // attribute AutoCAD, không phải tham số Revit
                ["viewName"] = FieldKind.Text,         // tên view SẼ TẠO, chưa có trong mô hình
            };

        private static readonly string[] BoolPrefixes =
            { "is", "use", "create", "allow", "purge", "pin", "delete", "keep", "skip", "check", "build", "reset", "remove", "detach" };

        private static readonly string[] NumberSuffixes =
            { "Mm", "Deg", "Ms", "PerM", "Percent", "Count", "Minutes", "Seconds", "Days" };

        public static FieldKind Of(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                return FieldKind.Text;
            }

            var name = fieldName.Trim();

            if (Exact.TryGetValue(name, out var exact))
            {
                return exact;
            }

            // Đường dẫn: quy ước trong repo là hậu tố Path/Paths.
            if (name.EndsWith("Folder", StringComparison.OrdinalIgnoreCase))
            {
                return FieldKind.FolderPath;
            }

            if (name.EndsWith("Path", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Paths", StringComparison.OrdinalIgnoreCase))
            {
                return FieldKind.FilePath;
            }

            foreach (var suffix in NumberSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return FieldKind.Number;
                }
            }

            foreach (var prefix in BoolPrefixes)
            {
                // "createMissing" → Bool, nhưng "category" KHÔNG được khớp "check"/"create".
                if (name.Length > prefix.Length
                    && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && char.IsUpper(name[prefix.Length]))
                {
                    return FieldKind.Bool;
                }
            }

            // IndexOf chứ không StartsWith: "obstacleCategories" cũng là danh sách category.
            if (name.IndexOf("categor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return FieldKind.Category;
            }

            if (name.IndexOf("parameter", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return FieldKind.Parameter;
            }

            if (name.IndexOf("viewTemplate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return FieldKind.View;
            }

            if (name.EndsWith("levelName", StringComparison.OrdinalIgnoreCase)
                || name.Equals("level", StringComparison.OrdinalIgnoreCase))
            {
                return FieldKind.Level;
            }

            if (name.EndsWith("FamilyName", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Family", StringComparison.OrdinalIgnoreCase)
                || name.Equals("typeName", StringComparison.OrdinalIgnoreCase)
                || name.Equals("elementType", StringComparison.OrdinalIgnoreCase))
            {
                return FieldKind.FamilyType;
            }

            // Danh sách: số nhiều mà không rơi vào luật nào ở trên.
            if (name.EndsWith("s", StringComparison.Ordinal) && !name.EndsWith("ss", StringComparison.Ordinal)
                && !name.EndsWith("Contains", StringComparison.Ordinal))
            {
                return FieldKind.TextList;
            }

            return FieldKind.Text;
        }
    }
}
