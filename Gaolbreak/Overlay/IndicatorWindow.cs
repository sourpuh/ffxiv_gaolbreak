using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace Gaolbreak.Overlay;

internal class IndicatorWindow : OverlayWindow
{
    private readonly Config config;
    private readonly Capturer capture;
    private readonly Action onRightClick;

    public IndicatorWindow(string name, Config config, Capturer capture, Action onRightClick)
        : base(name)
    {
        this.config = config;
        this.capture = capture;
        this.onRightClick = onRightClick;
    }

    protected override ImGuiWindowFlags ExtraWindowFlags() => ImGuiWindowFlags.AlwaysAutoResize
        | (config.EnableIndicator ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoInputs);

    protected override void DrawContent(ImDrawListPtr dl)
    {
        if (!config.EnableIndicator) return;

        string? inactiveReason = capture.InactiveReason();
        bool broken = capture.HooksBroken;
        uint dotColor = broken ? 0xFF00A5FFu
                      : !config.Enable ? 0xFF2222DDu
                      : inactiveReason == null ? 0xFF22DD22u
                      : 0xFF00FFFFu;
        const float dotRadius = 4f;
        var pad = new Vector2(6f, 6f);
        var boxSize = new Vector2(dotRadius) + pad * 2;

        var p = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##gaolbreak_killswitch", boxSize);
        bool leftClicked = ImGui.IsItemClicked();
        bool rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(broken ? inactiveReason
                           : !config.Enable ? "Off"
                           : inactiveReason == null ? "On" : inactiveReason);
        }

        dl.AddCircleFilled(p + pad, dotRadius + 2, 0xB0000000u);
        dl.AddCircleFilled(p + pad, dotRadius, dotColor);

        if (leftClicked)
        {
            config.Enable = !config.Enable;
        }
        if (rightClicked)
        {
            onRightClick();
        }
    }
}
