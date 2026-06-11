using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Gaolbreak.Overlay;

namespace Gaolbreak;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Hooker { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    private const string CommandName = "/gbui";

    public static event Action<bool>? OnEnableChanged;
    public static bool Enable
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnEnableChanged?.Invoke(value);
        }
    }

    public static bool EnableReorder = true;
    public static bool EnableIndicator = true;
    private bool liftFgOverlay;
    // Last frame's CaptureActive - prevents one frame UI loss when capture is disabled.
    private bool prevCaptureActive;

    private readonly WindowSystem windowSystem = new("GaolbreakUI");
    private readonly WindowManager windowManager;
    private readonly DepthManager depthManager;
    private readonly UICapture capture;
    private readonly SharedCaptureWriter captureWriter;
    private readonly UIOverlayWindow fgOverlay;
    private readonly OverlayWindow bgOverlay;
    private readonly IndicatorWindow indicator;
    private readonly ConfigWindow configWindow;
    private readonly Config config;
    private readonly PinsConfig pins;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Config ?? new Config();
        pins = new(PluginInterface, config);
        windowManager = new(pins);

        Enable = true;

        depthManager = new DepthManager(GameGui);
        capture = new UICapture();
        captureWriter = new SharedCaptureWriter();
        var name = PluginInterface.InternalName;
        fgOverlay = new UIOverlayWindow($"###{name}ForegroundOverlay", capture.DrawFgTexture, Hooker, windowManager);
        bgOverlay = new OverlayWindow($"###{name}BackgroundOverlay", capture.DrawBgTexture);
        indicator = new IndicatorWindow($"###{name}Indicator", capture, OpenConfig);
        configWindow = new ConfigWindow($"{name}##Config", capture, fgOverlay, bgOverlay, depthManager, windowManager);
        windowSystem.AddWindow(configWindow);
        windowManager.InitOverlays(fgOverlay, bgOverlay, indicator);

        PluginInterface.UiBuilder.DisableAutomaticUiHide = true;
        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;

        AddonLifecycle.RegisterListener(AddonEvent.PreDraw, DepthManager.ContinuousReapplyAddons, depthManager.OnContinuousReapplyAddonPreDraw);
        AddonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, "NamePlate", depthManager.OnNamePlateRequestedUpdate);
        AddonLifecycle.RegisterListener(AddonEvent.PostShow, depthManager.OnAddonPostShow);
        AddonLifecycle.RegisterListener(AddonEvent.PostShow, windowManager.OnAddonPostShow);
        depthManager.OnForegroundAddonShown += OnForegroundAddonShown;
        OnEnableChanged += OnEnableChangedHandler;
        fgOverlay.OnAddonLmbDown += windowManager.QueuePinLift;
        fgOverlay.OnWindowLmbDown += depthManager.OnWindowLmbDown;
        fgOverlay.OnAddonLmbDown += depthManager.OnAddonLmbDown;

        CommandManager.AddHandler(CommandName, new CommandInfo((_, _) => OpenConfig())
        {
            HelpMessage = "Toggle the Config window.",
        });
    }

    public void Dispose()
    {
        AddonLifecycle.UnregisterListener(depthManager.OnAddonPostShow);
        AddonLifecycle.UnregisterListener(depthManager.OnContinuousReapplyAddonPreDraw);
        AddonLifecycle.UnregisterListener(depthManager.OnNamePlateRequestedUpdate);
        AddonLifecycle.UnregisterListener(windowManager.OnAddonPostShow);
        depthManager.OnForegroundAddonShown -= OnForegroundAddonShown;
        OnEnableChanged -= OnEnableChangedHandler;
        fgOverlay.OnAddonLmbDown -= windowManager.QueuePinLift;
        fgOverlay.OnWindowLmbDown -= depthManager.OnWindowLmbDown;
        fgOverlay.OnAddonLmbDown -= depthManager.OnAddonLmbDown;

        depthManager.RestoreAll();
        fgOverlay.Dispose();
        bgOverlay.Dispose();
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        windowSystem.RemoveAllWindows();
        capture.Dispose();
        captureWriter.Dispose();
    }

    private void OnForegroundAddonShown() => liftFgOverlay = true;

    private void OnEnableChangedHandler(bool enabled)
    {
        if (enabled) depthManager.InvalidateAll();
        else depthManager.RestoreAll();
    }

    private void OpenConfig() => configWindow.IsOpen = !configWindow.IsOpen;

    private void OnDraw()
    {
        capture.Update();
        capture.CollectDiagnostics = configWindow.IsOpen;

        if (Enable)
        {
            captureWriter.Write(capture);
            depthManager.Update();            
        }

        windowManager.Update();
        try
        {
            windowSystem.Draw();

            indicator.Draw();
            if (!Enable)
            {
                return;
            }

            bool showCapture = capture.UiFresh && (capture.CaptureActive || prevCaptureActive);
            prevCaptureActive = capture.CaptureActive;
            if (showCapture)
            {
                fgOverlay.Draw();
                bgOverlay.Draw();

                if (EnableReorder && liftFgOverlay)
                {
                    liftFgOverlay = false;
                    fgOverlay.BringToFront();
                }
            }

            // Re-snapshot the draw order so EnforcePinned sees the overlay's just-applied bring-to-front.
            // TODO remove need for this?
            windowManager.Update();
            if (EnableReorder)
                windowManager.ProcessPinLifts();
        }
        catch (Exception e)
        {
            Log.Error(e, "[GBUI] OnDraw failed");
        }
        finally
        {
            if (EnableIndicator)
                indicator.BringToFront();
        }
    }
}
