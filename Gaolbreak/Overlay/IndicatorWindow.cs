using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace Gaolbreak.Overlay;

internal class IndicatorWindow : OverlayWindow
{
    private readonly UICapture capture;
    private readonly Action onRightClick;

    public IndicatorWindow(string name, UICapture capture, Action onRightClick)
        : base(name)
    {
        this.capture = capture;
        this.onRightClick = onRightClick;
    }

    protected override ImGuiWindowFlags ExtraWindowFlags() => ImGuiWindowFlags.AlwaysAutoResize;

    protected override void DrawContent(ImDrawListPtr dl)
    {
        if (!Plugin.EnableIndicator) return;

        string? inactiveReason = capture.InactiveReason();
        uint dotColor = !Plugin.Enable ? 0xFF2222DD
                      : inactiveReason == null ? 0xFF22DD22
                      : 0xFF00FFFFu;
        const float dotRadius = 4f;
        var pad = new Vector2(6f, 6f);
        var boxSize = new Vector2(dotRadius) + pad * 2;

        var p = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##gbui_kill", boxSize);
        bool leftClicked = ImGui.IsItemClicked();
        bool rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(!Plugin.Enable ? "Off" : inactiveReason == null ? "On" : inactiveReason);
        }

        dl.AddCircleFilled(p + pad, dotRadius + 2, 0xB0000000u);
        dl.AddCircleFilled(p + pad, dotRadius, dotColor);

        if (leftClicked)
        {
            Plugin.Enable = !Plugin.Enable;
        }
        if (rightClicked)
        {
            onRightClick();
        }
    }
}
