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

        /// <summary>
        /// Object/mảng-object JSON thô (<c>levels</c>, <c>grids</c>, <c>colors</c>, điểm <c>{x,y,z}</c>):
        /// form hiện ô nhiều dòng và đọc lại bằng bộ đọc JSON.
        /// <para>
        /// Phải là một KIỂU RIÊNG chứ không dựa vào "chuỗi bắt đầu bằng dấu ngoặc": mẫu đặt tên của
        /// <c>SheetRename</c> là <c>{Discipline}-{Number}</c> — bắt đầu bằng "{" nhưng không phải JSON,
        /// và bản trước chặn thẳng người dùng lại (bắt được khi bấm tay 2026-09-05, §34).
        /// </para>
        /// </summary>
        Json,
    }

    /// <summary>Một trường config: tên khoá JSON, mô tả cho người dùng, và kiểu để dựng ô nhập.</summary>
    public sealed class FieldSpec
    {
        public FieldSpec(string name, string description, FieldKind kind)
        {
            Name = name;
            Description = description;
            Kind = kind;
            IsList = kind == FieldKind.TextList || (IsChoiceKind(kind) && LooksPlural(name));
        }

        public string Name { get; }

        public string Description { get; }

        public FieldKind Kind { get; }

        /// <summary>
        /// Trường này nhận <b>nhiều</b> giá trị hay <b>một</b> — quyết định form ghi JSON ra mảng hay ra
        /// chuỗi.
        /// <para>
        /// Không suy được từ mỗi <see cref="Kind"/>: cùng là combo lấy từ mô hình, <c>categories</c> là
        /// danh sách còn <c>category</c> là một; <c>parameterNames</c> là danh sách còn <c>parameterName</c>
        /// là một. Trước đây mọi <c>Category</c> đều bị coi là danh sách và mọi <c>Parameter</c> đều
        /// không, nên form gửi mảng vào property <c>string</c> (và ngược lại) — Newtonsoft ném ngay và
        /// lệnh <b>không chạy được từ Ribbon</b>, trong khi bộ ca kiểm gửi JSON đúng kiểu nên vẫn xanh.
        /// Tìm ra khi bấm tay 2026-09-05, xem <c>docs/bang-chung-test.md</c> §34.
        /// </para>
        /// </summary>
        public bool IsList { get; }

        private static bool IsChoiceKind(FieldKind kind) =>
            kind == FieldKind.Category || kind == FieldKind.Parameter || kind == FieldKind.Level
            || kind == FieldKind.View || kind == FieldKind.FamilyType;

        /// <summary>
        /// Tên trường có nói rằng nó chứa nhiều giá trị không. <c>categoriesA</c>/<c>categoriesB</c> không
        /// kết thúc bằng "s" nên phải bắt riêng chuỗi "categories"; <c>parameterNames</c> bắt bằng hậu tố
        /// "Names".
        /// </summary>
        private static bool LooksPlural(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (name.IndexOf("categories", StringComparison.OrdinalIgnoreCase) >= 0
                || name.EndsWith("Names", StringComparison.Ordinal))
            {
                return true;
            }

            return name.EndsWith("s", StringComparison.Ordinal)
                   && !name.EndsWith("ss", StringComparison.Ordinal)
                   && !name.EndsWith("Contains", StringComparison.Ordinal);
        }

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
                // "pattern" là mẫu CHUỖI ở TextReplace/SheetRename nhưng là OBJECT ở DevicePlacement —
                // cùng một tên, hai kiểu, nên chỗ kia phải khai thẳng FieldKind.Json trong catalog.
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
                ["sizeMm"] = FieldKind.Json,          // RouteSizeMm {width, height} — cũng bị hậu tố "Mm" đoán nhầm
                ["startMm"] = FieldKind.Json,         // PointMm {x, y, z} — hậu tố "Mm" làm luật số đoán nhầm
                ["endMm"] = FieldKind.Json,           // PointMm {x, y, z}
                ["levels"] = FieldKind.Json,           // [{name, elevationMm}]
                ["grids"] = FieldKind.Json,            // [{name, positionMm, orientation}]
                ["colors"] = FieldKind.Json,           // {tên hệ: #RRGGBB}
                ["roomFilter"] = FieldKind.Json,

                // Bool mà luật tiền tố không bắt được (chữ sau tiền tố không viết hoa).
                ["create3dView"] = FieldKind.Bool,     // "create" + '3'
                ["remove"] = FieldKind.Bool,           // "bỏ thay vì gán"
                ["reset"] = FieldKind.Bool,            // "xoá override"

                // Chuỗi lọc, không phải bool — "keep" + chữ hoa nên luật tiền tố đoán nhầm.
                ["keepNameContains"] = FieldKind.TextList,   // List<string>: "keep" + hậu tố "Contains" đánh lừa cả hai luật
                ["lowerEnd"] = FieldKind.Text,         // "End|Start"
                ["onlyCommands"] = FieldKind.TextList,
                ["outputFolder"] = FieldKind.FolderPath,
                ["days"] = FieldKind.Number,          // số ngày, không phải danh sách (hậu tố "s")
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
