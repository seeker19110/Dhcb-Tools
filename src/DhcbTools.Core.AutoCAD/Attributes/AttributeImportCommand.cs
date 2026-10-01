using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.Attributes;

/// <summary>
/// Đọc CSV đúng định dạng do <see cref="AttributeExportCommand"/> tạo ra
/// (BlockName,Handle,AttributeTag,AttributeValue) và ghi ngược giá trị vào attribute khớp Handle + Tag.
/// <para>
/// <b>Chỉ ghi ô đã đổi.</b> Bản trước đếm và ghi mọi dòng trong CSV, nên nhập lại chính file vừa xuất
/// vẫn báo "cập nhật 50 attribute" — không phân biệt được với việc kỹ sư sửa thật 50 ô. Cùng lỗi đã sửa
/// cho <c>ParameterImport</c> bên Revit (PR #29) và <c>LayerImport</c>; lộ ra ở vòng kiểm thử AutoCAD
/// đầu tiên 2026-09-03 nhờ ca "nhập lại chính CSV vừa xuất".
/// </para>
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

        // Đọc theo bản ghi RFC 4180 (ReadRecords): giá trị attribute nhiều dòng do chính AttributeExport
        // ghi ra (đã escape) trước đây bị ReadAllLines cắt đôi thành hai dòng hỏng.
        var lines = CsvText.ReadRecords(config.InputPath).ToList();
        if (lines.Count < 2)
        {
            return CommandResult.Fail("File CSV không có dữ liệu (chỉ có dòng tiêu đề hoặc rỗng).");
        }

        var updated = 0;
        var skipped = 0;
        var unchanged = 0;
        var result = CommandResult.Ok(string.Empty);

        using var transaction = database.TransactionManager.StartTransaction();
        var lockedLayers = AcadHelpers.LockedLayerIds(database, transaction);
        var lockedSkips = new LockedLayerSkips();

        for (var i = 1; i < lines.Count; i++)
        {
            var cells = lines[i];
            if (cells.Length == 1 && string.IsNullOrWhiteSpace(cells[0]))
            {
                continue;
            }

            if (cells.Length < 4)
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

            // Block đã xoá sau lúc xuất CSV: handle VẪN tra ra ObjectId (đối tượng xoá còn trong database tới khi lưu và
            // mở lại), và GetObject ném eWasErased — bản cũ vì thế sập cả lệnh thay vì bỏ qua một dòng.
            if (objectId.IsErased)
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: Block Handle \"{handleText}\" đã bị xoá khỏi bản vẽ.");
                skipped++;
                continue;
            }

            if (transaction.GetObject(objectId, OpenMode.ForRead) is not BlockReference blockRef)
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: Handle \"{handleText}\" không phải Block Reference.");
                skipped++;
                continue;
            }

            var matches = new List<AttributeReference>();
            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                var candidate = (AttributeReference)transaction.GetObject(attId, OpenMode.ForRead);
                if (string.Equals(candidate.Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(candidate);
                }
            }

            if (matches.Count == 0)
            {
                result.Messages.Add($"Bỏ qua dòng {i + 1}: Block Handle \"{handleText}\" không có attribute tag \"{tag}\".");
                skipped++;
                continue;
            }

            if (matches.Count > 1)
            {
                // AutoCAD cho phép hai attribute cùng tag trong một block, và AttributeExport xuất cả hai dòng cùng
                // (Handle, Tag). Bản cũ ghi mọi dòng vào attribute ĐẦU TIÊN: nhập lại nguyên file vừa xuất là đủ để
                // attribute đầu nhận giá trị của attribute sau, im lặng. Không đoán dòng nào ứng với attribute nào.
                result.Messages.Add(
                    $"Bỏ qua dòng {i + 1}: Block Handle \"{handleText}\" có {matches.Count} attribute cùng tag \"{tag}\" — "
                    + "không biết dòng này ứng với attribute nào; sửa trực tiếp trong AutoCAD.");
                skipped++;
                continue;
            }

            var attRef = matches[0];

            // Giá trị trùng thì không đụng vào: mở ForWrite một attribute là làm bẩn drawing và
            // đẩy một mục vào undo, dù không đổi gì.
            if (string.Equals(attRef.TextString, value, StringComparison.Ordinal))
            {
                unchanged++;
                continue;
            }

            if (lockedLayers.Contains(attRef.LayerId))
            {
                // UpgradeOpen trên layer khoá ném eOnLockedLayer — sập cả lệnh. Xem trước cũng bỏ qua để hai con số khớp.
                lockedSkips.Add(attRef.Layer);
                skipped++;
                continue;
            }

            if (config.DryRun)
            {
                result.Messages.Add($"[Xem trước] Handle {handleText} — {tag}: \"{attRef.TextString}\" → \"{value}\"");
            }
            else
            {
                attRef.UpgradeOpen();
                attRef.TextString = value;
                // Attribute canh giữa/Fit/Aligned giữ hình học canh cũ sau khi đổi chữ — tag lệch khỏi bong bóng.
                attRef.AdjustAlignment(database);
            }

            updated++;
        }

        if (lockedSkips.Message("attribute") is { } lockedNote)
        {
            result.Messages.Insert(0, lockedNote);
        }

        if (unchanged > 0)
        {
            result.Messages.Insert(0, $"{unchanged} ô giữ nguyên vì giá trị trong CSV trùng với drawing.");
        }

        if (config.DryRun)
        {
            transaction.Abort();
            // Giữ nguyên Messages: bản trước tạo CommandResult mới và đánh rơi toàn bộ dòng đã gom ở trên.
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ cập nhật {updated} attribute, bỏ qua {skipped} dòng (chưa ghi vào drawing).",
                updated);
            preview.Messages.AddRange(result.Messages);
            return preview;
        }

        transaction.Commit();

        var final = CommandResult.Ok($"Đã cập nhật {updated} attribute từ \"{config.InputPath}\", bỏ qua {skipped} dòng.", updated);
        final.Messages.AddRange(result.Messages);
        return final;
    }

    /// <summary>Handle đọc bằng <see cref="HandleText"/> (nhận cả "0x1A3", "(1A3)"); trước đây Convert.ToInt64 từ chối các dạng đó và nuốt lỗi.</summary>
    private static bool TryParseHandle(string text, out Handle handle)
    {
        var ok = HandleText.TryParse(text, out var value);
        handle = ok ? new Handle(value) : default;
        return ok;
    }
}
