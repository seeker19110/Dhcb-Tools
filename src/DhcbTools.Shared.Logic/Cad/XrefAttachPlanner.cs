using System;
using System.Collections.Generic;

namespace DhcbTools.Shared.Logic.Cad
{
    /// <summary>Yêu cầu gắn một xref (đọc từ config của lệnh <c>XrefAttach</c>).</summary>
    public sealed class XrefAttachRequest
    {
        public string? Path { get; set; }

        public string? Name { get; set; }

        public double[]? Position { get; set; }

        public double? Scale { get; set; }

        public double? Rotation { get; set; }

        public string? Layer { get; set; }
    }

    /// <summary>Kết quả kiểm: tên block xref sẽ dùng, đường dẫn đầy đủ, hoặc danh sách lỗi.</summary>
    public sealed class XrefAttachPlan
    {
        public string Name { get; set; } = string.Empty;

        public string FullPath { get; set; } = string.Empty;

        public List<string> Errors { get; } = new List<string>();

        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Kiểm yêu cầu gắn xref trước khi đụng bản vẽ: file DWG có thật, không tự gắn chính mình, tên block không có ký
    /// tự '|' (tên đó AutoCAD dùng làm tiền tố cho layer xref), tỉ lệ dương, điểm chèn hợp lệ. Phiên bóc khối lượng
    /// 2026-11-13 phải gắn overlay qua COM vì Bridge chưa có lệnh này.
    /// </summary>
    public static class XrefAttachPlanner
    {
        private static readonly char[] Forbidden = { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' };

        /// <param name="fileExists">Hàm kiểm file tồn tại (test truyền giả).</param>
        /// <param name="hostPath">Đường dẫn bản vẽ đang mở — gắn chính nó là tham chiếu vòng.</param>
        public static XrefAttachPlan Plan(XrefAttachRequest request, Func<string, bool> fileExists, string? hostPath)
        {
            var plan = new XrefAttachPlan();
            var raw = request.Path?.Trim().Trim('"') ?? string.Empty;
            if (raw.Length == 0)
            {
                plan.Errors.Add("Thiếu \"path\" — đường dẫn file DWG cần gắn.");
            }
            else if (!System.IO.Path.IsPathRooted(raw))
            {
                plan.Errors.Add($"\"path\" phải là đường dẫn đầy đủ (vd C:\\...\\1F.dwg), nhận \"{raw}\".");
            }
            else if (!string.Equals(System.IO.Path.GetExtension(raw), ".dwg", StringComparison.OrdinalIgnoreCase))
            {
                plan.Errors.Add($"\"{raw}\" không phải file .dwg.");
            }
            else
            {
                plan.FullPath = System.IO.Path.GetFullPath(raw);
                if (!fileExists(plan.FullPath))
                {
                    plan.Errors.Add($"Không tìm thấy file: \"{plan.FullPath}\".");
                }
                else if (!string.IsNullOrWhiteSpace(hostPath)
                         && string.Equals(System.IO.Path.GetFullPath(hostPath), plan.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    plan.Errors.Add("Không gắn bản vẽ vào chính nó (tham chiếu vòng).");
                }
            }

            var name = string.IsNullOrWhiteSpace(request.Name)
                ? (plan.FullPath.Length > 0 ? System.IO.Path.GetFileNameWithoutExtension(plan.FullPath) : string.Empty)
                : request.Name!.Trim();
            plan.Name = name;
            if (name.Length > 0 && (name.IndexOfAny(Forbidden) >= 0 || name.Length > 255))
            {
                plan.Errors.Add($"Tên xref \"{name}\" có ký tự cấm (< > / \\ \" : ; ? * | , = `) hoặc quá 255 ký tự — đặt \"name\" khác.");
            }

            if (request.Position != null && !EntityEditPlanner.IsPoint(request.Position))
            {
                plan.Errors.Add("\"position\" phải dạng [x, y] hoặc [x, y, z].");
            }

            if (request.Scale != null && !(request.Scale > 0 && !double.IsInfinity(request.Scale.Value)))
            {
                plan.Errors.Add("\"scale\" phải > 0.");
            }

            if (request.Rotation != null && (double.IsNaN(request.Rotation.Value) || double.IsInfinity(request.Rotation.Value)))
            {
                plan.Errors.Add("\"rotation\" phải là số (độ).");
            }

            if (request.Layer != null && (request.Layer.Trim().Length == 0 || request.Layer.IndexOfAny(Forbidden) >= 0))
            {
                plan.Errors.Add($"\"layer\" \"{request.Layer}\" rỗng hoặc có ký tự cấm — bỏ trường để dùng layer hiện hành.");
            }

            return plan;
        }

        /// <summary>
        /// Xref cùng tên đã có trong bản vẽ: cùng file thì chèn thêm một lần nữa (như XATTACH chọn lại xref cũ); khác
        /// file thì từ chối — đổi đường dẫn âm thầm làm hỏng mọi bản chèn cũ. Tên đó là block thường cũng từ chối.
        /// </summary>
        public static string? ExistingNameProblem(string name, bool isXref, string? existingPath, string wantedPath)
        {
            if (!isXref)
            {
                return $"Tên \"{name}\" đã là block thường trong bản vẽ — đặt \"name\" khác cho xref.";
            }

            return SamePath(existingPath, wantedPath)
                ? null
                : $"Xref \"{name}\" đã có nhưng trỏ tới \"{existingPath}\" — đặt \"name\" khác hoặc đổi đường dẫn bằng XREF.";
        }

        /// <summary>So đường dẫn xref: bỏ nháy, không phân biệt hoa thường, so tên file khi xref lưu đường dẫn tương đối.</summary>
        public static bool SamePath(string? stored, string wanted)
        {
            var a = (stored ?? string.Empty).Trim().Trim('"');
            if (a.Length == 0)
            {
                return false;
            }

            if (!System.IO.Path.IsPathRooted(a))
            {
                return string.Equals(System.IO.Path.GetFileName(a), System.IO.Path.GetFileName(wanted), StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(wanted), StringComparison.OrdinalIgnoreCase);
        }
    }
}
