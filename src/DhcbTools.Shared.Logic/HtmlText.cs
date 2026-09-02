using System;

namespace DhcbTools.Shared.Logic
{
    /// <summary>
    /// Escape văn bản chèn vào báo cáo HTML (HealthReport). Tên view/family do người dùng đặt có thể
    /// chứa &lt;, &gt;, &amp;, dấu nháy — chèn thẳng làm hỏng bố cục báo cáo.
    /// </summary>
    public static class HtmlText
    {
        /// <summary>Escape cho nội dung nằm giữa hai thẻ.</summary>
        public static string Escape(string? value)
        {
            if (StringGuard.IsEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }
    }
}
