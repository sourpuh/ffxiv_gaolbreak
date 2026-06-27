using System.Runtime.InteropServices;
using AtkServer = FFXIVClientStructs.FFXIV.Component.GUI.AtkServer;

namespace Gaolbreak.Capture;

internal sealed unsafe class CaptureInjector : IDisposable
{
    private const int GroupFg = 0, GroupBg = 1, GroupBgDepth = 2;

    private readonly Config config;
    private readonly AddonLayer addonLayer;
    private readonly CaptureTarget fg;
    private readonly CaptureTarget bg;

    private bool active;
    private ClipMaskDrawCommand* captureSentinels;
    private UICommandEntry* entriesBuffer;
    private int entriesCapacity;

    private UICommandEntry* prevList;
    private uint prevCount;

    public bool CollectDiagnostics;
    public string Runs = "";

    public CaptureInjector(Config config, AddonLayer addonLayer, CaptureTarget fg, CaptureTarget bg)
    {
        this.config = config;
        this.addonLayer = addonLayer;
        this.fg = fg;
        this.bg = bg;
        captureSentinels = (ClipMaskDrawCommand*)NativeMemory.Alloc(3 * (nuint)sizeof(ClipMaskDrawCommand));
    }

    public void Begin(AtkServer* self)
    {
        active = false;
        if (CollectDiagnostics) Runs = "";
        if (!config.Enable || fg.IsNull || bg.IsNull) return;

        var list = self->UICommandListPtr;
        var count = self->UICommandCount;
        if (list == null || count == 0 || count > self->UICommandPoolSize) return;

        BuildBgList(self->UICommandList, out var newList, out var newCount);

        prevList = list;
        prevCount = count;
        self->UICommandListPtr = newList;
        self->UICommandCount = newCount;
        active = true;
    }

    public void End(AtkServer* self)
    {
        if (!active) return;
        self->UICommandListPtr = prevList;
        self->UICommandCount = prevCount;
        active = false;
    }

    public void Dispose()
    {
        if (entriesBuffer != null)
        {
            NativeMemory.Free(entriesBuffer);
            entriesBuffer = null;
            entriesCapacity = 0;
        }
        if (captureSentinels != null)
        {
            NativeMemory.Free(captureSentinels);
            captureSentinels = null;
        }
    }

    private int GetGroup(ref UICommandEntry e)
        => e.IsDepthPriority ? GroupBgDepth : addonLayer.IsBackground(e.AddonHash) ? GroupBg : GroupFg;

    private static string GroupName(int group) => group switch
    {
        GroupFg => "FG",
        GroupBg => "BG",
        GroupBgDepth => "BG+D",
        _ => $"Unk{group}",
    };

    private void BuildBgList(Span<UICommandEntry> src, out UICommandEntry* newList, out uint newCount)
    {
        captureSentinels[GroupFg].Initialize(fg.NativeTex, false);
        captureSentinels[GroupBg].Initialize(bg.NativeTex, false);
        captureSentinels[GroupBgDepth].Initialize(bg.NativeTex, true);

        var transitions = 0;
        var prevGroup = -1;
        var prevHash = 0;
        for (int i = 0; i < src.Length; i++)
        {
            int group = GetGroup(ref src[i]);
            bool boundary = group != prevGroup;
            if (boundary) { transitions++; prevGroup = group; }
            if (CollectDiagnostics)
            {
                var hash = src[i].AddonHash;
                if (boundary) Runs += "\n" + GroupName(group) + " " + AddonName(hash);
                else if (hash != prevHash) Runs += "," + AddonName(hash);
                prevHash = hash;
            }
        }

        int outCount = src.Length + 1 + transitions;
        if (entriesCapacity < outCount)
        {
            if (entriesBuffer != null) NativeMemory.Free(entriesBuffer);
            entriesCapacity = outCount + 100;
            entriesBuffer = (UICommandEntry*)NativeMemory.Alloc((nuint)(entriesCapacity * sizeof(UICommandEntry)));
        }
        var dst = new Span<UICommandEntry>(entriesBuffer, entriesCapacity);

        int j = 0;
        dst[j++].Command = &captureSentinels[GroupBgDepth].Header;
        prevGroup = GroupBgDepth;
        for (int i = 0; i < src.Length; i++)
        {
            int group = GetGroup(ref src[i]);
            if (group != prevGroup)
            {
                dst[j++].Command = &captureSentinels[group].Header;
                prevGroup = group;
            }
            dst[j++] = src[i];
        }

        newList = entriesBuffer;
        newCount = (uint)j;
    }

    private string AddonName(int hc) => hc == 0 ? "—" : addonLayer.NameOf(hc) ?? $"?{hc:X8}";
}
