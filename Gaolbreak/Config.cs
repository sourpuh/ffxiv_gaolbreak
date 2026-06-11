using Dalamud.Bindings.ImGui;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Gaolbreak;

internal sealed class Config
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Data data;

    public Config(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        Data? loaded = null;
        try
        {
            loaded = pluginInterface.GetPluginConfig() as Data;
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "[GBUI] failed to load config; starting fresh");
        }
        data = loaded ?? new Data();
    }

    // Serialized payload. All access and saving goes through the outer class.
    internal sealed class Data : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        public bool EnableReorder { get; set; } = true;
        public bool EnableIndicator { get; set; } = true;

        // ImGui window ID -> the addon it's pinned to.
        public Dictionary<uint, string> WindowPins { get; set; } = new();
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

    private static readonly IReadOnlyDictionary<uint, string> DefaultPins = new Dictionary<uint, string>
    {
        [1043941337] = "FieldMarker",
        [3124431263] = "AreaMap",
    };
    // User pins that may override the default. If there is a default and they remove it, "" is stored.
    private Dictionary<uint, string> UserPins => data.WindowPins;

    public string? GetPinnedAddon(ImGuiWindowPtr w)
    {
        if (TryGetPinnedAddon(w, out var addon))
        {
            return addon;
        }
        return null;
    }

    public bool TryGetPinnedAddon(ImGuiWindowPtr w, out string addon)
    {
        addon = "";
        if (UserPins.TryGetValue(w.ID, out addon!))
        {
            return addon != "";
        }
        if (DefaultPins.TryGetValue(w.ID, out addon!))
        {
            return true;
        }
        return false;
    }

    public IEnumerable<uint> GetPinnedWindows(string addon)
    {
        foreach ((var w, var a) in UserPins)
            if (addon == a) yield return w;
        foreach ((var w, var a) in DefaultPins)
            if (addon == a && !UserPins.ContainsKey(w)) yield return w;
    }

    public void SetPinOverride(ImGuiWindowPtr w, string? addon)
    {
        bool hasDefault = DefaultPins.TryGetValue(w.ID, out var defaultAddon);
        bool changed;
        if (hasDefault ? addon == defaultAddon : addon is null)
        {
            changed = UserPins.Remove(w.ID);
        }
        else
        {
            var value = addon ?? "";
            changed = !UserPins.TryGetValue(w.ID, out var existing) || existing != value;
            UserPins[w.ID] = value;
        }
        if (changed) Save();
    }

    public bool HasPinOverride(ImGuiWindowPtr w)
    {
        return UserPins.ContainsKey(w.ID);
    }

    public void RemovePinOverride(ImGuiWindowPtr w)
    {
        if (UserPins.Remove(w.ID))
            Save();
    }
}
