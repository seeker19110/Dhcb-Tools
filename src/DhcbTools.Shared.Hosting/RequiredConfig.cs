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
            if (config == null)
            {
                return new List<string>();
            }

            var missing = new List<string>();
            foreach (var property in config.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !IsRequired(property))
                {
                    continue;
                }

                // Kiểu giá trị (int, bool…) không bao giờ null — thiếu thì nhận mặc định, không nổ.
                if (property.PropertyType.IsValueType)
                {
                    continue;
                }

                if (property.GetValue(config) == null)
                {
                    missing.Add(JsonName(property.Name));
                }
            }

            return missing;
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
