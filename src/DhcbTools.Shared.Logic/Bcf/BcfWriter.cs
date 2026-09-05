using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace DhcbTools.Shared.Logic.Bcf
{
    /// <summary>
    /// Ghi file <b>BCF 2.1</b> (đề xuất B3) — thay việc chụp màn hình va chạm dán vào Word gửi tư vấn.
    /// <para>
    /// Cấu trúc là một file zip: <c>bcf.version</c> ở gốc, mỗi vấn đề một thư mục tên GUID chứa
    /// <c>markup.bcf</c> (bắt buộc), <c>viewpoint.bcfv</c> (góc nhìn) và <c>snapshot.png</c>. Toàn bộ là
    /// XML + zip nên viết được ở tầng thuần và <b>test bằng cách đọc lại chính file vừa ghi</b>.
    /// </para>
    /// <para>Đuôi file: <c>.bcfzip</c> cho 2.0, <c>.bcf</c> từ 2.1 — bản này ghi 2.1.</para>
    /// </summary>
    public static class BcfWriter
    {
        public const string Version = "2.1";

        /// <summary>Đuôi file đúng chuẩn cho phiên bản đang ghi.</summary>
        public const string FileExtension = ".bcf";

        /// <summary>Ghi ra file. Thư mục cha phải có sẵn.</summary>
        public static void Write(string path, IEnumerable<BcfTopic> topics)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            Write(stream, topics);
        }

        /// <summary>Ghi vào một stream (test dùng MemoryStream).</summary>
        public static void Write(Stream stream, IEnumerable<BcfTopic> topics)
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

            AddText(zip, "bcf.version", VersionXml());

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var topic in topics ?? new List<BcfTopic>())
            {
                var guid = topic.Guid == Guid.Empty ? Guid.NewGuid() : topic.Guid;
                var folder = guid.ToString("D");

                // Hai topic cùng guid sẽ ghi đè nhau trong zip mà không báo gì — đổi guid thay vì mất vấn đề.
                while (!used.Add(folder))
                {
                    guid = Guid.NewGuid();
                    folder = guid.ToString("D");
                }

                var hasViewpoint = topic.Camera != null || topic.Components.Count > 0;
                AddText(zip, folder + "/markup.bcf", MarkupXml(topic, guid, hasViewpoint, topic.SnapshotPng != null));

                if (hasViewpoint)
                {
                    AddText(zip, folder + "/viewpoint.bcfv", ViewpointXml(topic));
                }

                if (topic.SnapshotPng != null)
                {
                    var entry = zip.CreateEntry(folder + "/snapshot.png", CompressionLevel.NoCompression);
                    using var entryStream = entry.Open();
                    entryStream.Write(topic.SnapshotPng, 0, topic.SnapshotPng.Length);
                }
            }
        }

        private static string VersionXml()
        {
            return Xml(writer =>
            {
                writer.WriteStartElement("Version");
                writer.WriteAttributeString("VersionId", Version);
                writer.WriteElementString("DetailedVersion", Version);
                writer.WriteEndElement();
            });
        }

        /// <summary>Thứ tự thẻ theo XSD của BCF 2.1 — sai thứ tự là máy đọc nghiêm ngặt từ chối cả file.</summary>
        private static string MarkupXml(BcfTopic topic, Guid guid, bool hasViewpoint, bool hasSnapshot)
        {
            return Xml(writer =>
            {
                writer.WriteStartElement("Markup");

                writer.WriteStartElement("Topic");
                writer.WriteAttributeString("Guid", guid.ToString("D"));
                writer.WriteAttributeString("TopicType", Or(topic.TopicType, "Issue"));
                writer.WriteAttributeString("TopicStatus", Or(topic.TopicStatus, "Open"));

                writer.WriteElementString("Title", topic.Title);
                if (topic.Priority.Length > 0)
                {
                    writer.WriteElementString("Priority", topic.Priority);
                }

                foreach (var label in topic.Labels)
                {
                    writer.WriteElementString("Labels", label);
                }

                writer.WriteElementString("CreationDate", Iso(topic.CreationDate));
                writer.WriteElementString("CreationAuthor", Or(topic.Author, "DHCB Tools"));
                if (topic.Description.Length > 0)
                {
                    writer.WriteElementString("Description", topic.Description);
                }

                writer.WriteEndElement();

                if (topic.Comment.Length > 0)
                {
                    writer.WriteStartElement("Comment");
                    writer.WriteAttributeString("Guid", Guid.NewGuid().ToString("D"));
                    writer.WriteElementString("Date", Iso(topic.CreationDate));
                    writer.WriteElementString("Author", Or(topic.Author, "DHCB Tools"));
                    writer.WriteElementString("Comment", topic.Comment);
                    writer.WriteEndElement();
                }

                if (hasViewpoint)
                {
                    writer.WriteStartElement("Viewpoints");
                    writer.WriteAttributeString("Guid", Guid.NewGuid().ToString("D"));
                    writer.WriteElementString("Viewpoint", "viewpoint.bcfv");
                    if (hasSnapshot)
                    {
                        writer.WriteElementString("Snapshot", "snapshot.png");
                    }

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            });
        }

        private static string ViewpointXml(BcfTopic topic)
        {
            return Xml(writer =>
            {
                writer.WriteStartElement("VisualizationInfo");
                writer.WriteAttributeString("Guid", Guid.NewGuid().ToString("D"));

                if (topic.Components.Count > 0)
                {
                    writer.WriteStartElement("Components");
                    writer.WriteStartElement("Selection");
                    foreach (var component in topic.Components)
                    {
                        writer.WriteStartElement("Component");
                        writer.WriteAttributeString("IfcGuid", component.IfcGuid);
                        if (!string.IsNullOrEmpty(component.OriginatingSystem))
                        {
                            writer.WriteElementString("OriginatingSystem", component.OriginatingSystem);
                        }

                        if (!string.IsNullOrEmpty(component.AuthoringToolId))
                        {
                            writer.WriteElementString("AuthoringToolId", component.AuthoringToolId);
                        }

                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();

                    writer.WriteStartElement("Visibility");
                    writer.WriteAttributeString("DefaultVisibility", "true");
                    writer.WriteEndElement();

                    writer.WriteEndElement();
                }

                if (topic.Camera != null)
                {
                    var camera = topic.Camera;
                    writer.WriteStartElement("PerspectiveCamera");
                    Point(writer, "CameraViewPoint", camera.X, camera.Y, camera.Z);
                    Point(writer, "CameraDirection", camera.DirectionX, camera.DirectionY, camera.DirectionZ);
                    Point(writer, "CameraUpVector", camera.UpX, camera.UpY, camera.UpZ);
                    writer.WriteElementString("FieldOfView", Number(camera.FieldOfViewDeg));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            });
        }

        private static void Point(XmlWriter writer, string name, double x, double y, double z)
        {
            writer.WriteStartElement(name);
            writer.WriteElementString("X", Number(x));
            writer.WriteElementString("Y", Number(y));
            writer.WriteElementString("Z", Number(z));
            writer.WriteEndElement();
        }

        /// <summary>Số luôn dấu chấm thập phân — máy đọc BCF không biết máy nào đang chạy culture nào.</summary>
        private static string Number(double value) =>
            Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);

        private static string Iso(DateTime date) =>
            (date.Kind == DateTimeKind.Utc ? date : date.ToUniversalTime()).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        private static string Or(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static string Xml(Action<XmlWriter> body)
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = false,
            };

            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, settings))
            {
                writer.WriteStartDocument();
                body(writer);
                writer.WriteEndDocument();
            }

            // XmlWriter ghi vào StringBuilder thì khai báo là encoding="utf-16"; file thật ghi bằng UTF-8.
            return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
        }

        private static void AddText(ZipArchive zip, string path, string content)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var stream = entry.Open();
            var bytes = new UTF8Encoding(false).GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
