using System;

namespace DhcbTools.Shared.Logic.Batch
{
    /// <summary>
    /// Sinh journal khởi động Revit cho batch — bản đối xứng của <see cref="AcadScriptGen"/> bên AutoCAD.
    /// <para>
    /// Revit không có chế độ headless chính thức; cách chạy không người trực là mở
    /// <c>Revit.exe "&lt;journal&gt;"</c> rồi để add-in tự đọc <c>pending-job.json</c> trong
    /// <c>ApplicationInitialized</c>. Journal chỉ cần làm một việc: tắt hộp thoại hỏi khi lỗi.
    /// </para>
    /// <para>
    /// Thuần chuỗi nên test được — và cần test, vì một dòng thừa trong journal làm hỏng cả vòng batch
    /// mà không test biên dịch nào bắt được (xem <see cref="Build"/>).
    /// </para>
    /// </summary>
    public static class RevitJournalGen
    {
        /// <summary>
        /// Journal tối giản cho batch.
        /// <para>
        /// <b>Tuyệt đối không thêm <c>Jrn.Directive "DocSymbol", "[]"</c>.</b> Chỉ thị đó cần một document
        /// đang mở để bind, mà lúc Revit vừa khởi động thì chưa có. Revit ghi vào journal của nó
        /// <i>"no DocumentStorage available to bind to DocSymbol []"</i>, coi journal là sai nhịp
        /// (<i>"Execution did not correspond to recorded journal sequence"</i>) và <b>dừng playback ngay tại
        /// dòng đó</b>. Lỗi này chỉ lộ ra khi chạy thật trên Revit — vòng kiểm thử đầu tiên ngày 2026-09-03
        /// mất 10 phút treo ở đây trước khi runner bỏ cuộc.
        /// </para>
        /// </summary>
        public static string Build()
        {
            return string.Join(Environment.NewLine,
                "' DHCB Tools batch journal",
                "Dim Jrn",
                "Set Jrn = CrsJournalScript",
                "Jrn.Directive \"DebugMode\", \"PerformAutomaticActionInErrorDialog\", 1",
                "Jrn.Directive \"DebugMode\", \"PermissiveJournal\", 1",
                string.Empty);
        }
    }
}
