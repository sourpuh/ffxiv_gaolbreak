using Dalamud.Bindings.ImGui;
using Dalamud.Configuration;
using Dalamud.Plugin;

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
    }

    private void Save()
    {
        pluginInterface.SavePluginConfig(data);
    }

    public event Action<bool>? OnEnableChanged;

    public bool Enable
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnEnableChanged?.Invoke(value);
        }
    } = true;

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
        var rc = remote.Current;
        if (rc.LiftableAddons.Contains(addonName)) return true;
        foreach (var prefix in rc.LiftableAddonPrefixes)
            if (addonName.StartsWith(prefix, StringComparison.Ordinal)) return true;
        return false;
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
