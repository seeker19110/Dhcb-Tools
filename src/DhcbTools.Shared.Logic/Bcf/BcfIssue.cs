using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Bcf
{
    /// <summary>Một điểm trong không gian, đơn vị **mét** (BCF quy định mét, không phải mm hay foot).</summary>
    public sealed class BcfPoint
    {
        public BcfPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public BcfPoint Normalised()
        {
            var len = Length;
            // Vector 0 không có hướng: trả về hướng nhìn mặc định thay vì sinh NaN — NaN lọt vào file
            // .bcf thì Navisworks/Solibri mở ra là camera bay đi đâu không ai biết.
            return len < 1e-12 ? new BcfPoint(0, 0, -1) : new BcfPoint(X / len, Y / len, Z / len);
        }

        public BcfPoint Plus(BcfPoint other) => new BcfPoint(X + other.X, Y + other.Y, Z + other.Z);

        public BcfPoint Times(double factor) => new BcfPoint(X * factor, Y * factor, Z * factor);
    }

    /// <summary>Phần tử được tô sáng khi mở vấn đề: id trong file gốc và/hoặc IfcGuid.</summary>
    public sealed class BcfComponent
    {
        public BcfComponent(string authoringToolId, string? ifcGuid = null, string? originatingSystem = null)
        {
            AuthoringToolId = authoringToolId ?? string.Empty;
            IfcGuid = ifcGuid;
            OriginatingSystem = originatingSystem;
        }

        /// <summary>ElementId phía Revit — thứ duy nhất luôn có; IfcGuid chỉ có sau khi xuất IFC.</summary>
        public string AuthoringToolId { get; }

        public string? IfcGuid { get; }

        public string? OriginatingSystem { get; }
    }

    /// <summary>Một vấn đề (topic) trong file BCF: tiêu đề, mô tả, camera, phần tử liên quan.</summary>
    public sealed class BcfIssue
    {
        public BcfIssue(string guid, string title)
        {
            Guid = guid;
            Title = title;
        }

        /// <summary>GUID của topic — cũng là tên thư mục trong zip.</summary>
        public string Guid { get; }

        public string Title { get; }

        public string? Description { get; set; }

        public string TopicType { get; set; } = "Clash";

        public string TopicStatus { get; set; } = "Open";

        public string Priority { get; set; } = "Normal";

        public string? Stage { get; set; }

        public string Author { get; set; } = "DHCB Tools";

        public DateTime CreationDate { get; set; } = DateTime.UtcNow;

        public List<string> Labels { get; } = new List<string>();

        /// <summary>Tâm va chạm, đơn vị mét. Null = không kèm góc nhìn.</summary>
        public BcfPoint? Target { get; set; }

        /// <summary>Hướng nhìn tới <see cref="Target"/>; null = hướng chéo mặc định.</summary>
        public BcfPoint? ViewDirection { get; set; }

        /// <summary>Khoảng cách camera tới tâm, mét.</summary>
        public double ViewDistance { get; set; } = 5;

        public List<BcfComponent> Components { get; } = new List<BcfComponent>();

        /// <summary>Ảnh PNG chụp sẵn cho topic (tuỳ chọn — BCF 2.1 không bắt buộc).</summary>
        public byte[]? Snapshot { get; set; }
    }
}
