using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Interface.Utility;
using Dalamud.Utility.Signatures;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using System.Runtime.InteropServices;
using AtkUnitBase = FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase;
using Device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device;
using Format = SharpDX.DXGI.Format;
using ImmediateContext = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.ImmediateContext;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;
using TextureFlags = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFlags;
using TextureFormat = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFormat;

namespace Gaolbreak;

internal unsafe class UICapture : IDisposable
{
    private delegate void AtkServerDrawDelegate(void* self, bool a2);
    [Signature("48 89 5C 24 ?? 48 89 6C 24 ?? 56 57 41 54 41 56 41 57 48 83 EC 50 44 8B 05 ?? ?? ?? ??", DetourName = nameof(AtkServerDrawDetour))]
    private readonly Hook<AtkServerDrawDelegate>? AtkServerDrawHook = null;

    private delegate void QueueRenderTargetsDelegate(void* context, int count, Texture** renderTargets, Texture* depthBuffer, short a5, short a6, short a7, short a8);
    [Signature("E8 ?? ?? ?? ?? 48 8B 45 F8", DetourName = nameof(QueueRenderTargetsDetour))]
    private readonly Hook<QueueRenderTargetsDelegate>? QueueRenderTargetsHook = null;

    private delegate void ApplySetTargetCommandDelegate(ImmediateContext* self, RenderCommandSetTarget* command);
    [Signature("E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 01 45 23", DetourName = nameof(ApplySetTargetCommandDetour))]
    private readonly Hook<ApplySetTargetCommandDelegate>? ApplySetTargetHook = null;

    [StructLayout(LayoutKind.Explicit, Size = 0x40)]
    private struct RenderCommandSetTarget
    {
        [FieldOffset(0x8)] public Texture* RenderTarget0;
        [FieldOffset(0x30)] public Texture* DepthBuffer;
    }

    internal sealed class CaptureTarget(DeviceContext Context) : IDisposable
    {
        public Texture2D? Tex;
        public RenderTargetView? Rtv;
        public ShaderResourceView? Srv;
        public uint SrcWidth;
        public uint SrcHeight;

        public nint Handle => Srv?.NativePointer ?? nint.Zero;
        public uint Width => (uint)(Tex?.Description.Width ?? 0);
        public uint Height => (uint)(Tex?.Description.Height ?? 0);
        public float Aspect => (float)Height / Width;
        public bool IsNull => Tex == null;

        public void Clear()
        {
            if (Rtv != null)
                Context.ClearRenderTargetView(Rtv, new RawColor4(0, 0, 0, 0));
        }

        public void Dispose()
        {
            Srv?.Dispose();
            Rtv?.Dispose();
            Tex?.Dispose();
            Srv = null;
            Rtv = null;
            Tex = null;
            SrcWidth = SrcHeight = 0;
        }
    }

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
    private void* lastRtContext = null;
    private bool targetDetourArmed;
    private bool fgCleared;
    private bool bgCleared;
    private bool captureActive;
    private bool hooksEnabled;

    public static readonly int[] RtmOffsetCandidates = [
        // UI Target / BackBuffer
        0x570,
        // Gamma / Color Filter Target
        0x370
    ];
    public int[] RtmMatchOffsets = (int[])RtmOffsetCandidates.Clone();

    private int fgBinds, bgBinds;
    private int fgBindsLast, bgBindsLast;
    // TODO update to FG after BG binds >=1 ?
    public int BgOrdinal = 1;
    public int FgOrdinal = 2;
    public bool Gt = true;

    // Diagnostics
    public bool CollectDiagnostics;
    public string rtmSnapshot = "";
    public string applySequenceCapture = "";
    public string queueSequenceCapture = "";
    public string rtmTex = "";

    private long uiRedirectTick;
    private const long StaleMs = 150;
    public bool UiFresh => Environment.TickCount64 - uiRedirectTick < StaleMs;

    public bool CaptureActive => captureActive;

    public UICapture(Config config)
    {
        this.config = config;
        device = new SharpDX.Direct3D11.Device((nint)Device.Instance()->D3D11Forwarder);
        context = device.ImmediateContext;
        FgCapture = new(context);
        BgCapture = new(context);

        premultBlend = CreateBlend(BlendOption.One);
        straightBlend = CreateBlend(BlendOption.SourceAlpha);
        premultBlendCallback = (list, cmd) => { try { context.OutputMerger.SetBlendState(premultBlend, new RawColor4(0, 0, 0, 0), -1); } catch { } };
        straightBlendCallback = (list, cmd) => { try { context.OutputMerger.SetBlendState(straightBlend, new RawColor4(0, 0, 0, 0), -1); } catch { } };
        try
        {
            Plugin.Hooker.InitializeFromAttributes(this);
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e, "[GBUI] failed to install hooks");
        }
    }

    public void Dispose()
    {
        QueueRenderTargetsHook?.Dispose();
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
    {
        // TODO move?
        if (ImGui.IsWindowAppearing())
            CImGui.igBringWindowToDisplayBack(CImGui.igGetCurrentWindow());
        DrawTextureToDrawlist(drawlist, FgCapture.Handle);
    }

    public void DrawBgTexture(ImDrawListPtr drawlist)
    {
        // TODO move?
        if (ImGui.IsWindowAppearing())
            CImGui.igBringWindowToDisplayBack(CImGui.igGetCurrentWindow());
        DrawTextureToDrawlist(drawlist, BgCapture.Handle);
    }

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
            QueueRenderTargetsHook?.Enable();
            AtkServerDrawHook?.Enable();
            ApplySetTargetHook?.Enable();
        }
        else
        {
            QueueRenderTargetsHook?.Disable();
            AtkServerDrawHook?.Disable();
            ApplySetTargetHook?.Disable();
        }
    }

    private void AtkServerDrawDetour(void* self, bool a2)
    {
        bool capture = config.Enable && lastRtContext != null;
        captureActive = capture;

        if (CollectDiagnostics) { rtmSnapshot = ""; queueSequenceCapture = ""; }

        if (capture)
        {
            EnsureSentinels();
            QueueSentinel(sentinelStart);
            if (CollectDiagnostics) queueSequenceCapture += "Queue start > ";
        }
        AtkServerDrawHook!.Original(self, a2);
        if (capture)
        {
            QueueSentinel(sentinelEnd);
            if (CollectDiagnostics) queueSequenceCapture += " < Queue end";
        }
    }

    // TODO replace with thread local context
    private void QueueRenderTargetsDetour(void* context, int count, Texture** renderTargets, Texture* depthBuffer, short a5, short a6, short a7, short a8)
    {
        lastRtContext = context;
        QueueRenderTargetsHook!.Original(context, count, renderTargets, depthBuffer, a5, a6, a7, a8);
    }

    public void Update()
    {
        bool allowed = config.Enable && CaptureAllowed();

        if (allowed != hooksEnabled)
        {
            SetHooksEnabled(allowed);
            hooksEnabled = allowed;
            if (!allowed)
                lastRtContext = null;
        }

        if (!allowed)
            captureActive = false;
    }

    private bool CaptureAllowed()
    {
        try
        {
            return Plugin.ClientState.IsLoggedIn
                && Plugin.ObjectTable.LocalPlayer != null
                && !IsZoning()
                && !Plugin.GameGui.GameUiHidden
                && !IsInCutscene()
                && !IsFaded();
        }
        catch { return false; }
    }

    public string? InactiveReason()
    {
        try
        {
            if (!Plugin.ClientState.IsLoggedIn) return "Not logged in";
            if (Plugin.ObjectTable.LocalPlayer == null) return "No local player";
            if (IsZoning()) return "Zoning";
            if (Plugin.GameGui.GameUiHidden) return "UI hidden";
            if (IsInCutscene()) return "Cutscene";
            if (IsFaded()) return "Faded";
            if (FgCapture.IsNull) return "No capture";
            if (!UiFresh) return "Stale";
            return null;
        }
        catch (Exception e)
        {
            return e.GetType().Name;
        }
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
        steps.Add(HookStep("SetRenderTargets hook", QueueRenderTargetsHook));
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

        steps.Add(new("SetRenderTargets hook fired", lastRtContext != null, lastRtContext != null ? null : "never observed a UI render"));
        steps.Add(new("Sentinels created", sentinelStart != null && sentinelEnd != null));
        steps.Add(new("apply sequence", true, applySequenceCapture));
        steps.Add(new("RTM tex", true, rtmTex));

        queueSequenceCapture = "";
        applySequenceCapture = "";
        fgBinds = bgBinds = 0;

        steps.Add(new("Foreground texture captured", !FgCapture.IsNull, FgCapture.IsNull ? "redirect never reached the UI pass" : null));
        steps.Add(new("Background texture captured", !BgCapture.IsNull, BgCapture.IsNull ? "no depth-tested (nameplate) draws yet" : null));
        steps.Add(new("UI fresh (redirecting now)", UiFresh, UiFresh ? null : $"last redirect > {StaleMs}ms ago"));

        return steps;
    }

    private void ApplySetTargetCommandDetour(ImmediateContext* self, RenderCommandSetTarget* command)
    {
        if (command->RenderTarget0 == sentinelStart)
        {
            targetDetourArmed = true;
            fgBinds = bgBinds = 0;
            fgCleared = false;
            bgCleared = false;
            if (CollectDiagnostics)
            {
                rtmTex = "";
                byte* rtm = (byte*)FFXIVClientStructs.FFXIV.Client.Graphics.Render.RenderTargetManager.Instance();
                foreach (var offset in RtmOffsetCandidates)
                {
                    var tex = (Texture*)*(nint*)(rtm + offset);
                    if (tex != null)
                        rtmTex += $"0x{offset:X}: 0x{(nint)tex:X} 0x{(nint)tex->D3D11Texture2D:X} | ";
                }
            }
            return;
        }
        if (command->RenderTarget0 == sentinelEnd)
        {
            targetDetourArmed = false;
            fgBindsLast = fgBinds;
            bgBindsLast = bgBinds;
            if (!fgCleared) FgCapture.Clear();
            if (!bgCleared) BgCapture.Clear();
            return;
        }

        ApplySetTargetHook!.Original(self, command);

        if (targetDetourArmed && captureActive && IsRtmBuffer(command->RenderTarget0))
        {
            try
            {
                RedirectToCapture(command);
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e, "[GBUI] redirect failed");
            }
        }
    }

    private void EnsureSentinels()
    {
        if (sentinelStart == null)
            sentinelStart = Texture.CreateTexture2D(1, 1, 1, TextureFormat.B8G8R8A8_UNORM,
                TextureFlags.TextureRenderTarget | TextureFlags.TextureType2D, 0);
        if (sentinelEnd == null)
            sentinelEnd = Texture.CreateTexture2D(1, 1, 1, TextureFormat.B8G8R8A8_UNORM,
                TextureFlags.TextureRenderTarget | TextureFlags.TextureType2D, 0);
    }

    private void QueueSentinel(Texture* sentinel)
    {
        if (lastRtContext != null)
        {
            QueueRenderTargetsHook!.Original(lastRtContext, 1, &sentinel, null, 0, 0, 0, 0);
        }
    }

    private bool IsRtmBuffer(Texture* rt) => RtmOffset(rt) >= 0;
    private int RtmOffset(Texture* rt)
    {
        byte* rtm = (byte*)FFXIVClientStructs.FFXIV.Client.Graphics.Render.RenderTargetManager.Instance();
        foreach (var offset in RtmMatchOffsets)
        {
            var tex = (Texture*)*(nint*)(rtm + offset);
            if (rt == tex)
            {
                return offset;
            }
        }
        return -1;
    }

    private void RedirectToCapture(RenderCommandSetTarget* command)
    {
        var rtvs = context.OutputMerger.GetRenderTargets(0, out var currentDsv);
        try
        {
            bool isBackground = currentDsv != null;
            if (isBackground) bgBinds++; else fgBinds++;
            var target = isBackground ? BgCapture : FgCapture;

            if (CollectDiagnostics)
            {
                var type = isBackground ? $"BG{bgBinds}" : $"FG{fgBinds}";
                applySequenceCapture += $"{type}:0x{RtmOffset(command->RenderTarget0):X} | ";
            }

            if (isBackground && bgBinds != BgOrdinal) return;
            if (Gt)
            {
                if (!isBackground && fgBinds < FgOrdinal) return;
            }
            else
            {
                if (!isBackground && fgBinds != FgOrdinal) return;
            }

            EnsureCapture(target, command->RenderTarget0);

            context.OutputMerger.SetTargets(currentDsv, target.Rtv);

            if (!isBackground) uiRedirectTick = Environment.TickCount64;

            ref bool cleared = ref isBackground ? ref bgCleared : ref fgCleared;
            if (!cleared)
            {
                context.ClearRenderTargetView(target.Rtv, new RawColor4(0, 0, 0, 0));
                cleared = true;
             }
        }
        finally
        {
            if (rtvs != null)
                foreach (var r in rtvs) r?.Dispose();
            currentDsv?.Dispose();
        }
    }

    private void EnsureCapture(CaptureTarget target, Texture* engineBb)
    {
        if (engineBb == null) return;
        var res = (nint)engineBb->D3D11Texture2D;
        if (res == nint.Zero) return;

        if (target.Tex != null
            && target.SrcWidth == engineBb->AllocatedWidth
            && target.SrcHeight == engineBb->AllocatedHeight)
        {
            return;
        }

        Marshal.AddRef(res);
        using var bbTex = new Texture2D(res);
        var desc = bbTex.Description;

        target.Dispose();

        desc.BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource;
        desc.CpuAccessFlags = CpuAccessFlags.None;
        desc.OptionFlags = ResourceOptionFlags.None;
        desc.Usage = ResourceUsage.Default;
        desc.Format = ToUNorm(desc.Format);

        target.Tex = new Texture2D(device, desc);
        target.Rtv = new RenderTargetView(device, target.Tex);
        target.Srv = new ShaderResourceView(device, target.Tex);
        target.SrcWidth = engineBb->AllocatedWidth;
        target.SrcHeight = engineBb->AllocatedHeight;
        context.ClearRenderTargetView(target.Rtv, new RawColor4(0, 0, 0, 0));
    }

    private static Format ToUNorm(Format f) => f switch
    {
        Format.B8G8R8A8_Typeless => Format.B8G8R8A8_UNorm,
        Format.R8G8B8A8_Typeless => Format.R8G8B8A8_UNorm,
        Format.R10G10B10A2_Typeless => Format.R10G10B10A2_UNorm,
        _ => f,
    };
}
