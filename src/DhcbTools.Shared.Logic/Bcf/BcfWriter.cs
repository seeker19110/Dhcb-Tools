using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace DhcbTools.Shared.Logic.Bcf
{
    /// <summary>Thông tin dự án ghi vào <c>project.bcfp</c> và dùng làm tác giả mặc định.</summary>
    public sealed class BcfProject
    {
        public string? ProjectId { get; set; }

        public string? Name { get; set; }
    }

    /// <summary>
    /// Ghi file BCF 2.1 (BIM Collaboration Format — chuẩn mở của buildingSMART) từ danh sách vấn đề.
    /// <para>
    /// Vì sao thuần: toàn bộ .bcf là **zip + XML**, không cần <c>Document</c> nào — nên viết được ở đây
    /// và test bằng cách đọc lại chính file vừa ghi, thay vì phải mở Revit mới biết đúng sai.
    /// </para>
    /// <para>
    /// Đuôi file: <c>.bcfzip</c> cho 1.0/2.0, <c>.bcf</c> từ 2.1 — bản này ghi 2.1.
    /// </para>
    /// </summary>
    public static class BcfWriter
    {
        /// <summary>Phiên bản BCF sinh ra.</summary>
        public const string Version = "2.1";

        private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

        /// <summary>
        /// GUID **ổn định** sinh từ khoá va chạm: xuất lại cùng một va chạm thì ra đúng topic cũ, nên
        /// nhận xét/trạng thái mà người duyệt đã ghi trong phần mềm của họ không bị tách thành vấn đề mới.
        /// </summary>
        public static string GuidFromKey(string key)
        {
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key ?? string.Empty));
                var bytes = new byte[16];
                Array.Copy(hash, bytes, 16);
                // Đánh dấu version 5 / variant RFC 4122 để đây là một UUID hợp lệ chứ không phải 16 byte tuỳ ý.
                // Nibble phiên bản nằm ở byte[7]: Guid(byte[]) đọc ba trường đầu theo thứ tự little-endian,
                // nên byte[6] (đúng vị trí trong RFC 4122) lại rơi vào chỗ khác khi in ra chuỗi.
                bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
                bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
                return new Guid(bytes).ToString("D");
            }
        }

        /// <summary>Ghi file .bcf. Thư mục đích được tạo nếu chưa có; file cũ bị ghi đè.</summary>
        public static void WriteFile(string path, IEnumerable<BcfIssue> issues, BcfProject? project = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Thiếu đường dẫn file .bcf.", nameof(path));

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir!);

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Write(stream, issues, project);
            }
        }

        /// <summary>Ghi nội dung .bcf vào một stream đang mở (dùng cho test và cho đường ghi vào bộ nhớ).</summary>
        public static void Write(Stream stream, IEnumerable<BcfIssue> issues, BcfProject? project = null)
        {
            var list = (issues ?? Enumerable.Empty<BcfIssue>()).ToList();

            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddText(zip, "bcf.version", VersionXml());
                AddText(zip, "extensions.xml", ExtensionsXml(list));
                if (project != null && (!string.IsNullOrEmpty(project.ProjectId) || !string.IsNullOrEmpty(project.Name)))
                {
                    AddText(zip, "project.bcfp", ProjectXml(project));
                }

                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var issue in list)
                {
                    var folder = string.IsNullOrWhiteSpace(issue.Guid) ? Guid.NewGuid().ToString("D") : issue.Guid.Trim();
                    // Hai topic cùng GUID = hai thư mục cùng tên trong zip: phần mềm đọc BCF hoặc bỏ bớt
                    // một vấn đề, hoặc báo file hỏng. Thà đổi tên còn hơn mất một va chạm.
                    if (!used.Add(folder))
                    {
                        folder = Guid.NewGuid().ToString("D");
                        used.Add(folder);
                    }

                    AddText(zip, folder + "/markup.bcf", MarkupXml(issue, folder));
                    if (issue.Target != null)
                    {
                        AddText(zip, folder + "/viewpoint.bcfv", ViewpointXml(issue, folder));
                    }

                    if (issue.Snapshot != null && issue.Snapshot.Length > 0)
                    {
                        var entry = zip.CreateEntry(folder + "/snapshot.png", CompressionLevel.Fastest);
                        using (var target = entry.Open())
                        {
                            target.Write(issue.Snapshot, 0, issue.Snapshot.Length);
                        }
                    }
                }
            }
        }

        internal static string VersionXml()
            => Serialise(new XElement("Version",
                new XAttribute("VersionId", Version),
                new XElement("DetailedVersion", Version)));

        internal static string ProjectXml(BcfProject project)
            => Serialise(new XElement("ProjectExtension",
                new XElement("Project",
                    new XAttribute("ProjectId", string.IsNullOrEmpty(project.ProjectId) ? Guid.NewGuid().ToString("D") : project.ProjectId!),
                    new XElement("Name", project.Name ?? string.Empty)),
                new XElement("ExtensionSchema", string.Empty)));

        /// <summary>
        /// Danh sách giá trị hợp lệ cho status/type/nhãn. Phần mềm đọc BCF lấy đây làm danh sách chọn;
        /// nhãn nào dùng trong markup mà không khai ở đây thì hoặc bị bỏ, hoặc bị báo là giá trị lạ.
        /// </summary>
        internal static string ExtensionsXml(IEnumerable<BcfIssue> issues)
        {
            var list = issues.ToList();
            var statuses = Distinct(list.Select(i => i.TopicStatus).Concat(new[] { "Open", "Closed" }));
            var types = Distinct(list.Select(i => i.TopicType).Concat(new[] { "Clash", "Issue" }));
            var priorities = Distinct(list.Select(i => i.Priority).Concat(new[] { "Low", "Normal", "High" }));
            var labels = Distinct(list.SelectMany(i => i.Labels));
            var users = Distinct(list.Select(i => i.Author));
            var stages = Distinct(list.Select(i => i.Stage));

            return Serialise(new XElement("Extensions",
                new XElement("TopicTypes", types.Select(v => new XElement("TopicType", v))),
                new XElement("TopicStatuses", statuses.Select(v => new XElement("TopicStatus", v))),
                new XElement("Priorities", priorities.Select(v => new XElement("Priority", v))),
                new XElement("TopicLabels", labels.Select(v => new XElement("TopicLabel", v))),
                new XElement("Users", users.Select(v => new XElement("User", v))),
                new XElement("Stages", stages.Select(v => new XElement("Stage", v)))));
        }

        /// <summary>
        /// <c>markup.bcf</c>. Thứ tự các thẻ con của Topic là **bắt buộc theo XSD 2.1**
        /// (Title → Priority → Index → Labels → CreationDate → CreationAuthor → … → Stage → Description);
        /// đảo thứ tự thì file mở được ở phần mềm dễ tính và bị từ chối ở phần mềm kiểm schema.
        /// </summary>
        internal static string MarkupXml(BcfIssue issue, string? folder = null)
        {
            var guid = folder ?? issue.Guid;
            var topic = new XElement("Topic",
                new XAttribute("Guid", guid),
                new XAttribute("TopicType", issue.TopicType),
                new XAttribute("TopicStatus", issue.TopicStatus),
                new XElement("Title", issue.Title ?? string.Empty),
                new XElement("Priority", issue.Priority));
            foreach (var label in issue.Labels.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                topic.Add(new XElement("Labels", label));
            }

            topic.Add(new XElement("CreationDate", Iso(issue.CreationDate)));
            topic.Add(new XElement("CreationAuthor", issue.Author ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(issue.Stage)) topic.Add(new XElement("Stage", issue.Stage));
            if (!string.IsNullOrWhiteSpace(issue.Description)) topic.Add(new XElement("Description", issue.Description));

            var markup = new XElement("Markup", new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName), topic);

            if (issue.Target != null)
            {
                var viewpoints = new XElement("Viewpoints",
                    new XAttribute("Guid", guid),
                    new XElement("Viewpoint", "viewpoint.bcfv"));
                if (issue.Snapshot != null && issue.Snapshot.Length > 0)
                {
                    viewpoints.Add(new XElement("Snapshot", "snapshot.png"));
                }

                markup.Add(viewpoints);
            }

            return Serialise(markup);
        }

        /// <summary>
        /// <c>viewpoint.bcfv</c>: phần tử được chọn + camera phối cảnh nhìn thẳng vào tâm va chạm.
        /// Toạ độ ở đây là **mét** — người gọi phải quy đổi trước, lớp này không đoán đơn vị.
        /// </summary>
        internal static string ViewpointXml(BcfIssue issue, string? folder = null)
        {
            var target = issue.Target ?? new BcfPoint(0, 0, 0);
            var direction = (issue.ViewDirection ?? new BcfPoint(-1, 1, -1)).Normalised();
            var distance = issue.ViewDistance > 0 ? issue.ViewDistance : 5;
            var eye = target.Plus(direction.Times(-distance));

            var info = new XElement("VisualizationInfo",
                new XAttribute("Guid", folder ?? issue.Guid));

            if (issue.Components.Count > 0)
            {
                var selection = new XElement("Selection",
                    issue.Components.Select(c =>
                    {
                        var component = new XElement("Component");
                        if (!string.IsNullOrWhiteSpace(c.IfcGuid)) component.Add(new XAttribute("IfcGuid", c.IfcGuid));
                        if (!string.IsNullOrWhiteSpace(c.OriginatingSystem)) component.Add(new XElement("OriginatingSystem", c.OriginatingSystem));
                        component.Add(new XElement("AuthoringToolId", c.AuthoringToolId));
                        return component;
                    }));
                info.Add(new XElement("Components",
                    selection,
                    new XElement("Visibility", new XAttribute("DefaultVisibility", "true"))));
            }

            info.Add(new XElement("PerspectiveCamera",
                Point("CameraViewPoint", eye),
                Point("CameraDirection", direction),
                Point("CameraUpVector", UpVector(direction)),
                new XElement("FieldOfView", Num(60))));

            return Serialise(info);
        }

        /// <summary>
        /// Vector "lên" vuông góc với hướng nhìn. Nhìn thẳng đứng (trần/sàn) thì trục Z không dùng làm
        /// gốc được nữa — lấy trục Y, nếu không camera nhận vector 0 và ảnh xoay lung tung.
        /// </summary>
        internal static BcfPoint UpVector(BcfPoint direction)
        {
            var d = direction.Normalised();
            var reference = Math.Abs(d.Z) > 0.999 ? new BcfPoint(0, 1, 0) : new BcfPoint(0, 0, 1);
            var dot = (d.X * reference.X) + (d.Y * reference.Y) + (d.Z * reference.Z);
            return reference.Plus(d.Times(-dot)).Normalised();
        }

        private static XElement Point(string name, BcfPoint p)
            => new XElement(name, new XElement("X", Num(p.X)), new XElement("Y", Num(p.Y)), new XElement("Z", Num(p.Z)));

        private static string Num(double value)
        {
            var rounded = Math.Round(value, 6);
            if (rounded == 0) rounded = 0; // bỏ -0
            return rounded.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string Iso(DateTime value)
            => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        private static IEnumerable<string> Distinct(IEnumerable<string?> values)
            => values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim())
                     .Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal);

        private static string Serialise(XElement root)
            => new XDeclaration("1.0", "UTF-8", null).ToString() + Environment.NewLine + root;

        private static void AddText(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }
    }
}
