using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Gaolbreak.Overlay;
using System.Drawing;
using System.Numerics;

namespace Gaolbreak;

internal unsafe class WindowManager(PinsConfig pins)
{
    // back-to-front
    private readonly List<ImGuiWindowPtr> drawOrder = new();
    private readonly HashSet<uint> pendingLifts = new();
    private readonly List<ImGuiWindowPtr> liftBuffer = new();

    private OverlayWindow fgOverlay = null!;
    private OverlayWindow bgOverlay = null!;
    private OverlayWindow indicator = null!;
    private ImGuiWindowPtr lastFocusedWindow;

    public PinsConfig Pins => pins;

    public void InitOverlays(OverlayWindow fgOverlay, OverlayWindow bgOverlay, OverlayWindow indicator)
    {
        this.fgOverlay = fgOverlay;
        this.bgOverlay = bgOverlay;
        this.indicator = indicator;
    }

    public void Update()
    {
        drawOrder.Clear();
        var ctx = ImGui.GetCurrentContext();
        foreach (var w in ctx.Windows)
        {
            drawOrder.Add(w);
        }
        lastFocusedWindow = ctx.WindowsFocusOrder.LastOrDefault();
    }

    public bool IsAlwaysLifted(ImGuiWindowPtr w) => w == indicator.GetNativeWindow() || w.GetName().StartsWith("##NotifyMainWindow");

    public bool IsVisibleOverlay(ImGuiWindowPtr w)
    {
        return w == fgOverlay.GetNativeWindow() || w == bgOverlay.GetNativeWindow();
    }

    public List<(int index, ImGuiWindowPtr window, bool focused)> GetVisibleWindows(bool filter)
    {
        var result = new List<(int index, ImGuiWindowPtr window, bool focused)>();
        int j = 0;
        for (int i = drawOrder.Count - 1; i >= 0; i--)
        {
            var w = drawOrder[i];
            if (w.Hidden) continue;
            if (filter && (w.Flags & (ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.Popup | ImGuiWindowFlags.Tooltip)) != 0) continue;
            result.Add((j++, w, lastFocusedWindow == w));
        }
        return result;
    }

    public void OnAddonPostShow(AddonEvent type, AddonArgs args)
    {
        QueuePinLift(args.AddonName);
    }

    public void QueuePinLift(string? addonName)
    {
        if (string.IsNullOrEmpty(addonName)) return;
        foreach (var id in pins.GetPinnedWindows(addonName))
        {
            pendingLifts.Add(id);
        }
    }

    public void ProcessPinLifts()
    {
        ImGuiWindowPtr overlay = fgOverlay.GetNativeWindow();
        if (overlay.IsNull) { pendingLifts.Clear(); return; }

        int overlayIdx = DrawOrder(overlay);
        if (overlayIdx < 0) { pendingLifts.Clear(); return; }

        liftBuffer.Clear();
        for (int i = 0; i < overlayIdx; i++)
        {
            var w = drawOrder[i];
            if (w.IsNull) continue;
            if (pendingLifts.Contains(w.ID) || IsAlwaysLifted(w))
                liftBuffer.Add(w);
        }
        pendingLifts.Clear();
        if (liftBuffer.Count == 0) return;

        bool hasFront = overlayIdx + 1 < drawOrder.Count;
        ImGuiWindowPtr frontNeighbor = hasFront ? drawOrder[overlayIdx + 1] : default;
        foreach (var w in liftBuffer)
        {
            if (hasFront)
                CImGui.igBringWindowToDisplayBehind(w.Handle, frontNeighbor.Handle);
            else
                CImGui.igBringWindowToDisplayFront(w.Handle);
        }
    }

    public int DrawOrder(ImGuiWindowPtr window)
    {
        for (int i = 0; i < drawOrder.Count; i++)
            if (drawOrder[i] == window) return i;
        return -1;
    }

    public bool IsInFront(ImGuiWindowPtr a, ImGuiWindowPtr b) => Compare(a, b) > 0;

    public int Compare(ImGuiWindowPtr a, ImGuiWindowPtr b)
    {
        if (a.IsNull || b.IsNull || a == b) return 0;

        int ia = DrawOrder(a), ib = DrawOrder(b);
        if (ia < 0 || ib < 0) return 0;
        return ia.CompareTo(ib);
    }

    public static bool IsHoverEligible(ImGuiWindowPtr w) =>
        !w.IsNull && w.WasActive && !w.Hidden
        && (w.Flags & (ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.ChildWindow
                       | ImGuiWindowFlags.Popup | ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoNav)) == 0;

    public ImGuiWindowPtr WindowAt(Vector2 test, ImGuiWindowPtr ignore = default)
    {
        PointF point = new(test);
        for (int i = drawOrder.Count - 1; i >= 0; i--)
        {
            var w = drawOrder[i];
            if (!IsHoverEligible(w)) continue;
            if (!ignore.IsNull && w == ignore) continue;

            var pos = w.Pos;
            var size = w.Size;

            RectangleF window = new(pos.X, pos.Y, size.X, size.Y);
            if (!window.Contains(point))
                continue;

            var holeSize = w.HitTestHoleSize;
            if (holeSize.X != 0)
            {
                var holeOffset = w.HitTestHoleOffset;
                RectangleF hole = new(pos.X + holeOffset.X, pos.Y + holeOffset.Y, holeSize.X, holeSize.Y);
                if (!hole.Contains(point))
                    continue;
            }

            return w;
        }
        return default;
    }
}
