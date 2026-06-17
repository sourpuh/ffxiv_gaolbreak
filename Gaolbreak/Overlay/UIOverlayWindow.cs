using Dalamud.Bindings.ImGui;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Gaolbreak.Overlay;

internal unsafe class UIOverlayWindow : OverlayWindow
{
    private delegate void GetAddonCollisionDelegate(AtkUnitManager* self, AddonCollision* collisionInfo, short x, short y);
    private readonly Hook<GetAddonCollisionDelegate>? hook;
    private readonly WindowManager windowManager;
    private readonly Config config;
    private AtkUnitBase* hoverAddon;
    private enum PressOwner { None, Window, Game }
    private PressOwner pressOwner;
    private bool prevLmbDown;
    private bool currLmbDown;
    private bool lmbJustPressed => currLmbDown && !prevLmbDown;

    private string lastClickAddon = "—";
    private string lastClickWindow = "—";
    private string lastClickTop = "—";

    public event Action<string>? OnWindowLmbDown;
    public event Action<string>? OnAddonLmbDown;

    public UIOverlayWindow(string name, Config config, Action<ImDrawListPtr> drawAction, IGameInteropProvider hooker, WindowManager windowManager)
        : base(name, drawAction)
    {
        this.config = config;
        this.windowManager = windowManager;
        try
        {
            hook = hooker.HookFromAddress<GetAddonCollisionDelegate>((nint)RaptureAtkUnitManager.StaticVirtualTablePointer->GetAddonCollision, GetAddonCollisionDetour);
            hook.Enable();
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e, "failed to install hook.");
        }
    }

    protected override ImGuiWindowFlags ExtraWindowFlags()
    {
        var ctx = ImGui.GetCurrentContext();
        ImGuiWindowPtr hovered = ctx.HoveredWindow;
        ImGuiWindowPtr overlay = GetNativeWindow();
        bool windowHovered = !hovered.IsNull && hovered != overlay;

        var mouse = ImGui.GetMousePos();
        hoverAddon = AddonCollisionResolver.GetAddonCollision((short)mouse.X, (short)mouse.Y);
        AtkUnitBase* intersecting = hoverAddon;
        bool bgAddonHovered = IsBackgroundAddon(intersecting);
        if (bgAddonHovered && !windowHovered)
        {
            ImGuiWindowPtr behind = windowManager.WindowAt(ImGui.GetMousePos(), overlay);
            if (!behind.IsNull)
            {
                hovered = behind;
                windowHovered = true;
            }
        }

        bool onAddon = intersecting != null && !(windowHovered && bgAddonHovered);
        prevLmbDown = currLmbDown;
        var input = UIInputData.Instance();
        var held = input != null ? input->CursorInputs.MouseButtonHeldFlags : MouseButtonFlags.None;
        currLmbDown = ImGui.IsMouseDown(ImGuiMouseButton.Left) || (held & MouseButtonFlags.LBUTTON) != 0;
        var lmbJustReleased = !currLmbDown && prevLmbDown;
        if (lmbJustPressed)
        {
            pressOwner = windowHovered ? PressOwner.Window : onAddon ? PressOwner.Game : PressOwner.None;
            if (pressOwner is PressOwner.Window)
            {
                OnWindowLmbDown?.Invoke(hovered.GetName());
            }
            if (pressOwner is PressOwner.Game && intersecting != null)
            {
                OnAddonLmbDown?.Invoke(intersecting->NameString);
            }

            lastClickAddon = intersecting != null ? intersecting->NameString : "—";
            lastClickWindow = windowHovered ? hovered.GetName() : "—";
            lastClickTop = OwnerLabel(pressOwner);
        }
        else if (lmbJustReleased)
        {
            pressOwner = PressOwner.None;
        }

        bool windowAboveOverlay = windowHovered && windowManager.IsInFront(hovered, overlay);
        var isHoveredWindowAboveOverlay = currLmbDown ? pressOwner == PressOwner.Window : windowAboveOverlay;
        if (isHoveredWindowAboveOverlay)
            return ImGuiWindowFlags.NoInputs;

        if (pressOwner is PressOwner.Game)
            return ImGuiWindowFlags.None;

        return onAddon ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoInputs;
    }

    private static bool IsBackgroundAddon(AtkUnitBase* addon)
        => addon != null && addon->RootNode != null && addon->RootNode->NodeFlags.HasFlag(NodeFlags.UseDepthBasedPriority);

    private static string OwnerLabel(PressOwner o) => o switch
    {
        PressOwner.Window => "ImGui",
        PressOwner.Game => "Game",
        _ => "—",
    };

    public readonly record struct MouseDiag(
        string HoverAddon, string HoverWindow, string HoverTop,
        string ClickAddon, string ClickWindow, string ClickTop);

    public MouseDiag MouseDiagnostics()
    {
        string hoverAddonName = hoverAddon != null ? hoverAddon->NameString : "—";

        ImGuiWindowPtr hovered = ImGui.GetCurrentContext().HoveredWindow;
        ImGuiWindowPtr overlay = GetNativeWindow();
        bool windowHovered = !hovered.IsNull && hovered != overlay;
        bool onAddon = hoverAddonName != "—";

        string hoverWindow = windowHovered ? hovered.GetName() : "—";
        string hoverTop =
            windowHovered && onAddon ? (windowManager.IsInFront(hovered, overlay) ? "ImGui" : "Game")
            : windowHovered ? "ImGui"
            : onAddon ? "Game"
            : "—";

        return new(hoverAddonName, hoverWindow, hoverTop, lastClickAddon, lastClickWindow, lastClickTop);
    }

    private void GetAddonCollisionDetour(AtkUnitManager* self, AddonCollision* collisionInfo, short x, short y)
    {
        hook!.Original(self, collisionInfo, x, y);
        if (config.Enable && WindowOwnsCursor(x, y) && !UIModule.Instance()->IsPadModeEnabled())
        {
            collisionInfo->UnitBase = null;
            collisionInfo->CollisionNode = null;
        }
    }

    private bool WindowOwnsCursor(short x, short y)
    {
        if (currLmbDown && pressOwner is PressOwner.Window)
            return true;
        ImGuiWindowPtr overlay = GetNativeWindow();
        if (overlay.IsNull)
            return false;

        var hovered = windowManager.WindowAt(new(x, y), overlay);
        if (IsBackgroundAddon(AddonCollisionResolver.GetAddonCollision(x, y)))
            return !hovered.IsNull;

        return !hovered.IsNull && windowManager.IsInFront(hovered, overlay);
    }

    protected override void DrawContent(ImDrawListPtr drawList)
    {
        base.DrawContent(drawList);
        if ((currentFlags & ImGuiWindowFlags.NoInputs) != 0)
            return;

        var io = ImGui.GetIO();
        io.WantCaptureMouse = false;
        io.WantCaptureMouseUnlessPopupClose = false;
        ImGui.SetNextFrameWantCaptureMouse(false);

        if (config.EnableReorder && lmbJustPressed && hoverAddon != null)
        {
            BringToFront();
        }
    }

    public override void Dispose()
    {
        hook?.Dispose();
        base.Dispose();
    }
}
