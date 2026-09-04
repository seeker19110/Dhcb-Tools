using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Checks;

namespace DhcbTools.Core.Checks;

/// <summary>
/// Thu thập sự kiện từ mô hình cho <see cref="Precondition"/> — phần quyết định nằm ở tầng thuần
/// (có test trên CI), phần đọc Revit nằm ở đây.
/// <para>
/// Chỗ vá của bug #14 nằm trong <c>BatchJobRunner.Open()</c>: mở file thì nạp lại link. Nhưng đường
/// <b>Ribbon</b> và <b>Bridge</b> không đi qua đó — kỹ sư tự mở một bản sao có link chưa nạp rồi bấm
/// <c>ClashDetection</c> vẫn nhận đúng con số 0 giả như cũ. Tiền đề này bịt phần còn lại của lớp lỗi.
/// </para>
/// </summary>
public static class RevitPrecondition
{
    /// <summary>
    /// Link chưa nạp thì mọi phần tử bên trong vô hình với lệnh. Đếm theo <see cref="RevitLinkInstance"/>
    /// vì đó chính là thứ các lệnh duyệt qua.
    /// </summary>
    /// <param name="tatLinkBang">Tên trường config để cố ý bỏ qua link, ví dụ <c>includeLinkedModels</c>.</param>
    public static PreconditionResult LinkedModels(Document document, string command, string tatLinkBang = "includeLinkedModels")
    {
        var instances = new FilteredElementCollector(document)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .ToList();

        var unloaded = instances
            .Where(i => i.GetLinkDocument() == null)
            .Select(i => i.Name)
            .ToList();

        return Precondition.LinkedModels(command, instances.Count, unloaded, tatLinkBang);
    }

    /// <summary>
    /// Gắn kết luận vào kết quả: chặn thì đổi kết quả thành lỗi (trả <c>true</c> để người gọi
    /// <c>return</c> ngay), cảnh báo thì thêm vào <c>Messages</c>.
    /// </summary>
    public static bool Blocks(PreconditionResult precondition, CommandResult result)
    {
        if (precondition.Blocks)
        {
            result.Success = false;
            result.Summary = precondition.Message;
            result.Errors.Add(precondition.Message);
            return true;
        }

        if (precondition.Warns)
        {
            result.Messages.Add(precondition.Message);
        }

        return false;
    }
}
