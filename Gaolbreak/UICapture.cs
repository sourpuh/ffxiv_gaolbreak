using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Interface.Utility;
using Dalamud.Utility.Signatures;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using AtkServer = FFXIVClientStructs.FFXIV.Component.GUI.AtkServer;
using AtkUnitBase = FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase;
using Context = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Context;
using Device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device;
using ImmediateContext = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.ImmediateContext;
using RenderCommandSetTarget = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.RenderCommandSetTarget;
using RenderTargetManager = FFXIVClientStructs.FFXIV.Client.Graphics.Render.RenderTargetManager;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;
using ThreadLocals = FFXIVClientStructs.Interop.ThreadLocals;

namespace Gaolbreak;

internal unsafe class UICapture : IDisposable
{
    private const byte BgBandStart = 0xCE;
    private const byte BgBandEnd = 0xCF;
    private const byte FgBandStart = 0xE0;
    private const byte FgBandEnd = 0xEF;

    private delegate void AtkServerDrawDelegate(AtkServer* self, bool a2);
    private readonly Hook<AtkServerDrawDelegate>? AtkServerDrawHook;

    private delegate void SetRenderTargetsDelegate(Context* context, int count, Texture** renderTargets, Texture* depthBuffer, short a5, short a6, short a7, short a8);
    private readonly Hook<SetRenderTargetsDelegate>? SetRenderTargetsHook;

    private delegate void ApplySetTargetCommandDelegate(ImmediateContext* self, RenderCommandSetTarget* command);
    [Signature("E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? D1 47 23", DetourName = nameof(ApplySetTargetCommandDetour))]
    private readonly Hook<ApplySetTargetCommandDelegate>? ApplySetTargetHook = null;

    private readonly Config config;
    private readonly SharpDX.Direct3D11.Device device;
    private readonly DeviceContext context;

    private readonly BlendState premultBlend;
    private readonly BlendState straightBlend;
    private readonly ImDrawCallback premultBlendCallback;
    private readonly ImDrawCallback straightBlendCallback;

    public readonly CaptureTarget FgCapture;
    public readonly CaptureTarget BgCapture;

    private Texture* sceneDepth = null;
    private Context* currentCtx = null;
    private bool uiBindSeen;
    private bool inAtkServerDraw;
    private bool captureActive;
    private bool hooksEnabled;
    private string? blockedReason;

    // Diagnostics
    public bool CollectDiagnostics;
    public string applySequenceCapture = "";
    public string queueSequenceCapture = "";

    private long uiRedirectTick;
    private const long StaleMs = 1000;
    public bool UiFresh => Environment.TickCount64 - uiRedirectTick < StaleMs;

    public bool CaptureActive => captureActive;

    private static RenderTargetManager* Rtm => RenderTargetManager.Instance();
    private static bool IsExpectedTarget(Texture* rt)
        => rt != null && (rt == Rtm->SwapChainBackBuffer || rt == Rtm->ToneAdjustSource);
    private static string RtmName(Texture* rt)
        => rt == Rtm->SwapChainBackBuffer ? "BackBuffer"
            : rt == Rtm->ToneAdjustSource ? "ToneAdjustSrc"
            : "?";

    public UICapture(Config config)
    {
        this.config = config;
        device = new SharpDX.Direct3D11.Device((nint)Device.Instance()->D3D11Forwarder);
        context = device.ImmediateContext;
        FgCapture = new(device, context);
        BgCapture = new(device, context);

        premultBlend = CreateBlend(BlendOption.One);
        straightBlend = CreateBlend(BlendOption.SourceAlpha);
        premultBlendCallback = (list, cmd) => { try { context.OutputMerger.SetBlendState(premultBlend, new RawColor4(0, 0, 0, 0), -1); } catch { } };
        straightBlendCallback = (list, cmd) => { try { context.OutputMerger.SetBlendState(straightBlend, new RawColor4(0, 0, 0, 0), -1); } catch { } };
        try
        {
            AtkServerDrawHook = Plugin.Hooker.HookFromAddress<AtkServerDrawDelegate>(
                AtkServer.Addresses.Draw.Value, AtkServerDrawDetour);
            SetRenderTargetsHook = Plugin.Hooker.HookFromAddress<SetRenderTargetsDelegate>(
                Context.Addresses.SetRenderTargets.Value, SetRenderTargetsDetour);
            Plugin.Hooker.InitializeFromAttributes(this);
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e, "Failed to install hooks");
        }
    }

    public void Dispose()
    {
        SetRenderTargetsHook?.Dispose();
        AtkServerDrawHook?.Dispose();
        ApplySetTargetHook?.Dispose();

        FgCapture.Dispose();
        BgCapture.Dispose();
        premultBlend.Dispose();
        straightBlend.Dispose();
    }

    private BlendState CreateBlend(BlendOption srcColorBlend)
    {
        var desc = BlendStateDescription.Default();
        desc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            IsBlendEnabled = true,
            SourceBlend = srcColorBlend,
            DestinationBlend = BlendOption.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceAlphaBlend = BlendOption.One,
            DestinationAlphaBlend = BlendOption.InverseSourceAlpha,
            AlphaBlendOperation = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteMaskFlags.All,
        };
        return new BlendState(device, desc);
    }

    public void DrawFgTexture(ImDrawListPtr drawlist)
        => DrawTextureToDrawlist(drawlist, FgCapture.Handle);

    public void DrawBgTexture(ImDrawListPtr drawlist)
        => DrawTextureToDrawlist(drawlist, BgCapture.Handle);

    public void DrawTextureToDrawlist(ImDrawListPtr drawList, nint textureHandle)
    {
        var pos = ImGuiHelpers.MainViewport.Pos;
        var size = ImGuiHelpers.MainViewport.Size;

        drawList.AddCallback(premultBlendCallback, null);
        drawList.AddImage((ImTextureID)(ulong)textureHandle, pos, pos + size);
        drawList.AddCallback(straightBlendCallback, null);
    }

    public void SetHooksEnabled(bool enabled)
    {
        if (enabled)
        {
            SetRenderTargetsHook?.Enable();
            AtkServerDrawHook?.Enable();
            ApplySetTargetHook?.Enable();
        }
        else
        {
            SetRenderTargetsHook?.Disable();
            AtkServerDrawHook?.Disable();
            ApplySetTargetHook?.Disable();
        }
    }

    private void AtkServerDrawDetour(AtkServer* self, bool a2)
    {
        bool capture = config.Enable;
        captureActive = capture;

        if (CollectDiagnostics) queueSequenceCapture = "";

        uiBindSeen = false;
        if (capture)
        {
            var uiTarget = Rtm->SwapChainBackBuffer;
            FgCapture.BeginFrame(uiTarget);
            BgCapture.BeginFrame(uiTarget);
            var tls = ThreadLocals.ThreadLocalInstance();
            currentCtx = tls != null && tls->IsInitialized ? tls->GraphicsKernelContext : null;
            inAtkServerDraw = currentCtx != null;
        }
        AtkServerDrawHook!.Original(self, a2);
        inAtkServerDraw = false;
        sceneDepth = null;
    }

    private void SetRenderTargetsDetour(Context* context, int count, Texture** renderTargets, Texture* depthBuffer, short a5, short a6, short a7, short a8)
    {
        if (context != currentCtx)
        {
            SetRenderTargetsHook!.Original(context, count, renderTargets, depthBuffer, a5, a6, a7, a8);
            return;
        }

        // Capture full size scene depth; necessary for scaled resolution because RTM DepthStencil is scaled.
        var bb = Rtm->SwapChainBackBuffer;
        if (bb != null && depthBuffer != null && bb->AllocatedSizeEquals(depthBuffer) && depthBuffer->IsFullSize)
        {
            sceneDepth = depthBuffer;
        }

        var realTarget = renderTargets[0];
        bool isCapturableBind = inAtkServerDraw
            && count == 1
            && depthBuffer == null
            && renderTargets != null
            && IsExpectedTarget(realTarget)
            && FgCapture.SizeEquals(realTarget);

        if (isCapturableBind)
        {
            if (!uiBindSeen)
            {
                uiBindSeen = true;
                var sceneDepth = this.sceneDepth != null ? this.sceneDepth : Rtm->DepthStencil;
                if (CollectDiagnostics)
                {
                    float rtmDepthScale = MathF.Round(100f * Rtm->DepthStencil->ActualWidth / bb->ActualWidth);
                    float depthScale = MathF.Round(100f * sceneDepth->ActualWidth / bb->ActualWidth);
                    queueSequenceCapture += $"depth({rtmDepthScale}%->{depthScale}%):{(this.sceneDepth == null ? "depthstencil" : "capture")} | ";
                }
                var sceneTarget = Rtm->SwapChainBackBuffer;

                EnqueueBind(BgCapture.NativeTex, sceneDepth, BgBandStart);
                EnqueueBind(sceneTarget, sceneDepth, BgBandEnd);
                if (CollectDiagnostics) queueSequenceCapture += $"BG[{BgBandStart:X2}-{BgBandEnd:X2}] | ";

                EnqueueBind(FgCapture.NativeTex, null, FgBandStart);
                EnqueueBind(sceneTarget, null, FgBandEnd);
                if (CollectDiagnostics) queueSequenceCapture += $"FG[{FgBandStart:X2}-{FgBandEnd:X2}] | ";
            }

            bool bg = context->SubViewLayer == BgBandStart;
            renderTargets[0] = bg ? BgCapture.NativeTex : FgCapture.NativeTex;
        }

        if (CollectDiagnostics && count >= 1 && renderTargets != null)
        {
            if (inAtkServerDraw)
            {
                var kind = depthBuffer != null ? "BG" : "FG";
                var rt = renderTargets[0];
                var realName = RtmName(realTarget);
                var redirectName = rt == FgCapture.NativeTex ? "FgTex"
                    : rt == BgCapture.NativeTex ? "BgTex"
                    : "N/A";
                queueSequenceCapture += $"{kind}[{realName}->{redirectName}]@{context->SubViewLayer:X2} | ";
            }
        }
        SetRenderTargetsHook!.Original(context, count, renderTargets, depthBuffer, a5, a6, a7, a8);
    }

    private void EnqueueBind(Texture* renderTarget, Texture* depth, byte band)
    {
        if (currentCtx == null) return;
        byte saved = currentCtx->SubViewLayer;
        currentCtx->SubViewLayer = band;
        SetRenderTargetsHook!.Original(currentCtx, 1, &renderTarget, depth, 0, 0, 0, 0);
        currentCtx->SubViewLayer = saved;
    }

    public void Update()
    {
        blockedReason = ComputeBlockedReason();
        bool allowed = config.Enable && blockedReason == null;

        if (allowed != hooksEnabled)
        {
            SetHooksEnabled(allowed);
            hooksEnabled = allowed;
            if (!allowed)
            {
                currentCtx = null;
                uiBindSeen = false;
                inAtkServerDraw = false;
            }
        }

        if (!allowed)
            captureActive = false;
    }

    private static string? ComputeBlockedReason()
    {
        try
        {
            if (!Plugin.ClientState.IsLoggedIn) return "Not logged in";
            if (Plugin.ObjectTable.LocalPlayer == null) return "No local player";
            if (IsZoning()) return "Zoning";
            if (Plugin.GameGui.GameUiHidden) return "UI hidden";
            if (IsInCutscene()) return "Cutscene";
            if (IsFaded()) return "Faded";
            return null;
        }
        catch (Exception e)
        {
            return e.GetType().Name;
        }
    }

    public string? InactiveReason()
    {
        if (blockedReason != null) return blockedReason;
        if (FgCapture.IsNull) return "No capture";
        if (!UiFresh) return "Stale";
        return null;
    }

    private static bool IsZoning()
        => Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];

    private static bool IsInCutscene()
        => Plugin.Condition[ConditionFlag.WatchingCutscene] || Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Plugin.Condition[ConditionFlag.WatchingCutscene78];

    private static bool IsFaded()
    {
        // There's a brief period when loading the character creator that the game destroys and recreates all addons while faded.
        // The fade addons are also deleted yet the screen stays faded, so there seems to be another fade mechanism I'm missing.
        var fadeMiddle = (AtkUnitBase*)Plugin.GameGui.GetAddonByName("FadeMiddle").Address;
        var fadeBlack = (AtkUnitBase*)Plugin.GameGui.GetAddonByName("FadeBlack").Address;
        return (fadeMiddle != null && fadeMiddle->IsVisible)
            || (fadeBlack != null && fadeBlack->IsVisible);
    }

    public readonly record struct CaptureDiag(string Label, bool Ok, string? Detail = null);

    private static CaptureDiag HookStep<T>(string label, Hook<T>? hook) where T : Delegate =>
        hook == null ? new(label, false, "signature didn't match — likely a game update")
        : !hook.IsEnabled ? new(label, false, "resolved but not enabled")
        : new(label, true);

    public List<CaptureDiag> Diagnostics()
    {
        var steps = new List<CaptureDiag>();
        steps.Add(HookStep("SetRenderTargets hook", SetRenderTargetsHook));
        steps.Add(HookStep("AtkServerDraw hook", AtkServerDrawHook));
        steps.Add(HookStep("ApplySetTargetCommand hook", ApplySetTargetHook));
        steps.Add(new("Enabled", config.Enable, config.Enable ? null : "click the indicator to re-enable"));

        try
        {
            steps.Add(new("Logged in", Plugin.ClientState.IsLoggedIn));
            steps.Add(new("Local player present", Plugin.ObjectTable.LocalPlayer != null));
            steps.Add(new("Not zoning", !IsZoning()));
            steps.Add(new("Game UI not hidden", !Plugin.GameGui.GameUiHidden));
            steps.Add(new("Not in a cutscene", !IsInCutscene()));
            steps.Add(new("Not faded to black", !IsFaded()));
        }
        catch (Exception e)
        {
            steps.Add(new("Game-state check", false, e.GetType().Name));
        }

        steps.Add(new("UI fresh (redirecting now)", UiFresh, UiFresh ? null : $"last redirect > {StaleMs}ms ago"));

        steps.Add(new("apply sequence", true, applySequenceCapture));
        steps.Add(new("queue sequence", true, queueSequenceCapture));
        queueSequenceCapture = "";
        applySequenceCapture = "";

        return steps;
    }

    // TODO queue native clear commands instead of detouring this?
    private void ApplySetTargetCommandDetour(ImmediateContext* self, RenderCommandSetTarget* command)
    {
        ApplySetTargetHook!.Original(self, command);

        if (command->RenderTarget0 == FgCapture.NativeTex)
        {
            if (CollectDiagnostics) applySequenceCapture += "FG | ";
            FgCapture.Clear();
            uiRedirectTick = Environment.TickCount64;
            return;
        }
        if (command->RenderTarget0 == BgCapture.NativeTex)
        {
            if (CollectDiagnostics) applySequenceCapture += "BG | ";
            BgCapture.Clear();
            return;
        }
    }
}
