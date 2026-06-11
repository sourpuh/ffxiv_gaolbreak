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
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string CommandName = "/gbui";

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

    public Plugin()
    {
        config = new(PluginInterface);
        windowManager = new(config);
        depthManager = new DepthManager(config, GameGui);
        capture = new UICapture(config);
        captureWriter = new SharedCaptureWriter();
        var name = PluginInterface.InternalName;
        fgOverlay = new UIOverlayWindow($"###{name}ForegroundOverlay", config, capture.DrawFgTexture, Hooker, windowManager);
        bgOverlay = new OverlayWindow($"###{name}BackgroundOverlay", capture.DrawBgTexture);
        indicator = new IndicatorWindow($"###{name}Indicator", config, capture, OpenConfig);
        configWindow = new ConfigWindow($"{name}##Config", config, capture, fgOverlay, bgOverlay, depthManager, windowManager);
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
        config.OnEnableChanged += OnEnableChangedHandler;
        fgOverlay.OnAddonLmbDown += windowManager.QueuePinLift;
        fgOverlay.OnWindowLmbDown += depthManager.OnWindowLmbDown;
        fgOverlay.OnAddonLmbDown += depthManager.OnAddonLmbDown;
        Framework.Update += Update;
        CommandManager.AddHandler(CommandName, new CommandInfo((_, _) => OpenConfig())
        {
            HelpMessage = "Toggle the Config window.",
        });
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        AddonLifecycle.UnregisterListener(depthManager.OnAddonPostShow);
        AddonLifecycle.UnregisterListener(depthManager.OnContinuousReapplyAddonPreDraw);
        AddonLifecycle.UnregisterListener(depthManager.OnNamePlateRequestedUpdate);
        AddonLifecycle.UnregisterListener(windowManager.OnAddonPostShow);
        depthManager.OnForegroundAddonShown -= OnForegroundAddonShown;
        config.OnEnableChanged -= OnEnableChangedHandler;
        fgOverlay.OnAddonLmbDown -= windowManager.QueuePinLift;
        fgOverlay.OnWindowLmbDown -= depthManager.OnWindowLmbDown;
        fgOverlay.OnAddonLmbDown -= depthManager.OnAddonLmbDown;
        Framework.Update -= Update;
        CommandManager.RemoveHandler(CommandName);

        depthManager.RestoreAll();
        fgOverlay.Dispose();
        bgOverlay.Dispose();
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

    internal void Update(IFramework framework)
    {
        capture.Update();
        capture.CollectDiagnostics = configWindow.IsOpen;

        if (config.Enable)
        {
            captureWriter.Write(capture);
            depthManager.Update();
        }
    }

    private void OnDraw()
    {
        windowManager.Update();
        try
        {
            windowSystem.Draw();

            indicator.Draw();
            if (!config.Enable)
            {
                return;
            }

            bool showCapture = capture.UiFresh && (capture.CaptureActive || prevCaptureActive);
            prevCaptureActive = capture.CaptureActive;
            if (showCapture)
            {
                fgOverlay.Draw();
                bgOverlay.Draw();

                if (config.EnableReorder && liftFgOverlay)
                {
                    liftFgOverlay = false;
                    fgOverlay.BringToFront();
                }
            }

            // Re-snapshot the draw order so EnforcePinned sees the overlay's just-applied bring-to-front.
            // TODO remove need for this?
            windowManager.Update();
            if (config.EnableReorder)
                windowManager.ProcessPinLifts();
        }
        catch (Exception e)
        {
            Log.Error(e, "[GBUI] OnDraw failed");
        }
        finally
        {
            if (config.EnableIndicator)
                indicator.BringToFront();
        }
    }
}
