using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using AtkServer = FFXIVClientStructs.FFXIV.Component.GUI.AtkServer;
using Context = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Context;
using ImmediateContext = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.ImmediateContext;
using RenderCommandSetTarget = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.RenderCommandSetTarget;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace Gaolbreak;

internal unsafe partial class Capturer
{
    private delegate void AtkServerDrawDelegate(AtkServer* self, bool a2);
    private Hook<AtkServerDrawDelegate>? atkServerDrawHook;

    private delegate void SetRenderTargetsDelegate(Context* context, int count, Texture** renderTargets, Texture* depthBuffer, short a5, short a6, short a7, short a8);
    private Hook<SetRenderTargetsDelegate>? setRenderTargetsHook;

    private const string ApplySetTargetSig = "E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? B1 48";
    private delegate void ApplySetTargetCommandDelegate(ImmediateContext* self, RenderCommandSetTarget* command);
    private Hook<ApplySetTargetCommandDelegate>? applySetTargetHook;

    private const string CommitCommandSig = "48 89 5C 24 ?? 83 41 68 FF";
    private delegate ulong CommitCommandDelegate(long* drawState, long record, ushort seq);
    private Hook<CommitCommandDelegate>? commitCommandHook;

    private const string ClipMaskSig = "40 53 55 57 41 56 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 65 48 8B 04 25 ?? ?? ?? ??";
    private delegate void ClipMaskDelegate(long drawState, byte begin, Texture* target, float* matrix, byte emitMask);
    private Hook<ClipMaskDelegate>? clipMaskHook;

    private delegate void ProcessCommandsDelegate(ImmediateContext* self, void* group, uint count);
    private Hook<ProcessCommandsDelegate>? processCommandsHook;

    private bool hooksEnabled;

    private bool TryInstallHooks(IGameInteropProvider hooker)
    {
        atkServerDrawHook = TryHook(() => hooker.HookFromAddress<AtkServerDrawDelegate>(AtkServer.Addresses.Draw.Value, AtkServerDrawDetour));
        setRenderTargetsHook = TryHook(() => hooker.HookFromAddress<SetRenderTargetsDelegate>(Context.Addresses.SetRenderTargets.Value, SetRenderTargetsDetour));
        applySetTargetHook = TryHook(() => hooker.HookFromSignature<ApplySetTargetCommandDelegate>(ApplySetTargetSig, ApplySetTargetCommandDetour));
        commitCommandHook = TryHook(() => hooker.HookFromSignature<CommitCommandDelegate>(CommitCommandSig, CommitCommandDetour));
        clipMaskHook = TryHook(() => hooker.HookFromSignature<ClipMaskDelegate>(ClipMaskSig, ClipMaskDetour));
        processCommandsHook = TryHook(() => hooker.HookFromAddress<ProcessCommandsDelegate>(ImmediateContext.Addresses.ProcessCommands.Value, ProcessCommandsDetour));

        return atkServerDrawHook == null
            || setRenderTargetsHook == null
            || applySetTargetHook == null
            || commitCommandHook == null
            || clipMaskHook == null
            || processCommandsHook == null;
    }

    private static Hook<T>? TryHook<T>(Func<Hook<T>> hook) where T : Delegate
    {
        try
        {
            return hook();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void SetHooksEnabled(bool enabled)
    {
        if (enabled)
        {
            setRenderTargetsHook?.Enable();
            atkServerDrawHook?.Enable();
            applySetTargetHook?.Enable();
            commitCommandHook?.Enable();
            clipMaskHook?.Enable();
            processCommandsHook?.Enable();
        }
        else
        {
            setRenderTargetsHook?.Disable();
            atkServerDrawHook?.Disable();
            applySetTargetHook?.Disable();
            commitCommandHook?.Disable();
            clipMaskHook?.Disable();
            processCommandsHook?.Disable();
        }
    }

    private void DisposeHooks()
    {
        setRenderTargetsHook?.Dispose();
        atkServerDrawHook?.Dispose();
        applySetTargetHook?.Dispose();
        commitCommandHook?.Dispose();
        clipMaskHook?.Dispose();
        processCommandsHook?.Dispose();
    }

    private static CaptureDiag HookStep<T>(string label, Hook<T>? hook) where T : Delegate =>
        hook == null ? new(label, false, "signature didn't match — likely a game update")
        : !hook.IsEnabled ? new(label, false, "resolved but not enabled")
        : new(label, true);

    private void AddHookDiagnostics(List<CaptureDiag> steps)
    {
        steps.Add(HookStep("SetRenderTargets hook", setRenderTargetsHook));
        steps.Add(HookStep("AtkServerDraw hook", atkServerDrawHook));
        steps.Add(HookStep("ApplySetTargetCommand hook", applySetTargetHook));
        steps.Add(HookStep("CommitCommand hook", commitCommandHook));
        steps.Add(HookStep("ClipMask hook", clipMaskHook));
        steps.Add(HookStep("ProcessCommands hook", processCommandsHook));
    }
}
