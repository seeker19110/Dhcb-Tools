using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DhcbTools.Shared.Logic.Batch
{
    /// <summary>
    /// Lưu DWG của batch AutoCAD qua một file tạm rồi mới thay file đích — cách để <c>saveOnError</c> có nghĩa
    /// bên AutoCAD.
    /// <para>
    /// Vì sao cần: script accoreconsole dựng sẵn từ đầu, dòng <c>SAVEAS</c> nằm cuối và chạy bất kể các
    /// <c>DHCB_RUN</c> trước đó lỗi hay không. Bản cũ vì thế lưu cả bản vẽ có step lỗi — với
    /// <c>saveMode: "Save"</c> là ghi đè file GỐC bằng một bản vẽ nửa vời (step trước đã ghi, step sau
    /// rollback), đúng điều <c>saveOnError: false</c> (mặc định) hứa sẽ chặn và batch Revit đã chặn từ lâu.
    /// Nay script lưu vào file tạm, runner đọc log của chính file đó rồi mới quyết định thay hay bỏ.
    /// </para>
    /// </summary>
    public static class StagedSave
    {
        /// <summary>
        /// File tạm nằm CẠNH file đích: <c>&lt;tên&gt;.dhcb-luu-&lt;dấu thời gian&gt;.dwg</c>. Cùng thư mục để đường dẫn
        /// xref tương đối trong bản vẽ vẫn đúng nghĩa khi file tạm thành file đích, và để bước thay là một lần đổi
        /// tên trên cùng ổ đĩa.
        /// </summary>
        public static string StagingPath(string target, DateTime runTime)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                throw new ArgumentException("Thiếu đường dẫn file đích.", nameof(target));
            }

            var folder = Path.GetDirectoryName(target) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(target)
                       + ".dhcb-luu-" + runTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".dwg";
            return Path.Combine(folder, name);
        }

        /// <summary>
        /// Lý do KHÔNG được thay file đích, hoặc <c>null</c> nếu lượt chạy của file này sạch: accoreconsole thoát 0
        /// đúng giờ, NETLOAD được, mọi step đều ghi kết quả vào log và đều thành công. Thiếu dòng log của step
        /// nào cũng là lý do — không biết step đó ra sao thì không lưu đè thay người dùng.
        /// </summary>
        public static string? Blocker(bool timedOut, int exitCode, bool netloadFailed, int expectedSteps, IReadOnlyList<RunLogEntry> stepEntries)
        {
            if (stepEntries == null)
            {
                throw new ArgumentNullException(nameof(stepEntries));
            }

            if (timedOut)
            {
                return "accoreconsole quá giờ.";
            }

            if (exitCode != 0)
            {
                return "accoreconsole thoát mã " + exitCode.ToString(CultureInfo.InvariantCulture) + ".";
            }

            if (netloadFailed)
            {
                return "NETLOAD thất bại — không step nào chạy.";
            }

            foreach (var entry in stepEntries)
            {
                if (!entry.Success)
                {
                    return "step " + entry.Command + " lỗi: " + entry.Summary;
                }
            }

            if (stepEntries.Count < expectedSteps)
            {
                return "chỉ " + stepEntries.Count.ToString(CultureInfo.InvariantCulture) + "/"
                       + expectedSteps.ToString(CultureInfo.InvariantCulture) + " step ghi kết quả vào log.";
            }

            return null;
        }

        /// <summary>
        /// Thay <paramref name="target"/> bằng <paramref name="staging"/>. <paramref name="keepBackup"/> = true (lưu đè
        /// file gốc) thì bản cũ giữ ở <c>&lt;tên&gt;.bak</c> — đúng thứ <c>SAVEAS</c> đè file của AutoCAD vẫn để lại
        /// (<c>ISAVEBAK</c>), bản cũ của batch có nó nên bản mới không được bỏ mất đường lui đó.
        /// </summary>
        public static void Promote(string staging, string target, bool keepBackup)
        {
            if (!File.Exists(staging))
            {
                throw new FileNotFoundException("Không thấy file tạm vừa lưu.", staging);
            }

            if (!File.Exists(target))
            {
                File.Move(staging, target);
                return;
            }

            string? backup = null;
            if (keepBackup)
            {
                backup = Path.ChangeExtension(target, ".bak");
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }
            }

            File.Replace(staging, target, backup, ignoreMetadataErrors: true);
        }
    }
}
