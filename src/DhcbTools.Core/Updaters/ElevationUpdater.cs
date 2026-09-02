using System.Diagnostics;
using Autodesk.Revit.DB;
using DhcbTools.Core.MEPF;
using DhcbTools.Shared.Logic;
using Newtonsoft.Json;

namespace DhcbTools.Core.Updaters;

/// <summary>Công tắc trong <c>%APPDATA%\DHCB\settings.json</c>: <c>{"updaters":{"elevation":false}}</c>. Mặc định TẮT (mục 4.1).</summary>
public sealed class UpdaterSettings
{
    [JsonProperty("updaters")]
    public Dictionary<string, bool> Updaters { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ngưỡng ms cho một lần Execute; vượt thì tự tắt cho tới lần khởi động sau.</summary>
    [JsonProperty("maxExecuteMs")]
    public int MaxExecuteMs { get; set; } = 200;

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DHCB", "settings.json");

    public bool IsEnabled(string name) => Updaters.TryGetValue(name, out var on) && on;

    public static UpdaterSettings Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        if (!File.Exists(file))
        {
            return new UpdaterSettings();
        }

        try
        {
            return JsonConvert.DeserializeObject<UpdaterSettings>(File.ReadAllText(file)) ?? new UpdaterSettings();
        }
        catch (JsonException)
        {
            return new UpdaterSettings();
        }
    }
}

/// <summary>
/// <see cref="IUpdater"/> điền cao độ đáy/đỉnh/tim khi phần tử MEP đổi hình học — cùng công thức
/// <see cref="MepLayout.Elevations"/> và cùng tham số với lệnh cấp 1 <see cref="ElevationTagCommand"/>.
/// Không bao giờ ném exception ra ngoài; đo thời gian, vượt ngưỡng thì tự tắt và báo một lần.
/// </summary>
public sealed class ElevationUpdater : IUpdater
{
    public const string Name = "elevation";

    private static readonly Guid UpdaterGuid = new Guid("6f1e8c2a-3b4d-4e5f-9a7b-1c2d3e4f5a6b");

    private readonly UpdaterId _id;
    private readonly ElevationTagConfig _config;
    private readonly int _maxMs;
    private bool _disabled;

    public ElevationUpdater(AddInId addInId, ElevationTagConfig? config = null, int maxExecuteMs = 200)
    {
        _id = new UpdaterId(addInId, UpdaterGuid);
        _config = config ?? new ElevationTagConfig { DryRun = false };
        _maxMs = maxExecuteMs;
    }

    /// <summary>Thông báo cho vỏ hiển thị (một lần) khi updater tự tắt.</summary>
    public Action<string>? OnDisabled { get; set; }

    public static ElementMulticategoryFilter TriggerFilter() => new ElementMulticategoryFilter(new List<BuiltInCategory>
    {
        BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Conduit,
    });

    /// <summary>Đăng ký updater + trigger GeometryChange. Trả false nếu đã đăng ký.</summary>
    public bool Register(bool optional = true)
    {
        if (UpdaterRegistry.IsUpdaterRegistered(_id))
        {
            return false;
        }

        UpdaterRegistry.RegisterUpdater(this, optional);
        UpdaterRegistry.AddTrigger(_id, TriggerFilter(), Element.GetChangeTypeGeometry());
        UpdaterRegistry.AddTrigger(_id, TriggerFilter(), Element.GetChangeTypeElementAddition());
        return true;
    }

    public void Unregister()
    {
        if (UpdaterRegistry.IsUpdaterRegistered(_id))
        {
            UpdaterRegistry.UnregisterUpdater(_id);
        }
    }

    public void Execute(UpdaterData data)
    {
        if (_disabled)
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var doc = data.GetDocument();
            foreach (var id in data.GetModifiedElementIds().Concat(data.GetAddedElementIds()))
            {
                var el = doc.GetElement(id);
                var bb = el?.get_BoundingBox(null);
                if (el == null || bb == null)
                {
                    continue;
                }

                var e = MepLayout.Elevations(bb.Min.Z, bb.Max.Z);
                SetIfPossible(el, "bottomElevation", _config.BottomElevParamName, e.BottomMm);
                SetIfPossible(el, "topElevation", _config.TopElevParamName, e.TopMm);
                SetIfPossible(el, "centreElevation", _config.CenterElevParamName, e.CentreMm);
            }
        }
        catch
        {
            // Tuyệt đối không ném ra ngoài — làm hỏng transaction của người dùng.
        }
        finally
        {
            sw.Stop();
            if (sw.ElapsedMilliseconds > _maxMs)
            {
                _disabled = true;
                OnDisabled?.Invoke($"DHCB ElevationUpdater tự tắt: một lần cập nhật mất {sw.ElapsedMilliseconds} ms (> {_maxMs} ms). Bật lại trong settings.json sau khi kiểm tra hiệu năng.");
            }
        }
    }

    /// <summary>Ghi qua từ điển tên tham số (giai đoạn 9.2); paramName là tên người dùng chỉ định, có thể null.</summary>
    private static void SetIfPossible(Element el, string key, string? paramName, double mm)
    {
        var p = RevitCompat.Lookup(el, key, paramName);
        if (p == null || p.IsReadOnly)
        {
            return;
        }

        switch (p.StorageType)
        {
            case StorageType.Double:
                p.Set(MepLayout.MmToFeet(mm));
                break;
            case StorageType.String:
                p.Set(NumericText.Format(mm, 1));
                break;
        }
    }

    public string GetAdditionalInformation() => "Điền cao độ đáy/đỉnh/tim MEP theo thời gian thực (DHCB Tools).";

    public ChangePriority GetChangePriority() => ChangePriority.MEPSystems;

    public UpdaterId GetUpdaterId() => _id;

    public string GetUpdaterName() => "DHCB Elevation Updater";
}
