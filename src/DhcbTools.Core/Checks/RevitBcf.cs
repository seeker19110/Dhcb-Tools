using Autodesk.Revit.DB;
using DhcbTools.Shared.Logic.Bcf;

namespace DhcbTools.Core.Checks;

/// <summary>
/// Cầu nối giữa mô hình Revit và tầng thuần <see cref="BcfWriter"/> (đề xuất B3): lấy IFC GUID của
/// phần tử, dựng camera, ghi file và báo lỗi bằng tiếng Việt.
/// <para>
/// Đây là phần <b>duy nhất</b> chạm Revit trong đường xuất BCF, nên ba lệnh <c>ClashDetection</c>,
/// <c>ParameterRuleCheck</c> và <c>WarningsExport</c> dùng chung một cách sinh file — không có ba bản
/// sao lệch nhau như từng xảy ra với tra family của Sleeve và Hanger.
/// </para>
/// </summary>
public static class RevitBcf
{
    /// <summary>Số vấn đề tối đa ghi vào một file BCF — mở 3.000 topic trong Solibri là treo máy người nhận.</summary>
    public const int MaxTopics = 500;

    /// <summary>
    /// Phần tử → component của BCF. IFC GUID lấy bằng <c>ExportUtils.GetExportId</c> — <b>đúng guid mà
    /// bộ xuất IFC của Revit dùng</b>, nên phần tử trong file BCF khớp với phần tử trong file IFC đã nộp.
    /// Phần tử của model liên kết lấy guid theo document của chính link đó.
    /// </summary>
    public static BcfComponent? ComponentOf(Element? element)
    {
        if (element == null)
        {
            return null;
        }

        try
        {
            var guid = ExportUtils.GetExportId(element.Document, element.Id);
            return new BcfComponent(IfcGuid.From(guid), RevitCompat.IdValue(element.Id).ToString(), "DHCB Tools");
        }
        catch (Exception)
        {
            // Không lấy được guid thì bỏ component đó chứ không bỏ cả vấn đề: topic không có component
            // vẫn mở được, chỉ là không tự chọn phần tử.
            return null;
        }
    }

    /// <summary>Camera nhìn vào một điểm cho sẵn theo toạ độ nội bộ Revit (feet) — BCF dùng mét.</summary>
    public static BcfCamera CameraAt(XYZ pointInFeet) => BcfCamera.LookingAt(
        RevitCompat.FtToMm(pointInFeet.X) / 1000.0,
        RevitCompat.FtToMm(pointInFeet.Y) / 1000.0,
        RevitCompat.FtToMm(pointInFeet.Z) / 1000.0);

    /// <summary>
    /// Ghi file BCF và thêm một dòng vào <paramref name="result"/>. Lỗi ghi file <b>không</b> làm hỏng
    /// lệnh: báo cáo HTML/CSV chính đã ghi xong rồi, BCF là đầu ra thêm.
    /// </summary>
    public static void Write(string? path, List<BcfTopic> topics, int totalIssues, CommandResult result)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (topics.Count == 0)
        {
            result.Messages.Add("Không có vấn đề nào để xuất BCF — không ghi file.");
            return;
        }

        try
        {
            RevitCompat.EnsureParentDirectory(path);
            BcfWriter.Write(path!, topics);
            result.Messages.Add($"BCF {BcfWriter.Version}: {topics.Count}"
                + (totalIssues > topics.Count ? $"/{totalIssues} (giới hạn {MaxTopics} vấn đề một file)" : string.Empty)
                + $" vấn đề → \"{path}\".");
        }
        catch (Exception ex)
        {
            result.Messages.Add($"Không ghi được file BCF \"{path}\": {ex.Message}");
        }
    }
}
