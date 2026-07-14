using Dalamud.Bindings.ImGui;
using Dalamud.Configuration;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Gaolbreak;

internal sealed class Config
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly DynamicConfig remote;
    private readonly Data data;

    public Config(IDalamudPluginInterface pluginInterface, DynamicConfig remote)
    {
        this.pluginInterface = pluginInterface;
        this.remote = remote;
        Data? loaded = null;
        try
        {
            loaded = pluginInterface.GetPluginConfig() as Data;
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Failed to load config");
        }
        data = loaded ?? new Data();
    }

    // Serialized payload. All access and saving goes through the outer class.
    internal sealed class Data : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        public bool EnableReorder { get; set; } = true;
        public bool EnableIndicator { get; set; } = true;
        public bool EnableToneAdjust { get; set; } = true;
    }

    private void Save()
    {
        pluginInterface.SavePluginConfig(data);
    }

    public bool Enable { get; set; } = true;

    public bool EnableReorder
    {
        get => data.EnableReorder;
        set
        {
            if (data.EnableReorder == value) return;
            data.EnableReorder = value;
            Save();
        }
    }

    public bool EnableIndicator
    {
        get => data.EnableIndicator;
        set
        {
            if (data.EnableIndicator == value) return;
            data.EnableIndicator = value;
            Save();
        }
    }

    public bool EnableToneAdjust
    {
        get => data.EnableToneAdjust;
        set
        {
            if (data.EnableToneAdjust == value) return;
            data.EnableToneAdjust = value;
            Save();
        }
    }

    public bool IsAlwaysLifted(ImGuiWindowPtr w)
    {
        var rc = remote.Current;
        if (rc.ForegroundWindowIds.Contains(w.ID)) return true;
        var name = w.GetName();
        foreach (var prefix in rc.ForegroundWindowPrefixes)
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
        return false;
    }

    public bool IsAddonLiftable(string addonName)
    {
        var set = HudMoveable();
        if (set.Count == 0) return false;
        if (set.Contains(addonName)) return true;
        if (addonName.StartsWith("ChatLog", StringComparison.Ordinal)) return true;
        int end = addonName.Length;
        while (end > 0 && char.IsAsciiDigit(addonName[end - 1])) end--;
        return end != addonName.Length && set.Contains(addonName[..end]);
    }

    private HashSet<string>? hudMoveable;
    private HashSet<string> HudMoveable()
    {
        if (hudMoveable != null) return hudMoveable;
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var entry in HudLayoutAddon.GetSpan())
            {
                var name = entry.AddonName.ToString();
                if (!string.IsNullOrEmpty(name)) set.Add(name);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Dynamic config: failed to read the HudLayoutAddon registry");
        }
        hudMoveable = set;
        return set;
    }

    private IReadOnlyDictionary<string, IReadOnlySet<uint>> DefaultPins => remote.Current.DefaultPins;

    public IEnumerable<uint> GetPinnedWindows(string addon)
    {
        if (DefaultPins.TryGetValue(addon, out var pins))
        {
            foreach (var w in pins)
            {
                yield return w;
            }
        }
    }
}
