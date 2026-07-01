using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI;
using Gaolbreak.Overlay;
using System.Numerics;

namespace Gaolbreak;

internal sealed class ConfigWindow : Window
{
    private const string NullAddonName = "(none)";
    private readonly Config config;
    private readonly Capturer capture;
    private readonly UIOverlayWindow fgOverlay;
    private readonly OverlayWindow bgOverlay;
    private readonly AddonLayer addonLayer;
    private readonly WindowManager windowManager;

    private bool filterWindows = true;
    private bool filterAddonsVisible = true;

    public ConfigWindow(string name, Config config, Capturer capture, UIOverlayWindow fgOverlay, OverlayWindow bgOverlay, AddonLayer addonLayer, WindowManager windowManager)
        : base(name)
    {
        this.config = config;
        this.capture = capture;
        this.fgOverlay = fgOverlay;
        this.bgOverlay = bgOverlay;
        this.addonLayer = addonLayer;
        this.windowManager = windowManager;
        IsOpen = false;
        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(960, 540);
    }

    public override void Draw()
    {
        var self = ImGui.GetCurrentContext().CurrentWindow;
        bool enable = config.Enable;
        if (ImGui.Checkbox("Enable", ref enable))
            config.Enable = enable;
        ImGui.SameLine();
        bool reorder = config.EnableReorder;
        if (ImGui.Checkbox("Reorder On Click", ref reorder))
            config.EnableReorder = reorder;
        ImGui.SameLine();
        bool indicator = config.EnableIndicator;
        if (ImGui.Checkbox("Indicator", ref indicator))
            config.EnableIndicator = indicator;

        if (!ImGui.BeginTabBar("##gaolbreak_tabs")) return;

        if (ImGui.BeginTabItem("Windows"))
        {
            DrawWindowsTab(self);
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
        DrawCaptureDiagnostics();
        DrawMouseDiagnostics();
    }

    private static void DrawCapturePane(CaptureTarget capture)
    {
        ImGui.TextUnformatted($"{capture.Width}x{capture.Height}");

        if (capture.IsNull)
        {
            ImGui.TextDisabled("Waiting for capture.");
        }
        else
        {
            float paneWidth = ImGui.GetContentRegionAvail().X;
            ImGui.Image((ImTextureID)(ulong)capture.PresentHandle, new Vector2(paneWidth, paneWidth * capture.Aspect));
        }
    }

    private void DrawCaptureDiagnostics()
    {
        if (!ImGui.CollapsingHeader("Diagnostics", ImGuiTreeNodeFlags.DefaultOpen))
            return;
        var green = new Vector4(0.30f, 0.85f, 0.30f, 1f);
        var red = new Vector4(0.90f, 0.35f, 0.35f, 1f);
        int row = 0;
        foreach (var step in capture.Diagnostics())
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

        if (!ImGui.BeginTable("##windows_list", 6, flags)) return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Layer", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Flags", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Pos", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize);
        ImGui.TableHeadersRow();

        // Draw this window first to prevent reordering when clicking into the window
        var rows = windowManager.GetVisibleWindows(filterWindows);
        foreach (var row in rows)
        {
            var w = row.window;
            if (config.IsAlwaysLifted(w)) continue;
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
                if (ImGui.SmallButton($"copy##addon{idx}"))
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
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, 0x6700FFFF);
        }
        else if (focused)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, GetStyleColor(ImGuiCol.TabActive));
        }

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(i.ToString());

        ImGui.TableNextColumn();
        if (ImGui.SmallButton($"copy##{i}"))
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
