using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Gaolbreak.Overlay;
using System.Drawing;
using System.Numerics;

namespace Gaolbreak;

internal unsafe class WindowManager(Config config)
{
    // back-to-front
    private readonly List<ImGuiWindowPtr> drawOrder = new();
    private readonly HashSet<uint> pendingLifts = new();
    private readonly List<ImGuiWindowPtr> liftBuffer = new();

    private OverlayWindow fgOverlay = null!;
    private OverlayWindow bgOverlay = null!;
    private ImGuiWindowPtr lastFocusedWindow;

    public void InitOverlays(OverlayWindow fgOverlay, OverlayWindow bgOverlay)
    {
        this.fgOverlay = fgOverlay;
        this.bgOverlay = bgOverlay;
    }

    public void Update()
    {
        drawOrder.Clear();
        var ctx = ImGui.GetCurrentContext();
        foreach (var w in ctx.Windows)
        {
            drawOrder.Add(w);
            config.ResolvePinPatterns(w);
        }
        lastFocusedWindow = ctx.WindowsFocusOrder.LastOrDefault();
    }

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
            if (w.Hidden || !w.WasActive) continue;
            if (filter && !IsTopLevel(w)) continue;
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
        foreach (var id in config.GetPinnedWindows(addonName))
        {
            if (!config.IsPinnedToAddon(id)) continue;
            pendingLifts.Add(id);
        }
    }

    public void ProcessReordering()
    {
        ProcessAddonPins();
        EnforceLayerPins();
    }

    private void ProcessAddonPins()
    {
        ImGuiWindowPtr fgWindow = fgOverlay.GetNativeWindow();
        if (fgWindow.IsNull) { pendingLifts.Clear(); return; }

        int overlayIdx = DrawOrder(fgWindow);
        if (overlayIdx < 0) { pendingLifts.Clear(); return; }

        liftBuffer.Clear();
        for (int i = 0; i < overlayIdx; i++)
        {
            var w = drawOrder[i];
            if (w.IsNull) continue;
            if (pendingLifts.Contains(w.ID))
                liftBuffer.Add(w);
        }
        pendingLifts.Clear();
        if (liftBuffer.Count > 0)
        {
            ImGuiWindowPtr frontNeighbor = overlayIdx + 1 < drawOrder.Count ? drawOrder[overlayIdx + 1] : default;
            foreach (var w in liftBuffer)
            {
                if (!frontNeighbor.IsNull)
                    CImGui.igBringWindowToDisplayBehind(w.Handle, frontNeighbor.Handle);
                else
                    CImGui.igBringWindowToDisplayFront(w.Handle);
            }
        }
    }

    private void EnforceLayerPins()
    {
        if (!OverlaysActive())
            return;
        bool seenRegular = false;
        foreach (var w in drawOrder)
        {
            if (!IsTopLevel(w)) continue;
            if (config.TryGetPinAnchor(w.ID, out var anchor) && anchor == Config.BelowAllAnchor)
            {
                if (seenRegular) CImGui.igBringWindowToDisplayBehind(w.Handle, bgOverlay.GetNativeWindow().Handle);
            }
            else
                seenRegular = true;
        }

        liftBuffer.Clear();
        seenRegular = false;
        for (int i = drawOrder.Count - 1; i >= 0; i--)
        {
            var w = drawOrder[i];
            if (!IsTopLevel(w)) continue;
            if (config.TryGetPinAnchor(w.ID, out var anchor) && anchor == Config.AboveAllAnchor)
            {
                if (seenRegular) liftBuffer.Add(w);
            }
            else
                seenRegular = true;
        }
        liftBuffer.Reverse();
        foreach (var w in liftBuffer)
            CImGui.igBringWindowToDisplayFront(w.Handle);
    }

    private bool OverlaysActive()
    {
        var fg = fgOverlay.GetNativeWindow();
        var bg = bgOverlay.GetNativeWindow();
        return !fg.IsNull && !bg.IsNull && fg.WasActive && bg.WasActive;
    }

    public bool TryGetPinnableAnchor(ImGuiWindowPtr window, out string anchor)
    {
        anchor = "";
        if (!OverlaysActive()) return false;

        if (IsInFront(window, fgOverlay.GetNativeWindow()))
            anchor = Config.AboveAllAnchor;
        else if (IsInFront(bgOverlay.GetNativeWindow(), window))
            anchor = Config.BelowAllAnchor;
        else
            return false; // nothing to anchor to between the overlays
        return true;
    }

    private static bool IsTopLevel(ImGuiWindowPtr w) =>
        !w.IsNull && !w.Hidden
        && (w.Flags & (ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.Popup | ImGuiWindowFlags.Tooltip)) == 0;

    public ImGuiWindowPtr FindWindow(uint id)
    {
        foreach (var w in drawOrder)
            if (!w.IsNull && w.ID == id) return w;
        return default;
    }

    public void MoveWindowTo(ImGuiWindowPtr window, ImGuiWindowPtr target)
    {
        if (window.IsNull || target.IsNull || window == target) return;
        int src = DrawOrder(window), dst = DrawOrder(target);
        if (src < 0 || dst < 0) return;

        if (src > dst)
        {
            CImGui.igBringWindowToDisplayBehind(window.Handle, target.Handle);
        }
        else
        {
            if (dst + 1 < drawOrder.Count)
                CImGui.igBringWindowToDisplayBehind(window.Handle, drawOrder[dst + 1].Handle);
            else
                CImGui.igBringWindowToDisplayFront(window.Handle);
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

    private static bool IsHoverEligible(ImGuiWindowPtr w) =>
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
