using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;

namespace DhcbTools.Core.AutoCAD.Attributes;

/// <summary>
/// Đọc CSV đúng định dạng do <see cref="AttributeExportCommand"/> tạo ra
/// (BlockName,Handle,AttributeTag,AttributeValue) và ghi ngược giá trị vào attribute khớp Handle + Tag.
/// </summary>
public sealed class AttributeImportCommand : ICoreCommand<AttributeImportConfig>
{
    public string CommandName => "AttributeImport";

    public CommandResult Execute(Database database, AttributeImportConfig config)
    {
        if (!File.Exists(config.InputPath))
        {
            return CommandResult.Fail($"Không tìm thấy file: \"{config.InputPath}\".");
        }

        var lines = File.ReadAllLines(config.InputPath);
        if (lines.Length < 2)
        {
            return CommandResult.Fail("File CSV không có dữ liệu (chỉ có dòng tiêu đề hoặc rỗng).");
        }

        var updated = 0;
        var skipped = 0;
        var result = CommandResult.Ok(string.Empty);

        using var transaction = database.TransactionManager.StartTransaction();

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var cells = CsvText.SplitLine(lines[i]);
            if (cells.Count < 4)
            {
                skipped++;
                continue;
            }

            var handleText = cells[1];
            var tag = cells[2];
            var value = cells[3];

            if (!TryParseHandle(handleText, out var handle)
                || !database.TryGetObjectId(handle, out var objectId))
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: không tìm thấy Handle \"{handleText}\".");
                skipped++;
                continue;
            }

            if (transaction.GetObject(objectId, OpenMode.ForRead) is not BlockReference blockRef)
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: Handle \"{handleText}\" không phải Block Reference.");
                skipped++;
                continue;
            }

            var found = false;

            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                var attRef = (AttributeReference)transaction.GetObject(attId, OpenMode.ForRead);
                if (!string.Equals(attRef.Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                found = true;

                if (config.DryRun)
                {
                    result.Messages.Add($"[Xem trước] Handle {handleText} — {tag}: \"{attRef.TextString}\" → \"{value}\"");
                }
                else
                {
                    attRef.UpgradeOpen();
                    attRef.TextString = value;
                }

                updated++;
                break;
            }

            if (!found)
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: Block Handle \"{handleText}\" không có attribute tag \"{tag}\".");
                skipped++;
            }
        }

        if (config.DryRun)
        {
            transaction.Abort();
            result.Messages.Insert(0, $"[Xem trước] Sẽ cập nhật {updated} attribute, bỏ qua {skipped} dòng.");
            return CommandResult.Ok($"[Xem trước] Sẽ cập nhật {updated} attribute (chưa ghi vào drawing).", updated);
        }

        transaction.Commit();

        var final = CommandResult.Ok($"Đã cập nhật {updated} attribute từ \"{config.InputPath}\", bỏ qua {skipped} dòng.", updated);
        final.Messages.AddRange(result.Messages);
        return final;
    }

    private static bool TryParseHandle(string text, out Handle handle)
    {
        handle = default;
        text = text.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        try
        {
            var value = Convert.ToInt64(text, 16);
            handle = new Handle(value);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
