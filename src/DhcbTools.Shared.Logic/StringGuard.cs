namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// Polyfill: netstandard2.0 không có attribute này, và bản của Newtonsoft.Json là
    /// <c>internal</c> nên assembly khác không dùng được. Khai báo tại đây để
    /// <see cref="DhcbTools.Shared.Logic.StringGuard"/> nói được với trình biên dịch.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    [CodeAnalysis.ExcludeFromCodeCoverage] // polyfill chỉ tồn tại cho trình biên dịch, không bao giờ chạy
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

        public bool ReturnValue { get; }
    }
}

namespace DhcbTools.Shared.Logic
{
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// <c>string.IsNullOrWhiteSpace</c> trong netstandard2.0 không có annotation
    /// <c>[NotNullWhen(false)]</c>, nên trình biên dịch không thấy được rằng đoạn sau lệnh guard
    /// là non-null — sinh ra một loạt CS8602/CS8604 giả. Bọc lại một lần ở đây, có annotation
    /// đầy đủ, để chỗ gọi không phải rắc toán tử <c>!</c> (thứ sẽ che mất cả null thật).
    /// </summary>
    internal static class StringGuard
    {
        public static bool IsBlank([NotNullWhen(false)] string? value) => string.IsNullOrWhiteSpace(value);

        /// <summary>Như <see cref="IsBlank"/> nhưng theo ngữ nghĩa <c>string.IsNullOrEmpty</c> — chuỗi toàn khoảng trắng vẫn tính là có nội dung.</summary>
        public static bool IsEmpty([NotNullWhen(false)] string? value) => string.IsNullOrEmpty(value);
    }
}
