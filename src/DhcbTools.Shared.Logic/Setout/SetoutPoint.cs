using System;

namespace DhcbTools.Shared.Logic.Setout
{
    /// <summary>
    /// Một điểm định vị thô do Core đọc từ mô hình (đề xuất A1 — <c>SetoutExport</c>): toạ độ đã ở
    /// <b>hệ đầu ra</b> (Survey hoặc nội bộ) và đã đổi sang mm. Tầng thuần chỉ đặt tên, sắp xếp,
    /// định dạng — không biết Revit dùng feet hay <c>Transform</c> nào.
    /// </summary>
    public sealed class SetoutSource
    {
        public SetoutSource(string kind, double eastMm, double northMm, double elevationMm)
        {
            Kind = kind ?? string.Empty;
            EastMm = eastMm;
            NorthMm = northMm;
            ElevationMm = elevationMm;
        }

        /// <summary>Điểm này là gì trên phần tử: <c>tim</c>, <c>đầu</c>, <c>cuối</c>, <c>giữa</c>, <c>tâm hộp bao</c>, <c>giao trục</c>.</summary>
        public string Kind { get; }

        /// <summary>Đông (X) — mm.</summary>
        public double EastMm { get; }

        /// <summary>Bắc (Y) — mm.</summary>
        public double NorthMm { get; }

        /// <summary>Cao độ (Z) — mm.</summary>
        public double ElevationMm { get; }

        /// <summary>ElementId để truy ngược về mô hình; 0 cho điểm không thuộc phần tử nào (giao trục).</summary>
        public long ElementId { get; set; }

        public string Category { get; set; } = string.Empty;

        public string Family { get; set; } = string.Empty;

        public string TypeName { get; set; } = string.Empty;

        public string Level { get; set; } = string.Empty;

        public string Mark { get; set; } = string.Empty;

        /// <summary>Cặp trục cho điểm giao trục, ví dụ <c>A-1</c>; rỗng với điểm của phần tử.</summary>
        public string Grid { get; set; } = string.Empty;

        /// <summary>Mã ngắn; rỗng thì lấy theo <see cref="SetoutCodes.For"/> từ category.</summary>
        public string Code { get; set; } = string.Empty;
    }

    /// <summary>Điểm định vị đã đặt tên, sẵn sàng ghi ra CSV/DXF.</summary>
    public sealed class SetoutPoint
    {
        public SetoutPoint(string name, double eastMm, double northMm, double elevationMm)
        {
            Name = name ?? string.Empty;
            EastMm = eastMm;
            NorthMm = northMm;
            ElevationMm = elevationMm;
        }

        /// <summary>Tên điểm — đã làm sạch cho máy toàn đạc (không khoảng trắng, không dấu phẩy/nháy).</summary>
        public string Name { get; }

        public double EastMm { get; }

        public double NorthMm { get; }

        public double ElevationMm { get; }

        public string Description { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Level { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public long ElementId { get; set; }
    }
}
