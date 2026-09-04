using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>Config gửi tới lệnh không hợp lệ — bảng dispatch đổi thành <c>CommandResult.Fail</c> có thông báo rõ.</summary>
    public sealed class ConfigException : Exception
    {
        public ConfigException(string message) : base(message) { }

        public ConfigException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Kiểm trường <c>required</c> của lớp config sau khi deserialize.
    /// <para>
    /// Vì sao cần: từ khoá <c>required</c> của C# chỉ được compiler kiểm khi khởi tạo bằng object
    /// initializer. Newtonsoft dựng object bằng reflection nên đi vòng qua nó — thiếu trường thì property
    /// đơn giản là <c>null</c>, và lệnh nổ <c>NullReferenceException</c> trần trụi ngay dòng đầu.
    /// </para>
    /// <para>
    /// Lỗi có thật: vòng kiểm thử MEP đầu tiên trong Revit (2026-09-03), <c>SystemColor</c> gọi không kèm
    /// <c>colors</c> → <c>foreach (var kv in config.Colors)</c> ném NRE. Agent hay kỹ sư nhận được một
    /// stack trace .NET thay vì "thiếu trường colors".
    /// </para>
    /// </summary>
    public static class RequiredConfig
    {
        /// <summary>
        /// Tên (kiểu JSON, chữ thường đầu) của các trường <c>required</c> đang null. Rỗng = hợp lệ.
        /// </summary>
        public static IReadOnlyList<string> MissingMembers(object? config)
        {
            var missing = new List<string>();
            Collect(config, string.Empty, missing, 0);
            return missing;
        }

        /// <summary>
        /// Đệ quy vào object lồng và từng phần tử của danh sách (ví dụ <c>levels[0].name</c>): trước đây
        /// chỉ kiểm lớp ngoài cùng, nên <c>{"levels":[{"elevationMm":0}]}</c> qua kiểm rồi nổ NRE ở
        /// <c>LevelDefinition.Name</c>.
        /// </summary>
        private static void Collect(object? value, string prefix, List<string> missing, int depth)
        {
            if (value == null || depth > 4)
            {
                return;
            }

            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal || value is DateTime)
            {
                return;
            }

            if (value is System.Collections.IDictionary)
            {
                return;
            }

            if (value is System.Collections.IEnumerable list)
            {
                var i = 0;
                foreach (var item in list)
                {
                    Collect(item, prefix + "[" + i + "].", missing, depth + 1);
                    i++;
                }
                return;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object? propertyValue;
                try
                {
                    propertyValue = property.GetValue(value);
                }
                catch (Exception)
                {
                    continue;
                }

                // Kiểu giá trị (int, bool…) không bao giờ null — thiếu thì nhận mặc định, không nổ.
                if (IsRequired(property) && !property.PropertyType.IsValueType && propertyValue == null)
                {
                    missing.Add(prefix + JsonName(property.Name));
                    continue;
                }

                // Chỉ lặn vào kiểu của repo (config lồng, danh sách định nghĩa) — không lặn vào kiểu Revit/.NET.
                if (propertyValue != null && IsOwnType(property.PropertyType))
                {
                    Collect(propertyValue, prefix + JsonName(property.Name) + ".", missing, depth + 1);
                }
            }
        }

        private static bool IsOwnType(Type type)
        {
            if (type.IsArray)
            {
                return IsOwnType(type.GetElementType()!);
            }

            if (type.IsGenericType)
            {
                return type.GetGenericArguments().Any(IsOwnType);
            }

            return (type.Namespace ?? string.Empty).StartsWith("DhcbTools", StringComparison.Ordinal);
        }

        /// <summary>Ném <see cref="ConfigException"/> có thông báo tiếng Việt nếu thiếu trường bắt buộc.</summary>
        public static void ThrowIfIncomplete(object? config, string configTypeName)
        {
            var missing = MissingMembers(config);
            if (missing.Count == 0)
            {
                return;
            }

            throw new ConfigException(
                $"E-CONFIG-MISSING: thiếu trường bắt buộc trong config ({configTypeName}): "
                + string.Join(", ", missing.Select(m => "\"" + m + "\"")) + ".");
        }

        /// <summary>So khớp theo TÊN attribute để chạy được cả trên net48 (dùng bản polyfill trong repo).</summary>
        private static bool IsRequired(PropertyInfo property) =>
            property.GetCustomAttributes(inherit: true)
                .Any(a => string.Equals(a.GetType().Name, "RequiredMemberAttribute", StringComparison.Ordinal));

        private static string JsonName(string clrName) =>
            string.IsNullOrEmpty(clrName) ? clrName : char.ToLowerInvariant(clrName[0]) + clrName.Substring(1);
    }
}
