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
using TextureFlags = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFlags;
using TextureFormat = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFormat;
using ThreadLocals = FFXIVClientStructs.Interop.ThreadLocals;

namespace Gaolbreak;

internal unsafe class UICapture : IDisposable
{
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

    private Texture* sentinelStart = null;
    private Texture* sentinelEnd = null;
    private Texture* fullResDepthPtr = null;
    private Context* atkDrawCtx = null;
    private uint uiStartKey;
    private bool uiBindSeen;
    private bool queueHookFired;
    private bool inAtkServerDraw;
    private bool fgCleared;
    private bool bgCleared;
    private bool captureActive;
    private bool hooksEnabled;
    private string? blockedReason;

    // Sort key layout (Context+8): layer<<28 | sublayer<<24 | seq24.
    private const uint KeyLayerMask = 0xF0000000;
    private const uint KeyInLayerMax = 0x0FFFFFFF;
    private const uint KeySeqMax = 0x00FFFFFF;

    private const uint BgWindowStartKey = 0xCE000000;
    private const uint BgWindowEndKey = BgWindowStartKey | KeySeqMax;

    // Diagnostics
    public bool CollectDiagnostics;
    public string rtmSnapshot = "";
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
        if (sentinelStart != null) { sentinelStart->DecRef(); sentinelStart = null; }
        if (sentinelEnd != null) { sentinelEnd->DecRef(); sentinelEnd = null; }
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

        if (CollectDiagnostics) { rtmSnapshot = ""; queueSequenceCapture = ""; }

        uiBindSeen = false;
        if (capture)
        {
            EnsureSentinels();
            var tls = ThreadLocals.ThreadLocalInstance();
            atkDrawCtx = tls != null && tls->IsInitialized ? tls->GraphicsKernelContext : null;
            inAtkServerDraw = atkDrawCtx != null;
        }
        AtkServerDrawHook!.Original(self, a2);
        inAtkServerDraw = false;
        if (capture && uiBindSeen)
        {
            uint endKey = (uiStartKey & KeyLayerMask) | KeyInLayerMax;
            EnqueueBind(sentinelEnd, null, endKey);
            if (CollectDiagnostics) queueSequenceCapture += $" < S1@{endKey:X8}";
        }
    }

    private void SetRenderTargetsDetour(Context* context, int count, Texture** renderTargets, Texture* depthBuffer, short a5, short a6, short a7, short a8)
    {
        queueHookFired = true;
        bool uiThread = inAtkServerDraw && context == atkDrawCtx;
        var realTarget = renderTargets[0];
        bool expectedBind = uiThread
            && count == 1
            && depthBuffer == null
            && renderTargets != null
            && IsExpectedTarget(realTarget);

        // BG only needs binding once; I think this might work on accident because no BG addons do sub renders.
        if (expectedBind && !uiBindSeen)
        {
            uiBindSeen = true;
            uiStartKey = context->Key;
            EnqueueBind(sentinelStart, null, uiStartKey & KeyLayerMask);
            if (CollectDiagnostics) queueSequenceCapture += $"S0@{uiStartKey & KeyLayerMask:X8} > ";

            if (BgCapture.SizeEquals(realTarget))
            {
                var sceneDepth = fullResDepthPtr != null ? fullResDepthPtr : Rtm->DepthStencil;
                var sceneTarget = Rtm->SwapChainBackBuffer;
                if (sceneDepth != null && sceneTarget != null)
                {
                    EnqueueBind(BgCapture.NativeTex, sceneDepth, BgWindowStartKey);
                    EnqueueBind(sceneTarget, sceneDepth, BgWindowEndKey);
                    if (CollectDiagnostics) queueSequenceCapture += "BG->native | ";
                }
            }
        }

        // Redirect multiple foreground binds because rendering can fork and render other textures mid-draw.
        if (expectedBind && FgCapture.SizeEquals(realTarget))
        {
            renderTargets[0] = FgCapture.NativeTex;
            if (CollectDiagnostics) queueSequenceCapture += "FG->native | ";
        }

        if (CollectDiagnostics && count >= 1 && renderTargets != null)
        {
            bool isRtm = IsExpectedTarget(realTarget);
            if (uiThread || isRtm)
            {
                var kind = depthBuffer != null ? "BG" : "FG";
                var tid = Environment.CurrentManagedThreadId % 1000;
                var rt = renderTargets[0];
                var name = isRtm ? RtmName(realTarget)
                    : rt == FgCapture.NativeTex ? "FgTex"
                    : rt == BgCapture.NativeTex ? "BgTex"
                    : "Other";
                queueSequenceCapture += $"{kind}:{name}@{context->Key:X8}#t{tid} | ";
            }
        }
        SetRenderTargetsHook!.Original(context, count, renderTargets, depthBuffer, a5, a6, a7, a8);
    }

    private void EnqueueBind(Texture* renderTarget, Texture* depth, uint stampKey)
    {
        if (atkDrawCtx == null) return;
        uint key = atkDrawCtx->Key;
        atkDrawCtx->Key = stampKey;
        SetRenderTargetsHook!.Original(atkDrawCtx, 1, &renderTarget, depth, 0, 0, 0, 0);
        atkDrawCtx->Key = key;
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
                atkDrawCtx = null;
                uiBindSeen = false;
                queueHookFired = false;
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
        steps.Add(new("Killswitch on", config.Enable, config.Enable ? null : "click the indicator to re-enable"));

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

        steps.Add(new("SetRenderTargets hook fired", queueHookFired, queueHookFired ? null : "never observed a UI render"));
        steps.Add(new("Sentinels created", sentinelStart != null && sentinelEnd != null));
        steps.Add(new("apply sequence", true, applySequenceCapture));
        steps.Add(new("queue sequence", true, queueSequenceCapture));

        queueSequenceCapture = "";
        applySequenceCapture = "";

        steps.Add(new("Foreground texture captured", !FgCapture.IsNull, FgCapture.IsNull ? "redirect never reached the UI pass" : null));
        steps.Add(new("Background texture captured", !BgCapture.IsNull, BgCapture.IsNull ? "no depth-tested (nameplate) draws yet" : null));
        steps.Add(new("UI fresh (redirecting now)", UiFresh, UiFresh ? null : $"last redirect > {StaleMs}ms ago"));

        return steps;
    }

    private void ApplySetTargetCommandDetour(ImmediateContext* self, RenderCommandSetTarget* command)
    {
        if (command->RenderTarget0 == sentinelStart)
        {
            if (captureActive)
            {
                try
                {
                    var uiTarget = Rtm->SwapChainBackBuffer;
                    if (uiTarget == null) return;
                    FgCapture.Ensure(uiTarget);
                    BgCapture.Ensure(uiTarget);

                    uiRedirectTick = Environment.TickCount64;
                }
                catch (Exception e)
                {
                    Plugin.Log.Error(e, "FG redirect failed");
                }
            }
            return;
        }
        if (command->RenderTarget0 == sentinelEnd)
        {
            if (!fgCleared) FgCapture.Clear();
            if (!bgCleared) BgCapture.Clear();
            fgCleared = false;
            bgCleared = false;
            return;
        }

        var bb = Rtm->SwapChainBackBuffer;
        var d = command->DepthBuffer;
        if (d != null
            && d->ActualWidth == bb->ActualWidth && d->ActualHeight == bb->ActualHeight
            && d->AllocatedWidth == d->ActualWidth && d->AllocatedHeight == d->ActualHeight)
            fullResDepthPtr = d;

        ApplySetTargetHook!.Original(self, command);

        if (FgCapture.NativeTex != null && command->RenderTarget0 == FgCapture.NativeTex)
        {
            if (CollectDiagnostics) applySequenceCapture += $"FG:native | ";
            if (!fgCleared)
            {
                FgCapture.Clear();
                fgCleared = true;
            }
            uiRedirectTick = Environment.TickCount64;
            return;
        }
        if (BgCapture.NativeTex != null && command->RenderTarget0 == BgCapture.NativeTex)
        {
            if (CollectDiagnostics) applySequenceCapture += "BG:native | ";
            if (!bgCleared)
            {
                BgCapture.Clear();
                bgCleared = true;
            }
            return;
        }
    }

    private static Texture* CreateSentinel()
        => Texture.CreateTexture2D(1, 1, 1, TextureFormat.B8G8R8A8_UNORM,
            TextureFlags.TextureRenderTarget | TextureFlags.TextureType2D, 0);

    private void EnsureSentinels()
    {
        if (sentinelStart == null) sentinelStart = CreateSentinel();
        if (sentinelEnd == null) sentinelEnd = CreateSentinel();
    }
}
