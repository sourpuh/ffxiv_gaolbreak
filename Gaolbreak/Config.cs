using Dalamud.Configuration;

namespace Gaolbreak;

internal sealed class Config : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ImGui window ID -> the addon it's pinned to.
    public Dictionary<uint, string> WindowPins { get; set; } = new();
}
