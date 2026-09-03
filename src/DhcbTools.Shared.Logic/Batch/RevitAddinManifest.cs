using System;

namespace DhcbTools.Shared.Logic.Batch
{
    /// <summary>
    /// Sinh file <c>.addin</c> đặt <b>cạnh journal</b> để add-in được nạp khi chạy batch.
    /// <para>
    /// Khi Revit khởi động bằng journal, nó <b>chỉ đăng ký add-in có file <c>.addin</c> nằm cùng thư mục
    /// với journal</b> — Autodesk cố ý thiết kế vậy để giảm nhiễu khi chạy kiểm thử hồi quy. Add-in cài ở
    /// <c>%APPDATA%\Autodesk\Revit\Addins\&lt;năm&gt;</c> hoàn toàn bị bỏ qua, không báo lỗi, không hộp thoại.
    /// </para>
    /// <para>
    /// Đây là lý do batch chạy đêm chưa từng chạy được: đo trên máy thật ngày 2026-09-03, phiên tương tác
    /// nạp 48 external application (có DHCB), phiên khởi động bằng journal chỉ nạp 38 — không add-in bên thứ
    /// ba nào. Ký số DLL <b>không</b> giải quyết được; phải đặt manifest cạnh journal.
    /// </para>
    /// </summary>
    public static class RevitAddinManifest
    {
        /// <summary>AddInId cố định của DHCB Tools — trùng với file .addin cài kèm add-in.</summary>
        public const string AddInId = "2E9F5B1A-8F2D-4C7E-9B3A-1D6C4E8F2A70";

        /// <summary>
        /// Manifest trỏ tới DLL bằng <b>đường dẫn tuyệt đối</b>, nên không phải chép DLL sang cạnh journal.
        /// </summary>
        /// <param name="assemblyPath">Đường dẫn đầy đủ tới <c>DhcbTools.Revit.dll</c> đã cài.</param>
        public static string Build(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                throw new ArgumentException("Thiếu đường dẫn tới DhcbTools.Revit.dll.", nameof(assemblyPath));
            }

            return string.Join(Environment.NewLine,
                "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"no\"?>",
                "<!-- Sinh tự động bởi DhcbTools.BatchRunner. Phải nằm CÙNG THƯ MỤC với journal thì Revit",
                "     mới nạp add-in khi chạy batch — xem RevitAddinManifest. -->",
                "<RevitAddIns>",
                "  <AddIn Type=\"Application\">",
                "    <Name>DHCB Revit Tools (batch)</Name>",
                "    <Assembly>" + Escape(assemblyPath) + "</Assembly>",
                "    <AddInId>" + AddInId + "</AddInId>",
                "    <FullClassName>DhcbTools.Revit.App</FullClassName>",
                "    <VendorId>DHCB</VendorId>",
                "    <VendorDescription>DHCB, https://github.com/seeker19110/Dhcb-Tools</VendorDescription>",
                "  </AddIn>",
                "</RevitAddIns>",
                string.Empty);
        }

        /// <summary>Đường dẫn XML có thể chứa &amp; hoặc &lt; — thoát cho đúng, không thì Revit bỏ qua cả file.</summary>
        private static string Escape(string value) => value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
