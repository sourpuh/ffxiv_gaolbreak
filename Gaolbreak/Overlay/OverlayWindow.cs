using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;
using System.Text;

namespace Gaolbreak.Overlay;

internal class OverlayWindow : IDisposable
{
    internal const ImGuiWindowFlags DefaultFlags =
        ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoBackground |
        ImGuiWindowFlags.AlwaysUseWindowPadding |
        ImGuiWindowFlags.NoSavedSettings |
        ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoBringToFrontOnFocus |
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoResize;

    private readonly string name;
    private readonly Action<ImDrawListPtr>? drawAction;
    private ImGuiWindowPtr cachedWindow;
    public ImGuiWindowFlags currentFlags;

    public OverlayWindow(string name, Action<ImDrawListPtr>? drawAction = null)
    {
        this.name = name;
        this.drawAction = drawAction;
    }

    public unsafe ImGuiWindowPtr GetNativeWindow()
    {
        if (cachedWindow.IsNull)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name + "\0");
            fixed (byte* p = nameBytes)
                cachedWindow = CImGui.igFindWindowByName(p);
        }
        return cachedWindow;
    }

    public virtual unsafe void BringToFront()
    {
        var w = GetNativeWindow();
        if (!w.IsNull)
            CImGui.igBringWindowToDisplayFront(w);
    }

    protected virtual ImGuiWindowFlags ExtraWindowFlags() => ImGuiWindowFlags.NoInputs;
    protected virtual void DrawContent(ImDrawListPtr drawList) => drawAction?.Invoke(drawList);

    public void Draw()
    {
        ImGuiHelpers.ForceNextWindowMainViewport();
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGuiHelpers.SetNextWindowPosRelativeMainViewport(Vector2.Zero);

        currentFlags = ExtraWindowFlags();
        if (!currentFlags.HasFlag(ImGuiWindowFlags.AlwaysAutoResize))
            ImGui.SetNextWindowSize(ImGuiHelpers.MainViewport.Size);

        if (ImGui.Begin(name, DefaultFlags | currentFlags))
        {
            cachedWindow = ImGui.GetCurrentContext().CurrentWindow;
            DrawContent(ImGui.GetWindowDrawList());
        }
        ImGui.End();
    }

    public virtual void Dispose() { }
}
