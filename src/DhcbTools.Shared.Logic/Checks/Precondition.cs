using System;
using System.Collections.Generic;
using System.Linq;

namespace DhcbTools.Shared.Logic.Checks
{
    /// <summary>Kết luận của một tiền đề.</summary>
    public enum PreconditionVerdict
    {
        /// <summary>Tiền đề thoả — lệnh chạy tiếp, không nói gì thêm.</summary>
        Dat,

        /// <summary>Thoả một phần — lệnh vẫn chạy nhưng kết quả phải kèm cảnh báo.</summary>
        CanhBao,

        /// <summary>Không thoả — lệnh phải DỪNG và báo lỗi, không được trả một kết quả trông sạch.</summary>
        Chan,
    }

    /// <summary>Kết luận kèm thông báo đã có sẵn mã lỗi ở đầu (khi <see cref="PreconditionVerdict.Chan"/>).</summary>
    public sealed class PreconditionResult
    {
        internal PreconditionResult(PreconditionVerdict verdict, string message)
        {
            Verdict = verdict;
            Message = message;
        }

        public PreconditionVerdict Verdict { get; }

        /// <summary>Rỗng khi <see cref="PreconditionVerdict.Dat"/>.</summary>
        public string Message { get; }

        public bool Blocks => Verdict == PreconditionVerdict.Chan;

        public bool Warns => Verdict == PreconditionVerdict.CanhBao;
    }

    /// <summary>
    /// Tiền đề của một lệnh: những điều kiện mà nếu không thoả thì <b>con số 0 không có nghĩa là "sạch"</b>.
    /// <para>
    /// Lý do tồn tại — lỗi đắt nhất đã gặp trên dự án thật (progress.md §21, bug #14): bản sao model làm
    /// mất trạng thái nạp link, <c>ClashDetection</c> báo <b>0</b> va chạm thay vì <b>479</b>, và im lặng
    /// vì "0 va chạm" trông y hệt một kết quả sạch. Nguyên nhân cụ thể đã vá ở <c>BatchJobRunner</c>
    /// (nạp lại link khi mở file), nhưng <b>lớp lỗi</b> thì chưa: bất kỳ lệnh nào cũng có thể trả 0 vì
    /// tiền đề hỏng chứ không vì mô hình sạch, và đường Ribbon/Bridge không đi qua chỗ vá đó.
    /// </para>
    /// <para>
    /// Nguyên tắc: tiền đề hỏng thì <b>dừng và nói rõ</b>, không bao giờ trả kết quả 0 trông sạch.
    /// Thuần, không tham chiếu Revit — phần thu thập sự kiện nằm ở Core, phần quyết định nằm ở đây và có test.
    /// </para>
    /// </summary>
    public static class Precondition
    {
        /// <summary>Mã lỗi đứng đầu mọi thông báo chặn — xem <c>docs/ma-loi.md</c>.</summary>
        public const string Code = "E-PRECOND";

        private static readonly PreconditionResult Passed = new PreconditionResult(PreconditionVerdict.Dat, string.Empty);

        /// <summary>
        /// Model liên kết: lệnh đọc sang link mà link chưa nạp thì phần tử bên đó vô hình với lệnh.
        /// </summary>
        /// <param name="command">Tên lệnh, để thông báo nói rõ ai đang từ chối chạy.</param>
        /// <param name="total">Tổng số link trong mô hình.</param>
        /// <param name="unloaded">Tên các link đang ở trạng thái chưa nạp.</param>
        /// <param name="tatLinkBang">Tên trường config cho phép cố ý bỏ qua link (ví dụ <c>includeLinkedModels</c>).</param>
        public static PreconditionResult LinkedModels(string command, int total, IReadOnlyList<string> unloaded, string tatLinkBang)
        {
            if (total <= 0 || unloaded == null || unloaded.Count == 0)
            {
                // Không có link, hoặc đã nạp đủ — con số lệnh trả ra nói đúng về mô hình.
                return Passed;
            }

            var ten = string.Join(", ", unloaded.Select(n => "\"" + n + "\""));

            if (unloaded.Count >= total)
            {
                return new PreconditionResult(PreconditionVerdict.Chan,
                    $"{Code}: {command} đọc cả model liên kết, nhưng cả {total} link đều CHƯA NẠP ({ten}). "
                    + "Phần tử bên link vô hình với lệnh, nên kết quả sẽ là một con số thấp giả — không phải \"sạch\". "
                    + $"Nạp lại link (Manage → Manage Links → Reload) rồi chạy lại, hoặc đặt \"{tatLinkBang}\": false "
                    + "nếu cố ý chỉ kiểm trong file này.");
            }

            return new PreconditionResult(PreconditionVerdict.CanhBao,
                $"[Cảnh báo] {unloaded.Count}/{total} model liên kết chưa nạp ({ten}) — phần tử bên trong không được xét. "
                + "Nạp lại link nếu kết quả cần phủ cả những file đó.");
        }

        /// <summary>
        /// Tập đầu vào rỗng: lệnh không có gì để làm. Trả "đã xử lý 0" trong tình huống này là im lặng
        /// đúng nghĩa — người đọc hiểu thành "không có vấn đề gì".
        /// </summary>
        /// <param name="command">Tên lệnh.</param>
        /// <param name="what">Thứ không tìm thấy, viết như trong câu: "phần tử nhóm A (Ducts, Pipes)".</param>
        /// <param name="count">Số lượng tìm được.</param>
        /// <param name="hint">Việc kỹ sư nên làm tiếp.</param>
        public static PreconditionResult NonEmptyInput(string command, string what, int count, string hint)
        {
            if (count > 0)
            {
                return Passed;
            }

            return new PreconditionResult(PreconditionVerdict.Chan,
                $"{Code}: {command} không tìm thấy {what} nào trong mô hình, nên kết quả \"0\" nói về tập đầu vào "
                + $"chứ không nói về chất lượng mô hình. {hint}");
        }

        /// <summary>
        /// Gộp nhiều tiền đề: có cái nào chặn thì trả cái chặn ĐẦU TIÊN (thông báo dài dòng vì gộp
        /// nhiều lỗi thì kỹ sư không biết sửa cái nào trước); không thì trả cảnh báo nếu có.
        /// </summary>
        public static PreconditionResult First(params PreconditionResult[] results)
        {
            if (results == null || results.Length == 0)
            {
                return Passed;
            }

            return results.FirstOrDefault(r => r.Blocks)
                ?? results.FirstOrDefault(r => r.Warns)
                ?? Passed;
        }
    }
}
