using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using DhcbTools.Shared.Logic;
using DhcbTools.Shared.Logic.Cad;

namespace DhcbTools.Core.AutoCAD.LayerTools;

/// <summary>
/// Đổi layer của mọi entity (và attribute của block reference) từ Source sang Target theo bảng map CSV — tương đương LAYTRANS.
/// Tạo Target nếu chưa tồn tại (copy Color/Linetype/Lineweight/Plottable từ CSV nếu có; linetype chưa có
/// trong drawing thì nạp từ acad.lin, nạp không được thì BÁO). Sau khi chuyển, tuỳ chọn xoá layer Source
/// nếu không còn entity nào tham chiếu.
/// <para>
/// Không đụng entity trong block của xref (sửa là sửa file người khác) hay block anonymous (*U…, *D… do
/// AutoCAD tự quản). Dòng CSV có tên layer không hợp lệ được báo và bỏ qua thay vì ném exception giữa transaction.
/// </para>
/// </summary>
public sealed class LayerTranslateCommand : ICoreCommand<LayerTranslateConfig>
{
    private sealed record MapRow(int Line, string Source, string Target, string? Color, string? Linetype, string? Lineweight, string? Plottable);

    public string CommandName => "LayerTranslate";

    public CommandResult Execute(Database database, LayerTranslateConfig config)
    {
        if (!File.Exists(config.MapCsvPath))
        {
            return CommandResult.Fail($"Không tìm thấy file map: \"{config.MapCsvPath}\".");
        }

        var lines = CsvText.ReadRecords(config.MapCsvPath).ToList();
        if (lines.Count < 2)
        {
            return CommandResult.Fail("File map CSV không có dữ liệu.");
        }

        var report = new List<string>();
        var rows = new List<MapRow>();
        for (var i = 1; i < lines.Count; i++)
        {
            var cells = lines[i];
            if (cells.Length == 1 && string.IsNullOrWhiteSpace(cells[0]))
            {
                continue;
            }

            if (cells.Length < 2 || string.IsNullOrWhiteSpace(cells[0]) || string.IsNullOrWhiteSpace(cells[1]))
            {
                report.Add($"Bỏ qua dòng {i + 1}: cần đủ hai cột Source,Target.");
                continue;
            }

            var source = cells[0].Trim();
            var target = cells[1].Trim();
            if (!AcadHelpers.IsValidSymbolName(target))
            {
                report.Add($"Bỏ qua dòng {i + 1}: tên layer đích \"{target}\" không hợp lệ với AutoCAD.");
                continue;
            }

            rows.Add(new MapRow(
                i + 1,
                source,
                target,
                Cell(cells, 2),
                Cell(cells, 3),
                Cell(cells, 4),
                Cell(cells, 5)));
        }

        if (rows.Count == 0)
        {
            var fail = CommandResult.Fail("File map CSV không có dòng hợp lệ (cần cột Source,Target).");
            fail.Messages.AddRange(report);
            return fail;
        }

        var changedCount = 0;

        using var transaction = database.TransactionManager.StartTransaction();

        var layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);

        // Đảm bảo mọi Target đã tồn tại (tạo mới nếu cần) trước khi đổi entity.
        var createdTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (layerTable.Has(row.Target) || !createdTargets.Add(row.Target))
            {
                continue;
            }

            if (config.DryRun)
            {
                report.Add($"[Xem trước] Sẽ tạo layer mới: \"{row.Target}\".");
                DescribePlannedProperties(database, transaction, row, report);
                continue;
            }

            var ltWrite = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForWrite);
            var newLayer = new LayerTableRecord { Name = row.Target };
            ApplyProperties(database, transaction, newLayer, row, report);

            ltWrite.Add(newLayer);
            transaction.AddNewlyCreatedDBObject(newLayer, true);
            report.Add($"Đã tạo layer mới: \"{row.Target}\".");
        }

        // Đổi Layer của mọi entity Source → Target, trong mọi Block Table Record sửa được.
        var sourceToTarget = rows
            .GroupBy(r => r.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Target, StringComparer.OrdinalIgnoreCase);

        var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var lockedLayers = AcadHelpers.LockedLayerIds(database, transaction);
        var lockedSkips = new LockedLayerSkips();
        var skippedBlocks = 0;

        // Đổi layer của một entity hoặc attribute nếu layer của nó có trong bảng map. Layer khoá: bỏ qua + đếm
        // (UpgradeOpen trên layer khoá ném eOnLockedLayer, cả lệnh sập) — CollectUsedLayerNamesAfterMap xét đúng tập này.
        void Translate(Entity ent)
        {
            if (!sourceToTarget.TryGetValue(ent.Layer, out var target))
            {
                return;
            }

            if (lockedLayers.Contains(ent.LayerId))
            {
                lockedSkips.Add(ent.Layer);
                return;
            }

            if (!config.DryRun)
            {
                ent.UpgradeOpen();
                ent.Layer = target;
            }

            changedCount++;
        }

        foreach (ObjectId blockId in blockTable)
        {
            var block = (BlockTableRecord)transaction.GetObject(blockId, OpenMode.ForRead);
            if (AcadHelpers.IsProtectedBlock(block))
            {
                skippedBlocks++;
                continue;
            }

            foreach (ObjectId entityId in block)
            {
                if (transaction.GetObject(entityId, OpenMode.ForRead) is not Entity ent)
                {
                    continue;
                }

                Translate(ent);

                // AttributeReference thuộc BlockReference, KHÔNG nằm trong BlockTableRecord. Bản cũ bỏ sót: chữ khung tên
                // ở lại layer cũ, và xem trước (vốn tính cả attribute) báo "xoá N layer nguồn" mà chạy thật xoá ít hơn.
                if (ent is BlockReference blockRef)
                {
                    foreach (ObjectId attId in blockRef.AttributeCollection)
                    {
                        if (transaction.GetObject(attId, OpenMode.ForRead) is AttributeReference attRef)
                        {
                            Translate(attRef);
                        }
                    }
                }
            }
        }

        if (skippedBlocks > 0)
        {
            report.Add($"Không đụng {skippedBlocks} block của xref/anonymous.");
        }

        if (lockedSkips.Message("entity/attribute") is { } lockedNote)
        {
            report.Add(lockedNote);
        }

        var deletedLayers = new List<string>();

        if (config.DeleteEmptySource)
        {
            // Xem trước chưa đổi layer của entity nên tập "đang dùng" thật vẫn chứa mọi layer nguồn → báo "xoá 0";
            // chạy thật rồi xoá 40 layer. Tính tập SAU khi đổi (áp bảng map lên entity ngoài block bảo vệ).
            var stillUsed = config.DryRun
                ? AcadHelpers.CollectUsedLayerNamesAfterMap(database, transaction, sourceToTarget, lockedLayers)
                : AcadHelpers.CollectUsedLayerNames(database, transaction);

            foreach (var source in sourceToTarget.Keys)
            {
                if (string.Equals(source, "0", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!layerTable.Has(source) || stillUsed.Contains(source))
                {
                    continue;
                }

                // Xét layer hiện hành TRƯỚC nhánh xem trước: bản cũ chỉ xét lúc chạy thật, nên xem trước báo "sẽ xoá" cả layer
                // hiện hành rồi chạy thật giữ lại — con số "xoá N layer" của hai lượt lệch nhau.
                var layerId = layerTable[source];
                if (layerId == database.Clayer)
                {
                    report.Add($"Không xoá layer nguồn \"{source}\" vì đang là layer hiện hành.");
                    continue;
                }

                if (config.DryRun)
                {
                    report.Add($"[Xem trước] Sẽ xoá layer nguồn rỗng: \"{source}\".");
                    deletedLayers.Add(source);
                    continue;
                }

                var layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForWrite);
                layer.Erase();
                deletedLayers.Add(source);
            }
        }

        if (config.DryRun)
        {
            transaction.Abort();
            var preview = CommandResult.Ok(
                $"[Xem trước] Sẽ đổi layer của {changedCount} đối tượng (entity + attribute), xoá {deletedLayers.Count} layer nguồn rỗng.",
                changedCount);
            preview.Messages.AddRange(report);
            return preview;
        }

        transaction.Commit();

        var result = CommandResult.Ok(
            $"Đã đổi layer của {changedCount} đối tượng (entity + attribute) theo \"{config.MapCsvPath}\", xoá {deletedLayers.Count} layer nguồn rỗng.",
            changedCount);
        result.Messages.AddRange(report);
        return result;
    }

    private static string? Cell(string[] cells, int index)
        => cells.Length > index && cells[index].Trim().Length > 0 ? cells[index].Trim() : null;

    /// <summary>Gán Color/Linetype/Lineweight/Plottable từ CSV cho layer mới tạo; giá trị không đọc được thì báo.</summary>
    private static void ApplyProperties(Database database, Transaction transaction, LayerTableRecord layer, MapRow row, List<string> report)
    {
        if (row.Color is not null)
        {
            if (TryParseAci(row.Color, out var aci))
            {
                layer.Color = Color.FromColorIndex(ColorMethod.ByAci, aci);
            }
            else
            {
                report.Add($"Dòng {row.Line}: màu \"{row.Color}\" không phải chỉ số ACI 1–255, layer \"{row.Target}\" giữ màu mặc định.");
            }
        }

        if (row.Linetype is not null)
        {
            var linetypeId = ResolveLinetype(database, transaction, row.Linetype, row, report);
            if (!linetypeId.IsNull)
            {
                layer.LinetypeObjectId = linetypeId;
            }
        }

        if (row.Lineweight is not null)
        {
            if (TryParseLineWeight(row.Lineweight, out var lineWeight))
            {
                layer.LineWeight = lineWeight;
            }
            else
            {
                report.Add($"Dòng {row.Line}: lineweight \"{row.Lineweight}\" không có trong bảng chuẩn AutoCAD (đơn vị 1/100 mm, ví dụ 25 = 0.25 mm), layer \"{row.Target}\" giữ mặc định.");
            }
        }

        if (row.Plottable is not null)
        {
            if (TryParsePlottable(row.Plottable, out var plottable))
            {
                layer.IsPlottable = plottable;
            }
            else
            {
                report.Add($"Dòng {row.Line}: Plottable \"{row.Plottable}\" phải là true/false.");
            }
        }
    }

    /// <summary>
    /// Xem trước: chỉ kiểm giá trị (kể cả linetype có nạp được không) mà không tạo gì. Báo đủ những ô mà
    /// <see cref="ApplyProperties"/> sẽ bỏ qua — bản cũ quên màu và Plottable, nên lỗi đó chỉ lộ ra lúc chạy thật.
    /// </summary>
    private static void DescribePlannedProperties(Database database, Transaction transaction, MapRow row, List<string> report)
    {
        if (row.Color is not null && !TryParseAci(row.Color, out _))
        {
            report.Add($"Dòng {row.Line}: màu \"{row.Color}\" không phải chỉ số ACI 1–255, layer \"{row.Target}\" sẽ giữ màu mặc định.");
        }

        if (row.Linetype is not null)
        {
            var linetypeTable = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead);
            if (!linetypeTable.Has(row.Linetype))
            {
                report.Add($"[Xem trước] Linetype \"{row.Linetype}\" chưa có trong drawing — sẽ thử nạp từ acad.lin khi chạy thật.");
            }
        }

        if (row.Lineweight is not null && !TryParseLineWeight(row.Lineweight, out _))
        {
            report.Add($"Dòng {row.Line}: lineweight \"{row.Lineweight}\" không có trong bảng chuẩn AutoCAD (đơn vị 1/100 mm).");
        }

        if (row.Plottable is not null && !TryParsePlottable(row.Plottable, out _))
        {
            report.Add($"Dòng {row.Line}: Plottable \"{row.Plottable}\" phải là true/false.");
        }
    }

    /// <summary>Màu trong CSV: chỉ số ACI 1–255.</summary>
    private static bool TryParseAci(string text, out short aci)
        => short.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out aci)
           && aci >= 1 && aci <= 255;

    /// <summary>Plottable trong CSV: true/false hoặc 1/0.</summary>
    private static bool TryParsePlottable(string text, out bool plottable)
        => bool.TryParse(text, out plottable) || TryParseBit(text, out plottable);

    /// <summary>ObjectId của linetype theo tên; chưa có thì nạp từ acad.lin; nạp không được → báo, trả Null.</summary>
    private static ObjectId ResolveLinetype(Database database, Transaction transaction, string name, MapRow row, List<string> report)
    {
        var linetypeTable = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead);
        if (linetypeTable.Has(name))
        {
            return linetypeTable[name];
        }

        try
        {
            database.LoadLineTypeFile(name, "acad.lin");
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            report.Add($"Dòng {row.Line}: linetype \"{name}\" không có trong drawing và không nạp được từ acad.lin ({ex.ErrorStatus}) — layer \"{row.Target}\" giữ Continuous.");
            return ObjectId.Null;
        }

        // LoadLineTypeFile ghi thẳng vào database, ngoài transaction hiện tại: mở lại bảng để thấy bản ghi mới.
        linetypeTable = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead);
        if (linetypeTable.Has(name))
        {
            report.Add($"Đã nạp linetype \"{name}\" từ acad.lin.");
            return linetypeTable[name];
        }

        report.Add($"Dòng {row.Line}: acad.lin không có linetype \"{name}\" — layer \"{row.Target}\" giữ Continuous.");
        return ObjectId.Null;
    }

    /// <summary>Lineweight trong CSV: số nguyên 1/100 mm, mm thập phân, tên enum hoặc ByLayer/ByBlock/Default — bảng đọc ở <see cref="LineWeightText"/> (thuần, có test).</summary>
    internal static bool TryParseLineWeight(string text, out LineWeight lineWeight)
    {
        var ok = LineWeightText.TryParse(text, out var value);
        lineWeight = (LineWeight)value;
        return ok && Enum.IsDefined(typeof(LineWeight), lineWeight);
    }

    private static bool TryParseBit(string text, out bool value)
    {
        value = text.Trim() == "1";
        return value || text.Trim() == "0";
    }
}
