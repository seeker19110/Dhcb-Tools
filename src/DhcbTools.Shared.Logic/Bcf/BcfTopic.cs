using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Bcf
{
    /// <summary>Một phần tử được chỉ tới trong góc nhìn BCF.</summary>
    public sealed class BcfComponent
    {
        public BcfComponent(string ifcGuid, string? authoringToolId = null, string? originatingSystem = null)
        {
            IfcGuid = ifcGuid ?? string.Empty;
            AuthoringToolId = authoringToolId;
            OriginatingSystem = originatingSystem;
        }

        /// <summary>IFC GUID 22 ký tự — xem <see cref="Bcf.IfcGuid"/>.</summary>
        public string IfcGuid { get; }

        /// <summary>ElementId của Revit, để mở lại đúng phần tử trong chính mô hình đã sinh ra file.</summary>
        public string? AuthoringToolId { get; }

        public string? OriginatingSystem { get; }
    }

    /// <summary>
    /// Camera của góc nhìn BCF. Đơn vị <b>mét</b> theo đặc tả — người gọi tự đổi từ feet/mm, tầng thuần
    /// không đoán đơn vị của ai.
    /// </summary>
    public sealed class BcfCamera
    {
        public BcfCamera(double x, double y, double z, double dx, double dy, double dz, double ux, double uy, double uz, double fieldOfViewDeg = 60)
        {
            X = x; Y = y; Z = z;
            DirectionX = dx; DirectionY = dy; DirectionZ = dz;
            UpX = ux; UpY = uy; UpZ = uz;
            FieldOfViewDeg = fieldOfViewDeg;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public double DirectionX { get; }

        public double DirectionY { get; }

        public double DirectionZ { get; }

        public double UpX { get; }

        public double UpY { get; }

        public double UpZ { get; }

        public double FieldOfViewDeg { get; }

        /// <summary>
        /// Camera đứng chéo phía trên nhìn vào một điểm (đơn vị mét). Hướng nhìn là vector đơn vị, và
        /// vector "lên" được dựng vuông góc với nó — máy đọc BCF nào cũng yêu cầu hai vector này không
        /// song song, còn hằng "lên = trục Z" thì hỏng đúng lúc camera nhìn thẳng xuống.
        /// </summary>
        public static BcfCamera LookingAt(double targetX, double targetY, double targetZ, double distance = 8, double fieldOfViewDeg = 60)
        {
            if (distance <= 0)
            {
                distance = 8;
            }

            // Đứng chéo 45° trên mặt bằng và cao hơn điểm nhìn — góc quen thuộc của một ảnh chụp va chạm.
            var k = distance / Math.Sqrt(3.0);
            double px = targetX - k, py = targetY - k, pz = targetZ + k;

            double dx = targetX - px, dy = targetY - py, dz = targetZ - pz;
            var length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            dx /= length; dy /= length; dz /= length;

            // up = normalize(z × d × d) — thành phần của trục Z vuông góc với hướng nhìn.
            double ux = -dz * dx, uy = -dz * dy, uz = 1 - dz * dz;
            var upLength = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            if (upLength < 1e-9)
            {
                // Nhìn thẳng đứng: lấy trục Y làm "lên".
                ux = 0; uy = 1; uz = 0;
            }
            else
            {
                ux /= upLength; uy /= upLength; uz /= upLength;
            }

            return new BcfCamera(px, py, pz, dx, dy, dz, ux, uy, uz, fieldOfViewDeg);
        }
    }

    /// <summary>
    /// Một vấn đề trong file BCF: tiêu đề, mô tả, phần tử liên quan, góc nhìn, ảnh chụp.
    /// Dùng chung cho va chạm (<c>ClashDetection</c>), vi phạm quy tắc (<c>ParameterRuleCheck</c>) và
    /// cảnh báo Revit (<c>WarningsExport</c>) — cả ba đều là "một chỗ cần người xem và sửa".
    /// </summary>
    public sealed class BcfTopic
    {
        public BcfTopic(string title)
        {
            Title = title ?? string.Empty;
        }

        /// <summary>Guid của topic; <c>Guid.Empty</c> thì bộ ghi tự sinh.</summary>
        public Guid Guid { get; set; } = Guid.NewGuid();

        public string Title { get; }

        public string Description { get; set; } = string.Empty;

        /// <summary>Loại vấn đề: <c>Clash</c>, <c>Issue</c>, <c>Warning</c>, <c>Request</c>…</summary>
        public string TopicType { get; set; } = "Issue";

        public string TopicStatus { get; set; } = "Open";

        public string Priority { get; set; } = string.Empty;

        public string Author { get; set; } = "DHCB Tools";

        public DateTime CreationDate { get; set; } = DateTime.UtcNow;

        public List<string> Labels { get; } = new List<string>();

        public List<BcfComponent> Components { get; } = new List<BcfComponent>();

        public BcfCamera? Camera { get; set; }

        /// <summary>Ảnh PNG của góc nhìn; null = không có ảnh (topic vẫn hợp lệ).</summary>
        public byte[]? SnapshotPng { get; set; }

        /// <summary>Bình luận đầu tiên; rỗng = không ghi thẻ Comment.</summary>
        public string Comment { get; set; } = string.Empty;
    }
}
