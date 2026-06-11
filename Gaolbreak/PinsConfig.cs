using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;

namespace Gaolbreak;

internal class PinsConfig(IDalamudPluginInterface pluginInterface, Config config)
{
    private static readonly IReadOnlyDictionary<uint, string> DefaultPins = new Dictionary<uint, string>
    {
        [1043941337] = "FieldMarker",
        [3124431263] = "AreaMap",
    };

    // User pins that may override the default. If there is a default and they remove it, "" is stored.
    private Dictionary<uint, string> UserPins => config.WindowPins;

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

    public void SetOverride(ImGuiWindowPtr w, string? addon)
    {
        bool changed;
        if (addon == DefaultPins[w.ID])
            changed = UserPins.Remove(w.ID);
        else
            changed = UserPins.TryAdd(w.ID, addon ?? "");
        if (changed) Save();
    }

    public bool HasOverride(ImGuiWindowPtr w)
    {
        return UserPins.ContainsKey(w.ID);
    }

    public void RemoveOverride(ImGuiWindowPtr w)
    {
        if (UserPins.Remove(w.ID))
            Save();
    }

    internal void Save()
    {
        pluginInterface.SavePluginConfig(config);
    }
}
