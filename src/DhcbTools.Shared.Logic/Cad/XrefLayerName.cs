using System;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>
    /// Tên layer phụ thuộc xref dạng <c>XREF|LAYER</c> (vd <c>1F|M-HVAC-DUCT-1</c>). AutoCAD đặt tên này khi gắn xref;
    /// <c>|</c> là ký tự cấm trong tên symbol nên <c>ValidateSymbolName</c> từ chối — đúng cho việc TẠO/ĐỔI TÊN layer,
    /// nhưng sai cho việc đổi trạng thái (đóng băng, tắt, khoá, màu) của layer xref đã có: phiên bóc khối lượng
    /// 2026-11-13 phải vòng qua COM chỉ để đóng băng layer xref.
    /// </summary>
    public static class XrefLayerName
    {
        /// <summary>
        /// Tách <c>XREF|LAYER</c>: đúng một <c>|</c>, hai phía đều có chữ (đã trim). Lồng xref vẫn chỉ một <c>|</c> vì
        /// AutoCAD đặt tên theo xref chứa trực tiếp layer đó.
        /// </summary>
        public static bool TrySplit(string? name, out string xref, out string layer)
        {
            xref = string.Empty;
            layer = string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var text = name!.Trim();
            var bar = text.IndexOf('|');
            if (bar <= 0 || bar == text.Length - 1 || text.IndexOf('|', bar + 1) >= 0)
            {
                return false;
            }

            xref = text.Substring(0, bar).Trim();
            layer = text.Substring(bar + 1).Trim();
            return xref.Length > 0 && layer.Length > 0;
        }

        /// <summary>Tên có dạng layer phụ thuộc xref.</summary>
        public static bool IsDependentName(string? name) => TrySplit(name, out _, out _);

        /// <summary>
        /// Vì sao một dòng nhập trỏ tới layer xref không áp được — <c>null</c> = áp được (layer có sẵn và đúng là layer
        /// phụ thuộc xref). Không bao giờ tạo layer có <c>|</c>: layer đó chỉ AutoCAD sinh ra khi gắn xref.
        /// </summary>
        public static string? ImportProblem(string name, bool exists, bool isDependent)
        {
            if (!exists)
            {
                return $"layer xref \"{name}\" không có trong bản vẽ — không tạo layer có '|' (layer này do AutoCAD sinh khi gắn xref; gắn/nạp lại xref trước).";
            }

            return isDependent
                ? null
                : $"layer \"{name}\" có '|' nhưng không phải layer phụ thuộc xref — không sửa.";
        }

        /// <summary>
        /// Nhắc VISRETAIN: = 0 thì AutoCAD lấy lại trạng thái layer từ file xref mỗi lần nạp lại, mọi đổi đóng băng/tắt/
        /// màu vừa ghi sẽ mất khi mở lại bản vẽ.
        /// </summary>
        public static string VisretainWarning(int changedXrefLayers) =>
            $"VISRETAIN = 0: {changedXrefLayers} layer xref vừa đổi sẽ trở lại như file xref khi nạp lại/mở lại bản vẽ — đặt VISRETAIN = 1 để giữ.";
    }
}
