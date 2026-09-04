using System.Globalization;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Core.AutoCAD.AutoNumbering;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.Attributes;

/// <summary>
/// Gán attribute tăng dần theo mẫu (kiểu Lee Mac BATTE) cho các Block Reference cùng tên trong Model Space,
/// theo thứ tự vị trí: từ trên xuống dưới, trái sang phải — quy ước bản vẽ kỹ thuật, có gom hàng theo
/// <c>rowToleranceMm</c>. Phần thu thập/sắp xếp/ghi dùng chung với AutoNumbering qua <see cref="BlockNumbering"/>;
/// ở đây chỉ còn cách sinh nhãn từ mẫu "{n}" / "{n:000}".
/// </summary>
public sealed class AttributeIncrementCommand : ICoreCommand<AttributeIncrementConfig>
{
    private static readonly Regex PatternToken = new(@"\{n(:(\d+))?\}", RegexOptions.Compiled);

    public string CommandName => "AttributeIncrement";

    public CommandResult Execute(Database database, AttributeIncrementConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.AttributeTag))
        {
            return CommandResult.Fail("Thiếu tag attribute cần ghi (attributeTag).");
        }

        if (string.IsNullOrEmpty(config.Pattern) || !PatternToken.IsMatch(config.Pattern))
        {
            return CommandResult.Fail($"Mẫu \"{config.Pattern}\" không chứa \"{{n}}\" hoặc \"{{n:000}}\" — mọi block sẽ nhận cùng một giá trị.");
        }

        return BlockNumbering.Execute(database, new BlockNumberingRequest
        {
            BlockName = config.BlockName,
            AttributeTag = config.AttributeTag,
            Direction = ScanDirection.LeftToRightThenTopToBottom,
            RowTolerance = config.RowToleranceMm,
            StartNumber = config.StartNumber,
            Step = 1,
            Label = n => FormatPattern(config.Pattern, n),
            DryRun = config.DryRun,
            PreviewSummary = count => $"[Xem trước] Sẽ gán {count} giá trị vào attribute \"{config.AttributeTag}\" của block \"{config.BlockName}\".",
            DoneSummary = (updated, count) => $"Đã gán {updated}/{count} giá trị attribute \"{config.AttributeTag}\" cho block \"{config.BlockName}\".",
        });
    }

    /// <summary>Thay "{n}" hoặc "{n:000}" trong mẫu bằng số, độ rộng theo số ký tự trong ":000".</summary>
    internal static string FormatPattern(string pattern, int number)
    {
        return PatternToken.Replace(pattern, match =>
        {
            var digitsGroup = match.Groups[2].Value;
            return digitsGroup.Length > 0
                ? number.ToString("D" + digitsGroup.Length, CultureInfo.InvariantCulture)
                : number.ToString(CultureInfo.InvariantCulture);
        });
    }
}
