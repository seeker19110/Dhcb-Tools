using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.AutoNumbering;

/// <summary>
/// Đánh số hàng loạt Block Reference theo vị trí hình học — tương đương AutoNumberingCommand của Revit.
/// Sắp Block Reference theo InsertionPoint (gom hàng theo <c>rowToleranceMm</c> bằng <see cref="NumberingPlanner"/>,
/// cùng thuật toán với Revit nên hai nền tảng đánh số giống hệt nhau) rồi ghi giá trị vào AttributeReference khớp tag.
/// Phần chung với AttributeIncrement nằm ở <see cref="BlockNumbering"/>.
/// </summary>
public sealed class AutoNumberingCommand : ICoreCommand<AutoNumberingConfig>
{
    public string CommandName => "AutoNumbering";

    public CommandResult Execute(Database database, AutoNumberingConfig config)
    {
        if (config.PadWidth < 0)
        {
            return CommandResult.Fail($"Số chữ số đệm (padWidth) không được âm: {config.PadWidth}.");
        }

        var direction = config.Direction == NumberingDirection.LeftToRightThenTopToBottom
            ? ScanDirection.LeftToRightThenTopToBottom
            : ScanDirection.TopToBottomThenLeftToRight;

        return BlockNumbering.Execute(database, new BlockNumberingRequest
        {
            BlockName = config.BlockName,
            AttributeTag = config.AttributeTag,
            Direction = direction,
            RowTolerance = config.RowToleranceMm,
            StartNumber = config.StartNumber,
            Step = config.Step,
            Label = n => NumberingPlanner.FormatLabel(config.Prefix, n, config.PadWidth),
            DryRun = config.DryRun,
            PreviewSummary = count => $"[Xem trước] Sẽ đánh số {count} Block \"{config.BlockName}\" vào attribute \"{config.AttributeTag}\".",
            DoneSummary = (updated, count) => $"Đã đánh số {updated}/{count} Block \"{config.BlockName}\".",
        });
    }
}
