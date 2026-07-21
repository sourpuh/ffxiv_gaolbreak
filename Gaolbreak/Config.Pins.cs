using Dalamud.Bindings.ImGui;

namespace Gaolbreak;

internal sealed partial class Config
{
    public const string AboveAllAnchor = "*";
    public const string BelowAllAnchor = RestBeaconAddon.InternalName;
    private readonly Dictionary<string, HashSet<uint>> defaultPins = new();
    private readonly HashSet<uint> patternResolvedIds = new();

    private void DynamicConfigUpdated()
    {
        patternResolvedIds.Clear();
        this.defaultPins.Clear();
        var defaultPins = dynamicCfg.Current.DefaultPins;
        foreach ((var addon, var ids) in defaultPins)
        {
            this.defaultPins[addon] = new(ids);
        }
    }

    public IEnumerable<uint> GetPinnedWindows(string addon)
    {
        if (data.Pins.TryGetValue(addon, out var userIds))
            foreach (var id in userIds) yield return id;
        if (defaultPins.TryGetValue(addon, out var defaultIds))
            foreach (var id in defaultIds) yield return id;
    }

    public bool IsUserPinned(uint id)
    {
        foreach (var ids in data.Pins.Values)
            if (ids.Contains(id)) return true;
        return false;
    }

    public bool TryGetPinAnchor(uint id, out string anchor)
    {
        foreach (var (a, ids) in data.Pins)
            if (ids.Contains(id)) { anchor = a; return true; }
        foreach (var (a, ids) in defaultPins)
            if (ids.Contains(id)) { anchor = a; return true; }
        anchor = "";
        return false;
    }

    public void ResolvePinPatterns(ImGuiWindowPtr w)
    {
        if (!patternResolvedIds.Add(w.ID)) return;

        var defaultPinMatchers = dynamicCfg.Current.DefaultPinMatchers;
        var name = w.GetName();
        foreach (var (anchor, matchers) in defaultPinMatchers)
        {
            foreach (var matcher in matchers)
            {
                if (!matcher.IsMatch(name)) continue;
                if (!defaultPins.TryGetValue(anchor, out var ids))
                    defaultPins[anchor] = ids = new();
                ids.Add(w.ID);
                break;
            }
        }
    }

    public bool IsPinnedToAddon(uint id) =>
        TryGetPinAnchor(id, out var anchor) && !(anchor is AboveAllAnchor or BelowAllAnchor);

    public void SetPin(uint id, string anchor)
    {
        RemovePin(id);
        if (!data.Pins.TryGetValue(anchor, out var ids))
            data.Pins[anchor] = ids = new();
        ids.Add(id);
        Save();
    }

    public void ClearPin(uint id)
    {
        if (RemovePin(id))
            Save();
    }

    private bool RemovePin(uint id)
    {
        bool removed = false;
        foreach ((var anchor, var ids) in data.Pins)
        {
            removed |= ids.Remove(id);
        }
        return removed;
    }
}
