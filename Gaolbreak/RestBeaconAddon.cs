using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;

namespace Gaolbreak;

// A depth layer 0 addon that does nothing in game.
// This addon is a beacon for ReShade REST to detect and determine that UI drawing is complete.
// Without the beacon, Gaolbreak capture hides ReShade changes.
// Ported from KamiToolKit's NativeAddon & stripped to essentials.
internal sealed unsafe class RestBeaconAddon : IDisposable
{
    public const string InternalName = "GaolbreakRestBeacon";

    private AtkUnitBase* addon;

    private static bool Available => RaptureAtkUnitManager.Addresses.InitializeAddon.Value != 0;

    public RestBeaconAddon()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, InternalName, OnPreFinalize);
    }

    private void OnPreFinalize(AddonEvent type, AddonArgs args) => addon = null;

    // Called every framework tick: reopens after the game tears the addon down.
    public void Open()
    {
        if (addon != null || !Available) return;
        var stage = AtkStage.Instance();
        if (stage == null || stage->RaptureAtkUnitManager == null) return;

        addon = IMemorySpace.GetUISpace()->Create<AtkUnitBase>();
        if (addon == null) return;

        var localRef = addon;
        using var name = new Utf8String(InternalName);
        stage->RaptureAtkUnitManager->InitializeAddon(&localRef, name.StringPtr, 0, null);
        if (localRef == null)
        {
            Plugin.Log.Error("RestBeaconAddon initialization failed");
            addon = null;
            return;
        }

        addon->UldManager.InitializeResourceRendererManager();
        addon->UldManager.ResourceFlags |= AtkUldManagerResourceFlag.Initialized;
        BuildNodes(addon);

        addon->Open(depthLayer: 0);
    }

    public void Dispose()
    {
        if (addon != null) addon->Close(false);
        Plugin.AddonLifecycle.UnregisterListener(OnPreFinalize);
    }

    private static void BuildNodes(AtkUnitBase* self)
    {
        var widget = UiAlloc<AtkUldWidgetInfo>(1, 16);
        self->UldManager.Objects = (AtkUldObjectInfo*)widget;
        self->UldManager.ObjectCount = 1;
        self->UldManager.ResourceFlags |= AtkUldManagerResourceFlag.ArraysAllocated;

        var textNode = IMemorySpace.GetUISpace()->Create<AtkTextNode>();
        var text = (AtkResNode*)textNode;
        text->Type = NodeType.Text;
        text->NodeFlags = NodeFlags.Visible | NodeFlags.Enabled | NodeFlags.UseDepthBasedPriority;
        text->Depth = -1f; // -1 is never visible and beyond the far plane so it's always drawn first.
        textNode->SetText("Gaolbreak REST beacon"u8);
        self->RootNode = text;

        var nodeList = UiAlloc<Pointer<AtkResNode>>(1);
        nodeList[0] = text;
        widget->NodeList = (AtkResNode**)nodeList;
        widget->NodeCount = 1;

        self->UldManager.UpdateDrawNodeList();
        self->UldManager.LoadedState = AtkLoadState.Loaded;
        self->LoadState = AtkUnitBaseLoadState.FullyLoaded;
        self->WasLoadUldByNameCalled = true;
    }

    private static T* UiAlloc<T>(uint count = 1, ulong alignment = 8) where T : unmanaged
    {
        var size = (ulong)sizeof(T) * count;
        var memory = (T*)IMemorySpace.GetUISpace()->Malloc(size, alignment);
        if (memory != null) IMemorySpace.Memset(memory, 0, size);
        return memory;
    }
}
