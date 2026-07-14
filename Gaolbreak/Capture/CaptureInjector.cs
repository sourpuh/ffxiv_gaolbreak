using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;

namespace Gaolbreak.Capture;

internal sealed unsafe class CaptureInjector : IDisposable
{
    private const int GroupBeaconFront = 0, GroupBgDepth = 1, GroupBg = 2, GroupFg = 3;
    private const int SentinelCount = 4;
    private static readonly int BeaconHash = RestBeaconAddon.InternalName.GetHashCode();

    private readonly Config config;
    private readonly AddonLayer addonLayer;
    private readonly CaptureTarget fg;
    private readonly CaptureTarget bg;

    private bool active;
    private AtkUICommandClipMaskGB* captureSentinels;
    private AtkUICommandEntryGB* entriesBuffer;
    private int entriesCapacity;

    private AtkUICommandEntryGB* prevList;
    private uint prevCount;

    public bool CollectDiagnostics;
    public string Runs = "";

    public CaptureInjector(Config config, AddonLayer addonLayer, CaptureTarget fg, CaptureTarget bg)
    {
        this.config = config;
        this.addonLayer = addonLayer;
        this.fg = fg;
        this.bg = bg;
        captureSentinels = (AtkUICommandClipMaskGB*)NativeMemory.AllocZeroed(SentinelCount * (nuint)sizeof(AtkUICommandClipMaskGB));
    }

    public void Begin(AtkServer* self)
    {
        active = false;
        if (CollectDiagnostics) Runs = "";
        if (!config.Enable || fg.IsNull || bg.IsNull) return;

        var list = self->UICommandListGB;
        var count = self->UICommandCountGB;
        if (list == null || count == 0 || count > self->UICommandPoolSize) return;

        BuildBgList(new Span<AtkUICommandEntryGB>(list, (int)count), out var newList, out var newCount);

        prevList = list;
        prevCount = count;
        self->UICommandListGB = newList;
        self->UICommandCountGB = newCount;
        active = true;
    }

    public void End(AtkServer* self)
    {
        if (!active) return;
        self->UICommandListGB = prevList;
        self->UICommandCountGB = prevCount;
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

    private int GetGroup(ref AtkUICommandEntryGB e)
        => e.IsDepthPriority ? GroupBgDepth : addonLayer.IsBackground(e.AddonHash) ? GroupBg : GroupFg;

    private static string GroupName(int group) => group switch
    {
        GroupBgDepth => "BG+D",
        GroupBg => "BG",
        GroupFg => "FG",
        _ => $"Unk{group}",
    };

    private void BuildBgList(Span<AtkUICommandEntryGB> src, out AtkUICommandEntryGB* newList, out uint newCount)
    {
        captureSentinels[GroupBeaconFront].InitSentinel(bg.NativeTex, true, offset: 0);
        captureSentinels[GroupBgDepth].InitSentinel(bg.NativeTex, true, offset: 1);
        captureSentinels[GroupBg].InitSentinel(bg.NativeTex, false);
        captureSentinels[GroupFg].InitSentinel(fg.NativeTex, false);

        int beaconEnd = 0;
        while (beaconEnd < src.Length && src[beaconEnd].AddonHash == BeaconHash && src[beaconEnd].IsDepthPriority)
            beaconEnd++;
        if (CollectDiagnostics && beaconEnd > 0) Runs += $"\nBEACON x{beaconEnd}";

        var transitions = 0;
        var prevGroup = -1;
        var prevHash = 0;
        for (int i = beaconEnd; i < src.Length; i++)
        {
            int group = GetGroup(ref src[i]);
            bool boundary = group != prevGroup;
            if (boundary) { transitions++; prevGroup = group; }
            AtkUICommandPatcher.MaybePatchAdditiveAlpha(ref src[i]);
            if (CollectDiagnostics)
            {
                var hash = src[i].AddonHash;
                if (boundary) Runs += "\n" + GroupName(group) + " " + AddonName(hash);
                else if (hash != prevHash) Runs += "," + AddonName(hash);
                prevHash = hash;
            }
        }

        int outCount = src.Length + 2 + transitions;
        if (entriesCapacity < outCount)
        {
            if (entriesBuffer != null) NativeMemory.Free(entriesBuffer);
            entriesCapacity = outCount + 100;
            entriesBuffer = (AtkUICommandEntryGB*)NativeMemory.Alloc((nuint)(entriesCapacity * sizeof(AtkUICommandEntryGB)));
        }
        var dst = new Span<AtkUICommandEntryGB>(entriesBuffer, entriesCapacity);

        int j = 0;
        dst[j++].Command = (AtkUICommandGB*)&captureSentinels[GroupBeaconFront];
        for (int i = 0; i < beaconEnd; i++)
            dst[j++] = src[i];
        dst[j++].Command = (AtkUICommandGB*)&captureSentinels[GroupBgDepth];
        prevGroup = GroupBgDepth;
        for (int i = beaconEnd; i < src.Length; i++)
        {
            int group = GetGroup(ref src[i]);
            if (group != prevGroup)
            {
                dst[j++].Command = (AtkUICommandGB*)&captureSentinels[group];
                prevGroup = group;
            }
            dst[j++] = src[i];
        }

        newList = entriesBuffer;
        newCount = (uint)j;
    }

    private string AddonName(int hc) => hc == 0 ? "—" : addonLayer.NameOf(hc) ?? $"?{hc:X8}";
}
