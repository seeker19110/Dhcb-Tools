using Autodesk.Revit.DB;

namespace DhcbTools.Core;

/// <summary>
/// Hợp đồng chung cho mọi lệnh Core: nhận Document + config JSON (kiểu TConfig), tự mở transaction,
/// trả về CommandResult. Không có TaskDialog, không có Selection, không có WPF — để cùng một lệnh
/// chạy được từ Ribbon (vỏ Revit) lẫn từ hàng đợi batch (vỏ Batch) mà không cần viết lại.
/// </summary>
public interface ICoreCommand<in TConfig>
{
    /// <summary>Tên duy nhất của lệnh, dùng để log và để hàng đợi batch tra cứu.</summary>
    string CommandName { get; }

    CommandResult Execute(Document document, TConfig config);
}
