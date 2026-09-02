using System;
using System.Reflection;

namespace DhcbTools.Shared.Hosting
{
    /// <summary>
    /// Phiên bản của bản build đang chạy, để <c>GET /health</c> và log nói được "đang chạy bản nào".
    /// Ưu tiên <c>AssemblyInformationalVersion</c> (giữ hậu tố như <c>-dev</c>) rồi mới tới
    /// <c>AssemblyVersion</c>. Trước đây mọi DLL đều là <c>0.0.0.0</c> vì
    /// <c>GenerateAssemblyInfo=false</c> mà repo không có AssemblyInfo.cs nào.
    /// </summary>
    public static class DhcbVersion
    {
        /// <summary>Chuỗi phiên bản của assembly gọi hàm này.</summary>
        public static string Of(Assembly assembly)
        {
            if (assembly is null)
            {
                return "0";
            }

            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                // SDK gắn thêm "+<git sha>" khi có SourceLink — phần trước dấu + là đủ cho người đọc.
                var plus = informational!.IndexOf('+');
                return plus > 0 ? informational.Substring(0, plus) : informational;
            }

            return assembly.GetName().Version?.ToString() ?? "0";
        }

        /// <summary>Chuỗi phiên bản của assembly đang thực thi.</summary>
        public static string Current() => Of(Assembly.GetCallingAssembly());
    }
}
