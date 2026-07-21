using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI;
using Gaolbreak.Capture;
using Gaolbreak.Overlay;
using System.Numerics;

namespace Gaolbreak;

internal sealed class ConfigWindow : Window
{
    private const uint OverlayHighlight = 0x6700FFFF;
    private readonly Config config;
    private readonly Capturer capturer;
    private readonly UIOverlayWindow fgOverlay;
    private readonly OverlayWindow bgOverlay;
    private readonly AddonLayer addonLayer;
    private readonly WindowManager windowManager;

    private bool filterWindows = true;
    private bool filterAddonsVisible = true;
    private Vector4 captureBackdrop = Vector4.Zero;
    private readonly RowDragDrop<uint> windowRowDragDrop;

    public ConfigWindow(string name, Config config, Capturer capturer, UIOverlayWindow fgOverlay, OverlayWindow bgOverlay, AddonLayer addonLayer, WindowManager windowManager)
        : base(name)
    {
        this.config = config;
        this.capturer = capturer;
        this.fgOverlay = fgOverlay;
        this.bgOverlay = bgOverlay;
        this.addonLayer = addonLayer;
        this.windowManager = windowManager;
        windowRowDragDrop = new RowDragDrop<uint>(
            "GB_WINDOW_ID",
            landsBelow: (source, target) => windowManager.IsInFront(windowManager.FindWindow(source), windowManager.FindWindow(target)),
            onDrop: (source, target) =>
            {
                windowManager.MoveWindowTo(windowManager.FindWindow(source), windowManager.FindWindow(target));
                config.ClearPin(source);
            });
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(960, 540);
    }

    public override void Draw()
    {
        bool killed = !config.Enable;
        if (ImGui.Checkbox("Killswitch", ref killed))
            config.Enable = !killed;
        ImGui.SameLine();
        bool layer = config.EnableReorder;
        if (ImGui.Checkbox("Layer", ref layer))
            config.EnableReorder = layer;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Layer native UI with ImGui windows.");
        ImGui.SameLine();
        bool indicator = config.EnableIndicator;
        if (ImGui.Checkbox("Indicator", ref indicator))
            config.EnableIndicator = indicator;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show status indicator / killswitch dot.");
        ImGui.SameLine();
        bool toneAdjust = config.EnableToneAdjust;
        if (ImGui.Checkbox("Tone Adjust", ref toneAdjust))
            config.EnableToneAdjust = toneAdjust;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Apply gamma and color filter to the UI.");


        if (!ImGui.BeginTabBar("##gaolbreak_tabs")) return;

        if (ImGui.BeginTabItem("Windows"))
        {
            DrawWindowsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Addons"))
        {
            DrawAddonsTab();
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
            ImGuiTableFlags.SizingStretchSame;

        if (!ImGui.BeginTable("##capture_list", 2, flags)) return;

        ImGui.TableSetupColumn("Background", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Foreground", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        ImGui.TableNextRow();

        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, captureBackdrop.ToUint());
        ImGui.TableNextColumn();
        DrawCapturePane(capturer.BgCapture);
        ImGui.TableNextColumn();
        DrawCapturePane(capturer.FgCapture);

        ImGui.EndTable();
        ImGui.ColorEdit4("Backdrop", ref captureBackdrop, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf);

        DrawCaptureDiagnostics();
        DrawMouseDiagnostics();
    }

    private void DrawCapturePane(CaptureTarget capture)
    {
        ImGui.TextUnformatted($"{capture.Width}x{capture.Height}");

        if (capture.IsNull)
        {
            ImGui.TextDisabled("Waiting for capture.");
        }
        else
        {
            float paneWidth = ImGui.GetContentRegionAvail().X;
            var size = new Vector2(paneWidth, paneWidth * capture.Aspect);
            this.capturer.DrawTexture(ImGui.GetWindowDrawList(), capture, ImGui.GetCursorScreenPos(), size);
            ImGui.Dummy(size);
        }
    }

    private void DrawCaptureDiagnostics()
    {
        if (!ImGui.CollapsingHeader("Diagnostics", ImGuiTreeNodeFlags.DefaultOpen))
            return;
        var green = new Vector4(0.30f, 0.85f, 0.30f, 1f);
        var red = new Vector4(0.90f, 0.35f, 0.35f, 1f);
        int row = 0;
        foreach (var step in capturer.Diagnostics())
        {
            row++;
            ImGui.TextColored(step.Ok ? green : red, step.Ok ? "[ok]" : "[xx]");
            ImGui.SameLine();
            if (!string.IsNullOrEmpty(step.Detail))
            {
                if (ImGui.SmallButton($"copy##diag{row}"))
                    ImGui.SetClipboardText($"{step.Label}: {step.Detail}");
                ImGui.SameLine();
            }
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

    private void DrawWindowsTab()
    {
        var self = ImGui.GetCurrentContext().CurrentWindow;
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
        ImGui.TableSetupColumn("Pin", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn("Layer", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Flags", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Pos", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableHeadersRow();

        windowRowDragDrop.Begin();
        var rows = windowManager.GetVisibleWindows(filterWindows);
        foreach (var row in rows)
        {
            var w = row.window;
            // Skip self to avoid awkward re-ordering when refocusing to pin
            if (w == self) continue;
            using (ImRaii.PushId(unchecked((int)w.ID)))
                DrawWindowRow(row.index, w, row.focused);
        }

        ImGui.EndTable();
    }

    private unsafe void DrawAddonsTab()
    {
        ImGui.Checkbox("Visible only", ref filterAddonsVisible);

        const ImGuiTableFlags flags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingStretchSame;

        if (!ImGui.BeginTable("##addons_list", 5, flags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Layer", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn("Capture", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn("Pos", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableHeadersRow();

        var manager = RaptureAtkUnitManager.Instance();
        if (manager != null)
        {
            var addons = new List<(string Name, uint Layer, bool Visible, bool Bg, Vector2 Pos, Vector2 Size)>();
            var list = &manager->AllLoadedUnitsList;
            for (var i = 0; i < list->Count; i++)
            {
                var addon = list->Entries[i].Value;
                if (addon == null) continue;
                bool visible = addon->IsVisible;
                if (filterAddonsVisible && !visible) continue;
                string name = addon->NameString;
                if (string.IsNullOrEmpty(name)) continue;
                var pos = new Vector2(addon->X, addon->Y);
                var size = new Vector2(addon->GetScaledWidth(true), addon->GetScaledHeight(true));
                addons.Add((name, addon->DepthLayer, visible, addonLayer.IsBackground(addon), pos, size));
            }
            addons.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            var origin = ImGuiHelpers.MainViewport.Pos;
            for (int idx = 0; idx < addons.Count; idx++)
            {
                var a = addons[idx];
                ImGui.TableNextRow();
                using var greyed = ImRaii.PushColor(ImGuiCol.Text, 0xFF808080u, !a.Visible);

                ImGui.TableNextColumn();
                if (ImGuiComponents.IconButton($"copy##addon{idx}", FontAwesomeIcon.Copy))
                    ImGui.SetClipboardText(a.Name);
                ImGui.SameLine();
                ImGui.Selectable($"{a.Name}##addon{idx}", false, ImGuiSelectableFlags.SpanAllColumns);
                if (ImGui.IsItemHovered() && a.Size is { X: > 0, Y: > 0 })
                    ImGui.GetForegroundDrawList().AddRect(origin + a.Pos, origin + a.Pos + a.Size, 0xFF00FFFFu, 0f, ImDrawFlags.None, 2f);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(a.Layer.ToString());

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(a.Bg ? "BG" : "FG");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{a.Pos.X:F0}, {a.Pos.Y:F0}");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{a.Size.X:F0} x {a.Size.Y:F0}");
            }
        }

        ImGui.EndTable();
    }

    private unsafe void DrawWindowRow(int i, ImGuiWindowPtr w, bool focused)
    {
        bool isOverlay = windowManager.IsVisibleOverlay(w);
        ImGui.TableNextRow();

        if (isOverlay)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, OverlayHighlight);
        }
        else if (focused)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, GetStyleColor(ImGuiCol.TabActive));
        }
        ImGui.TableNextColumn();
        using var row = windowRowDragDrop.Row(w.ID, w.Name, !isOverlay);
        if (!isOverlay)
        {
            bool pinned = config.IsUserPinned(w.ID);
            bool defaultPinned = !pinned && config.TryGetPinAnchor(w.ID, out _);
            bool canPin = windowManager.TryGetPinnableAnchor(w, out var possibleAnchor);

            using (ImRaii.PushColor(ImGuiCol.Button, GetStyleColor(ImGuiCol.TabActive), pinned))
            using (ImRaii.PushColor(ImGuiCol.Button, OverlayHighlight, defaultPinned))
            using (ImRaii.Disabled(!pinned && !canPin))
            {
                if (ImGuiComponents.IconButton("pin", FontAwesomeIcon.Thumbtack))
                {
                    if (pinned)
                        config.ClearPin(w.ID);
                    else if (canPin)
                        config.SetPin(w.ID, possibleAnchor);
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(config.TryGetPinAnchor(w.ID, out var pinnedAnchor)
                    ? pinnedAnchor switch
                    {
                        Config.AboveAllAnchor => "Pinned above all addons",
                        Config.BelowAllAnchor => "Pinned below all addons",
                        _ => $"Pinned to {pinnedAnchor}",
                    }
                    : possibleAnchor switch
                    {
                        Config.AboveAllAnchor => "Pin above all addons",
                        Config.BelowAllAnchor => "Pin below all addons",
                        _ => $"Pin to {possibleAnchor}"
                    });
            }
        }
        else
        {
            using (ImRaii.Disabled())
                ImGuiComponents.IconButton("pin_disabled", FontAwesomeIcon.Ban);
        }

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(i.ToString());

        ImGui.TableNextColumn();
        if (ImGuiComponents.IconButton("copy", FontAwesomeIcon.Copy))
            ImGui.SetClipboardText($"0x{w.ID:X8}");

        ImGui.SameLine();
        ImGui.TextUnformatted($"{w.ID:X8}");

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(w.Name);

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(w.Flags.ToString());

        ImGui.TableNextColumn();
        ImGui.TextUnformatted($"{w.Pos.X:F0}, {w.Pos.Y:F0}");

        ImGui.TableNextColumn();
        ImGui.TextUnformatted($"{w.Size.X:F0} x {w.Size.Y:F0}");
    }
}
