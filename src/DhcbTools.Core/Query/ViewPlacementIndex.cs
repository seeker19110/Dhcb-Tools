namespace DhcbTools.Core.Query;

/// <summary>Legend có thể đặt trên nhiều sheet; không được coi ViewId là khoá duy nhất của viewport.</summary>
public static class ViewPlacementIndex
{
    public static Dictionary<long, List<long>> Build(IEnumerable<KeyValuePair<long, long>> placements)
    {
        var index = new Dictionary<long, List<long>>();
        foreach (var placement in placements)
        {
            if (!index.TryGetValue(placement.Key, out var sheets))
                index.Add(placement.Key, sheets = new List<long>());
            if (!sheets.Contains(placement.Value)) sheets.Add(placement.Value);
        }
        return index;
    }
}
