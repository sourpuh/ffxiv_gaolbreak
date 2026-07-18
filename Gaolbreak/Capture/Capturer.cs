using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Gaolbreak.Capture;
using System.Numerics;
using TerraFX.Interop.DirectX;
using AtkServer = FFXIVClientStructs.FFXIV.Component.GUI.AtkServer;
using Context = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Context;
using Device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device;
using ImmediateContext = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.ImmediateContext;
using RenderCommandSetTarget = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.RenderCommandSetTarget;
using RenderTargetManager = FFXIVClientStructs.FFXIV.Client.Graphics.Render.RenderTargetManager;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;
using ThreadLocals = FFXIVClientStructs.Interop.ThreadLocals;

namespace Gaolbreak;

internal unsafe partial class Capturer : IDisposable
{
    private readonly Config config;
    private readonly AddonLayer addonLayer;
    private readonly ID3D11DeviceContext* context;
    private readonly Renderer renderer;
    private readonly ToneAdjustPass toneAdjust;

    public readonly CaptureTarget FgCapture;
    public readonly CaptureTarget BgCapture;

    private Texture* sceneDepth = null;
    private Context* currentCtx = null;
    private bool inAtkServerDraw;
    private bool captureActive;
    private string? blockedReason;

    private readonly CaptureInjector clipBg;

    private bool collectDiagnostics;
    public bool CollectDiagnostics
    {
        get => collectDiagnostics;
        set { collectDiagnostics = value; clipBg.CollectDiagnostics = value; }
    }
    private string queueSequenceCapture = "";
    private string sceneDepthCapture = "";

    private long uiRedirectTick;
    private const long StaleMs = 1000;
    public bool UiFresh => Environment.TickCount64 - uiRedirectTick < StaleMs;

    public bool CaptureActive => captureActive;
    public readonly bool HooksBroken;

    private readonly Lock renderDisposeLock = new();
    private volatile bool disposing;

    private static RenderTargetManager* Rtm => RenderTargetManager.Instance();

    private string Describe(Texture* t)
    {
        if (t == null) return "null";
        if (t == Rtm->SwapChainBackBuffer) return "BackBuffer";
        if (t == Rtm->ToneAdjustSource) return "ToneAdjustSrc";
        if (FgCapture.Matches(t)) return "FgTex";
        if (BgCapture.Matches(t)) return "BgTex";
        return $"tex{t->ActualWidth}x{t->ActualHeight}";
    }

    public Capturer(Config config, AddonLayer addonLayer, IGameInteropProvider hooker)
    {
        this.config = config;
        this.addonLayer = addonLayer;
        var device = (ID3D11Device*)Device.Instance()->D3D11Forwarder;
        ID3D11DeviceContext* context;
        device->GetImmediateContext(&context);
        this.context = context;
        FgCapture = new(context);
        BgCapture = new(context);
        clipBg = new CaptureInjector(config, addonLayer, FgCapture, BgCapture);
        renderer = new Renderer(device, context);
        toneAdjust = new ToneAdjustPass(device);
        HooksBroken = TryInstallHooks(hooker);
    }

    public void Dispose()
    {
        disposing = true;
        lock (renderDisposeLock) { }
        DisposeHooks();

        clipBg.Dispose();
        FgCapture.Dispose();
        BgCapture.Dispose();
        renderer.Dispose();
        toneAdjust.Dispose();
        if (context != null) context->Release();
    }

    public void DrawFgTexture(ImDrawListPtr drawlist)
    {
        renderer.DrawTextureToDrawlist(drawlist, FgCapture.PresentHandle);
    }

    public void DrawBgTexture(ImDrawListPtr drawlist)
    {
        renderer.DrawTextureToDrawlist(drawlist, BgCapture.PresentHandle);
    }

    public void DrawTexture(ImDrawListPtr drawlist, CaptureTarget target, Vector2 pos, Vector2 size)
    {
        renderer.DrawTextureToDrawlist(drawlist, target.PresentHandle, pos, size);
    }

    private void AtkServerDrawDetour(AtkServer* self, bool a2)
    {
        bool capture = config.Enable;
        captureActive = capture;

        addonLayer.ResetHashes();

        if (CollectDiagnostics) queueSequenceCapture = "";

        currentCtx = null;
        inAtkServerDraw = false;
        if (capture)
        {
            var uiTarget = Rtm->SwapChainBackBuffer;
            var ready = true;
            ready &= FgCapture.BeginFrame(uiTarget);
            ready &= BgCapture.BeginFrame(uiTarget);
            if (ready)
            {
                var tls = ThreadLocals.ThreadLocalInstance();
                currentCtx = tls != null && tls->IsInitialized ? tls->GraphicsKernelContext : null;
                inAtkServerDraw = currentCtx != null;
            }
        }
        clipBg.Begin(self);
        atkServerDrawHook!.Original(self, a2);
        clipBg.End(self);
        inAtkServerDraw = false;
        sceneDepth = null;
    }

    private const int DrawStateCursorIndex = 11;

    private ulong CommitCommandDetour(long* drawState, long record, ushort seq)
    {
        var result = commitCommandHook!.Original(drawState, record, seq);
        long cursor = drawState[DrawStateCursorIndex];
        var entry = (AtkUICommandEntry*)cursor - 1;
        entry->AddonHash = addonLayer.CurrentHash;
        return result;
    }


    private const uint BeginDepthBandSortKey = 0xCE00_0000;
    private uint savedFrontSortKey;

    private void ClipMaskDetour(long drawState, byte begin, Texture* target, float* matrix, byte emitMask)
    {
        if (begin != 0 && currentCtx != null && (FgCapture.Matches(target) || BgCapture.Matches(target)))
        {
            var isDepthPriority = emitMask != 0;
            if (isDepthPriority)
            {
                var depth = sceneDepth != null ? sceneDepth : Rtm->DepthStencil;
                int offset = matrix != null ? (int)matrix[1] : 0;
                if (offset == 0)
                {
                    // Beacon front marker
                    savedFrontSortKey = currentCtx->SortKey;
                    currentCtx->SortKey = BeginDepthBandSortKey;
                }
                else
                {
                    // Nameplate
                    currentCtx->SortKey = BeginDepthBandSortKey + (uint)offset;
                    setRenderTargetsHook!.Original(currentCtx, 1, &target, depth, 0, 0, 0, 0);
                    currentCtx->SortKey = savedFrontSortKey;
                }
            }
            else
            {
                setRenderTargetsHook!.Original(currentCtx, 1, &target, null, 0, 0, 0, 0);
            }
            return;
        }
        clipMaskHook!.Original(drawState, begin, target, matrix, emitMask);
    }

    private void SetRenderTargetsDetour(Context* context, int count, Texture** renderTargets, Texture* depthBuffer, short a5, short a6, short a7, short a8)
    {
        if (context != currentCtx)
        {
            setRenderTargetsHook!.Original(context, count, renderTargets, depthBuffer, a5, a6, a7, a8);
            return;
        }

        // Capture full size scene depth; necessary for scaled resolution because RTM DepthStencil is scaled.
        var bb = Rtm->SwapChainBackBuffer;
        if (bb != null && depthBuffer != null && bb->AllocatedSizeEquals(depthBuffer) && depthBuffer->IsFullSize)
        {
            sceneDepth = depthBuffer;
            if (CollectDiagnostics) sceneDepthCapture = $"sceneDepthCapture({depthBuffer->IsFullSize} {Describe(renderTargets[0])})";
        }

        if (CollectDiagnostics && inAtkServerDraw && count >= 1 && renderTargets != null)
        {
            var kind = depthBuffer != null ? "BG" : "FG";
            queueSequenceCapture += $"{kind}[{Describe(renderTargets[0])}]@{context->SortKey >> 24:X2} | ";
        }
        setRenderTargetsHook!.Original(context, count, renderTargets, depthBuffer, a5, a6, a7, a8);
    }

    public void Update()
    {
        if (HooksBroken)
        {
            captureActive = false;
            return;
        }
        blockedReason = Gate.BlockedReason();
        bool allowed = config.Enable && blockedReason == null;

        if (allowed != hooksEnabled)
        {
            SetHooksEnabled(allowed);
            hooksEnabled = allowed;
            if (allowed)
            {
                FgCapture.Invalidate();
                BgCapture.Invalidate();
            }
            else
            {
                currentCtx = null;
                inAtkServerDraw = false;
            }
        }

        if (!allowed)
            captureActive = false;
    }

    public string? InactiveReason()
    {
        if (HooksBroken) return "Capture unavailable — a game update likely broke a signature";
        if (blockedReason != null) return blockedReason;
        if (FgCapture.IsNull) return "No capture";
        if (!UiFresh) return "Stale";
        return null;
    }

    public readonly record struct CaptureDiag(string Label, bool Ok, string? Detail = null);

    public List<CaptureDiag> Diagnostics()
    {
        var steps = new List<CaptureDiag>();
        AddHookDiagnostics(steps);
        steps.Add(new("Enabled", config.Enable, config.Enable ? null : "click the indicator to re-enable"));

        try
        {
            steps.Add(new("Game UI not hidden", !Plugin.GameGui.GameUiHidden));
            steps.Add(new("Not in a cutscene", !Gate.IsInCutscene()));
            steps.Add(new("Not faded", !Gate.IsFaded()));
            steps.Add(new("Not transition", !Gate.IsTransition()));
        }
        catch (Exception e)
        {
            steps.Add(new("Game-state check", false, e.GetType().Name));
        }

        steps.Add(new("UI fresh (redirecting now)", UiFresh, UiFresh ? null : $"last redirect > {StaleMs}ms ago"));
        steps.Add(new("queue sequence", true, queueSequenceCapture));
        steps.Add(new("scene depth capture", true, sceneDepthCapture));
        steps.Add(new("fg/bg runs", true, clipBg.Runs));
        queueSequenceCapture = "";

        return steps;
    }

    private void DoSetTargetCommandDetour(ImmediateContext* self, RenderCommandSetTarget* command)
    {
        doSetTargetHook!.Original(self, command);

        var rt = command->RenderTargets[0].Value;
        if (FgCapture.MaybeBind(rt))
        {
            uiRedirectTick = Environment.TickCount64;
            return;
        }
        if (BgCapture.MaybeBind(rt))
        {
            uiRedirectTick = Environment.TickCount64;
            return;
        }
    }

    private void ProcessCommandsDetour(ImmediateContext* self, void* group, uint count)
    {
        processCommandsHook!.Original(self, group, count);
        if (!config.Enable || disposing) return;
        lock (renderDisposeLock)
        {
            if (disposing) return;
            using var pass = config.EnableToneAdjust ? toneAdjust.Begin(context, Rtm->ToneAdjustSource) : default;
            FgCapture.EndFrame(pass);
            BgCapture.EndFrame(pass);
        }
    }
}
