using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Gaolbreak.Overlay;
using System.Numerics;
using static Gaolbreak.UICapture;

namespace Gaolbreak;

internal sealed class ConfigWindow : Window
{
    private const string NullAddonName = "(none)";
    private readonly Config config;
    private readonly UICapture capture;
    private readonly UIOverlayWindow fgOverlay;
    private readonly OverlayWindow bgOverlay;
    private readonly DepthManager depthManager;
    private readonly WindowManager windowManager;

    private bool filterWindows = true;

    public ConfigWindow(string name, Config config, UICapture capture, UIOverlayWindow fgOverlay, OverlayWindow bgOverlay, DepthManager depthManager, WindowManager windowManager)
        : base(name)
    {
        this.config = config;
        this.capture = capture;
        this.fgOverlay = fgOverlay;
        this.bgOverlay = bgOverlay;
        this.depthManager = depthManager;
        this.windowManager = windowManager;
        IsOpen = false;
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(960, 540);
    }

    public override void Draw()
    {
        var self = ImGui.GetCurrentContext().CurrentWindow;
        bool enable = config.Enable;
        if (ImGui.Checkbox("Draw Overlay", ref enable))
            config.Enable = enable;
        ImGui.SameLine();
        bool reorder = config.EnableReorder;
        if (ImGui.Checkbox("Reorder On Click", ref reorder))
            config.EnableReorder = reorder;
        ImGui.SameLine();
        bool indicator = config.EnableIndicator;
        if (ImGui.Checkbox("Indicator", ref indicator))
            config.EnableIndicator = indicator;

        if (!ImGui.BeginTabBar("##gbui_tabs")) return;

        if (ImGui.BeginTabItem("Windows"))
        {
            DrawWindowsTab(self);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Capture"))
        {
            DrawCaptureTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawCaptureTab()
    {
        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingStretchSame;

        if (!ImGui.BeginTable("##capture_list", 2, flags)) return;

        ImGui.TableSetupColumn("Background", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Foreground", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        ImGui.TableNextRow();

        //ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, 0xFFFFFFFF);
        ImGui.TableNextColumn();
        DrawCapturePane(capture.BgCapture);

        ImGui.TableNextColumn();
        DrawCapturePane(capture.FgCapture);

        ImGui.EndTable();
        DrawCaptureConfig();
        DrawCaptureDiagnostics();
        DrawMouseDiagnostics();
    }

    private static int[] ToggleOffset(int[] current, int offset, bool on)
    {
        var set = new List<int>(current);
        if (on) { if (!set.Contains(offset)) set.Add(offset); }
        else set.Remove(offset);
        return [.. set];
    }


    private static void DrawCapturePane(CaptureTarget capture)
    {
        ImGui.TextUnformatted($"{capture.Width}x{capture.Height}");

        if (capture.Tex == null)
        {
            ImGui.TextDisabled("Waiting for capture.");
        }
        else
        {
            float paneWidth = ImGui.GetContentRegionAvail().X;
            ImGui.Image((ImTextureID)(ulong)capture.Handle, new Vector2(paneWidth, paneWidth * capture.Aspect));
        }
    }


    private void DrawCaptureConfig()
    {
        if (!ImGui.CollapsingHeader("Config"))
            return;

        ImGui.TextUnformatted("RTM Offset matchers:");
        foreach (var off in RtmOffsetCandidates)
        {
            ImGui.SameLine();
            bool on = Array.IndexOf(capture.RtmMatchOffsets, off) >= 0;
            if (ImGui.Checkbox($"0x{off:X3}", ref on))
                capture.RtmMatchOffsets = ToggleOffset(capture.RtmMatchOffsets, off, on);
        }

        int bgOrd = capture.BgOrdinal, fgOrd = capture.FgOrdinal;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("BG ordinal", ref bgOrd)) capture.BgOrdinal = bgOrd;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("FG ordinal", ref fgOrd)) capture.FgOrdinal = fgOrd;
        ImGui.SameLine();
        ImGui.Checkbox("+", ref capture.Gt);
    }

    private void DrawCaptureDiagnostics()
    {
        if (!ImGui.CollapsingHeader("Diagnostics", ImGuiTreeNodeFlags.DefaultOpen))
            return;
        var green = new Vector4(0.30f, 0.85f, 0.30f, 1f);
        var red = new Vector4(0.90f, 0.35f, 0.35f, 1f);
        foreach (var step in capture.Diagnostics())
        {
            ImGui.TextColored(step.Ok ? green : red, step.Ok ? "[ok]" : "[xx]");
            ImGui.SameLine();
            ImGui.TextUnformatted(step.Detail != null ? $"{step.Label}  —  {step.Detail}" : step.Label);
        }
    }

    private void DrawMouseDiagnostics()
    {
        if (!ImGui.CollapsingHeader("Mouse", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var m = fgOverlay.MouseDiagnostics();
        if (!ImGui.BeginTable("##mouse_diag", 2, ImGuiTableFlags.SizingFixedFit)) return;

        Row("Hover addon", m.HoverAddon);
        Row("Hover window", m.HoverWindow);
        Row("Hover on top", m.HoverTop);
        Row("Last click addon", m.ClickAddon);
        Row("Last click window", m.ClickWindow);
        Row("Last click owner", m.ClickTop);

        ImGui.EndTable();

        static void Row(string label, string value)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(label);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(value);
        }
    }

    internal unsafe uint GetStyleColor(ImGuiCol idx)
    {
        var color = *ImGui.GetStyleColorVec4(idx);
        return color.ToUint();
    }

    private void DrawWindowsTab(ImGuiWindowPtr self)
    {
        ImGui.Checkbox("Filter windows", ref filterWindows);
        ImGui.SameLine();
        ImGui.TextDisabled(filterWindows ? "(filtered to top-level windows)" : "(filter off — including child/popup/tooltip)");

        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingStretchSame;

        if (!ImGui.BeginTable("##windows_list", 7, flags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Layer", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Flags", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Pinned Addon", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Pos", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableHeadersRow();

        var pinOptions = depthManager.GetForegroundAddonNames();

        // Draw this window first to prevent reordering when clicking into the window
        var rows = windowManager.GetVisibleWindows(filterWindows);
        foreach (var row in rows)
            if (row.window == self)
            {
                DrawWindowRow(row.index, row.window, row.focused, pinOptions);
                break;
            }
        foreach (var row in rows)
        {
            var w = row.window;
            if (w == self || windowManager.IsAlwaysLifted(w)) continue;
            DrawWindowRow(row.index, w, row.focused, pinOptions);
        }

        ImGui.EndTable();
    }

    private unsafe void DrawWindowRow(int i, ImGuiWindowPtr w, bool focused, List<string> pinOptions)
    {
        bool isOverlay = windowManager.IsVisibleOverlay(w);
        ImGui.TableNextRow();

        if (isOverlay)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, 0x6700FFFF);
        }
        else if (focused)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, GetStyleColor(ImGuiCol.TabActive));
        }

        uint normalText = GetStyleColor(ImGuiCol.Text);
        using var ineligible = ImRaii.PushColor(ImGuiCol.Text, 0xFF808080u, !isOverlay && !WindowManager.IsHoverEligible(w));

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(i.ToString());

        ImGui.TableNextColumn();
        ImGui.TextUnformatted($"{w.ID:X8}");

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(w.Name);

        ImGui.TableNextColumn();
        ImGui.TextUnformatted("" + w.Flags);

        ImGui.TableNextColumn();
        using (ImRaii.Disabled(isOverlay))
        {
            using var id = ImRaii.PushId((int)w.ID);
            using var pinColor = ImRaii.PushColor(ImGuiCol.Text, normalText);
            string? current = config.GetPinnedAddon(w);
            float resetWidth = ImGui.GetFrameHeight();
            float spacing = ImGui.GetStyle().ItemInnerSpacing.X;
            ImGui.SetNextItemWidth(-(resetWidth + spacing));
            using (var combo = ImRaii.Combo("##pin", current ?? NullAddonName))
            {
                if (combo)
                {
                    if (ImGui.Selectable(NullAddonName, current is null))
                        config.SetPinOverride(w, null);
                    // Keep a pin whose addon isn't currently loaded selectable
                    if (current is not null && !pinOptions.Contains(current))
                        if (ImGui.Selectable($"{current} (not loaded)", true)) { }
                    foreach (var addon in pinOptions)
                        if (ImGui.Selectable(addon, addon == current))
                            config.SetPinOverride(w, addon);
                }
            }
            ImGui.SameLine(0, spacing);
            using (ImRaii.Disabled(!config.HasPinOverride(w)))
            {
                using (ImRaii.PushFont(UiBuilder.IconFont))
                    if (ImGui.Button(FontAwesomeIcon.Undo.ToIconString(), new Vector2(resetWidth)))
                        config.RemovePinOverride(w);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Reset to default");
        }

        ImGui.TableNextColumn();
        ImGui.TextUnformatted($"{w.Pos.X:F0}, {w.Pos.Y:F0}");

        ImGui.TableNextColumn();
        ImGui.TextUnformatted($"{w.Size.X:F0} x {w.Size.Y:F0}");
    }
}
