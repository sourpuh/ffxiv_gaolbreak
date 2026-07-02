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

    private const string CommandName = "/gaolbreak";

    private bool liftFgOverlay;
    // Last frame's CaptureActive - prevents one frame UI loss when capture is disabled.
    private bool prevCaptureActive;

    private readonly WindowSystem windowSystem = new("GaolbreakUI");
    private readonly WindowManager windowManager;
    private readonly AddonLayer addonLayer;
    private readonly Capturer capturer;
    private readonly HeartbeatWriter heartbeat;
    private readonly UIOverlayWindow fgOverlay;
    private readonly OverlayWindow bgOverlay;
    private readonly IndicatorWindow indicator;
    private readonly ConfigWindow configWindow;
    private readonly DynamicConfig remoteConfig;
    private readonly Config config;
    private readonly RestBeaconAddon beacon;

    public Plugin()
    {
        remoteConfig = new DynamicConfig(PluginInterface);
        config = new(PluginInterface, remoteConfig);
        windowManager = new(config);
        addonLayer = new AddonLayer(config, GameGui, Condition);
        capturer = new Capturer(config, addonLayer, Hooker);
        heartbeat = new HeartbeatWriter();
        var name = PluginInterface.InternalName;
        fgOverlay = new UIOverlayWindow($"###{name}ForegroundOverlay", config, addonLayer, capturer.DrawFgTexture, Hooker, windowManager);
        bgOverlay = new OverlayWindow($"###{name}BackgroundOverlay", capturer.DrawBgTexture);
        indicator = new IndicatorWindow($"###{name}Indicator", config, capturer, OpenConfig);
        beacon = new RestBeaconAddon();
        configWindow = new ConfigWindow($"{name}##Config", config, capturer, fgOverlay, bgOverlay, addonLayer, windowManager);
        windowSystem.AddWindow(configWindow);
        windowManager.InitOverlays(fgOverlay, bgOverlay, indicator);

        PluginInterface.UiBuilder.DisableAutomaticUiHide = true;
        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
        AddonLifecycle.RegisterListener(AddonEvent.PostShow, addonLayer.OnAddonPostShow);
        AddonLifecycle.RegisterListener(AddonEvent.PostShow, windowManager.OnAddonPostShow);
        AddonLifecycle.RegisterListener(AddonEvent.PreDraw, addonLayer.OnAddonPreDraw);
        AddonLifecycle.RegisterListener(AddonEvent.PostDraw, addonLayer.OnAddonPostDraw);
        addonLayer.OnForegroundAddonShown += OnForegroundAddonShown;
        fgOverlay.OnAddonLmbDown += windowManager.QueuePinLift;
        fgOverlay.OnWindowLmbDown += addonLayer.OnWindowLmbDown;
        fgOverlay.OnAddonLmbDown += addonLayer.OnAddonLmbDown;
        Framework.Update += Update;

        var commandInfo = new CommandInfo((_, _) => OpenConfig())
        {
            HelpMessage = "Toggle the Config window.",
        };
        CommandManager.AddHandler(CommandName, commandInfo);
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        AddonLifecycle.UnregisterListener(addonLayer.OnAddonPostShow);
        AddonLifecycle.UnregisterListener(windowManager.OnAddonPostShow);
        AddonLifecycle.UnregisterListener(addonLayer.OnAddonPreDraw);
        AddonLifecycle.UnregisterListener(addonLayer.OnAddonPostDraw);
        addonLayer.OnForegroundAddonShown -= OnForegroundAddonShown;
        fgOverlay.OnAddonLmbDown -= windowManager.QueuePinLift;
        fgOverlay.OnWindowLmbDown -= addonLayer.OnWindowLmbDown;
        fgOverlay.OnAddonLmbDown -= addonLayer.OnAddonLmbDown;
        Framework.Update -= Update;
        CommandManager.RemoveHandler(CommandName);

        beacon.Dispose();
        fgOverlay.Dispose();
        bgOverlay.Dispose();
        indicator.Dispose();
        windowSystem.RemoveAllWindows();
        capturer.Dispose();
        heartbeat.Dispose();
        remoteConfig.Dispose();
    }

    private void OnForegroundAddonShown() => liftFgOverlay = true;

    private void OpenConfig() => configWindow.IsOpen = !configWindow.IsOpen;

    internal void Update(IFramework framework)
    {
        capturer.Update();
        capturer.CollectDiagnostics = configWindow.IsOpen;
        beacon.Open();

        if (config.Enable && capturer.CaptureActive && capturer.UiFresh)
            heartbeat.Tick();
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

            bool showCapture = capturer.UiFresh && (capturer.CaptureActive || prevCaptureActive);
            prevCaptureActive = capturer.CaptureActive;
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
            windowManager.Update();
            if (config.EnableReorder)
                windowManager.ProcessPinLifts();
        }
        catch (Exception e)
        {
            Log.Error(e, "OnDraw failed");
        }
        finally
        {
            if (config.EnableIndicator)
                indicator.BringToFront();
        }
    }
}
