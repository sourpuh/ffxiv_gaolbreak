using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Gaolbreak;

internal unsafe class DepthManager(IGameGui gameGui)
{
    private const float BackgroundDepth = 0.75f;
    private const float LayerDepthStep = 0.001f;
    private const uint MaxBackgroundDepthLayer = 3;
    private const int NamePlateUseDepthPriority = 0x8;

    // Addons allowed to move between the BG and the FG.
    // A Liftable addon sits in the background until the user clicks on it.
    public static readonly HashSet<string> LiftableAddons = [
        "_ActionCross",
        "_ActionDoubleCrossL",
        "_ActionDoubleCrossR",
        "_BagWidget",
        "_ToDoList",
        "_PartyList",
        "_EnemyList",
        "_DTR",
        "_ParameterWidget",
        "_MainCommand",
        "_Money",
        "ScenarioTree",
    ];

    public static readonly HashSet<string> LiftableAddonPrefixes = [
        "_ActionBar",
        "_Status",
        "_AllianceList",
        "_TargetInfo",
        "JobHud",
    ];

    // Liftable addons currently lifted to the FG.
    public readonly HashSet<string> LiftableForeground = [];
    // Addons the game rewrites node flags as it shows/animates entries clearing the depth-priority flag set on PostShow.
    public static readonly HashSet<string> ContinuousReapplyAddons = ["_FlyText"];
    // Game already uses depth priority for these so don't touch their depth.
    private static readonly HashSet<string> NativeDepthOrderAddons = ["NamePlate"];
    // Pop-up Addons whose show events should be ignored.
    private static readonly HashSet<string> ShowIgnoreAddons = ["Tooltip", "ActionDetail", "ItemDetail"];

    private readonly HashSet<string> PendingReapply = [];
    private bool FullApplyPending = true;

    public event Action? OnForegroundAddonShown;

    public void OnAddonPostShow(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null) return;
        Invalidate(addon->NameString);
        if (addon->DepthLayer > MaxBackgroundDepthLayer && !ShowIgnoreAddons.Contains(addon->NameString))
            OnForegroundAddonShown?.Invoke();
    }

    public void OnContinuousReapplyAddonPreDraw(AddonEvent type, AddonArgs args)
    {
        if (!Plugin.Enable) return;
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null) return;
        Apply(addon, addon->NameString);
    }

    public void OnNamePlateRequestedUpdate(AddonEvent type, AddonArgs args)
    {
        if (!Plugin.Enable) return;

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null) return;

        var array = NamePlateNumberArray.Instance();
        if (array == null) return;

        float depth = LayeredBackgroundDepth(addon);
        var objects = array->ObjectData;
        int count = Math.Clamp(array->ActiveNamePlateCount, 0, objects.Length);
        for (int i = 0; i < count; i++)
        {
            if ((objects[i].DrawFlags & NamePlateUseDepthPriority) == 0)
                objects[i].Depth = depth;
            objects[i].DrawFlags |= NamePlateUseDepthPriority;
        }
    }

    public bool IsLiftable(string name)
    {
        if (LiftableAddons.Contains(name)) return true;
        foreach (var prefix in LiftableAddonPrefixes)
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
        return false;
    }

    public void Invalidate(string name) => PendingReapply.Add(name);

    public void InvalidateAll()
    {
        FullApplyPending = true;
        PendingReapply.Clear();
    }

    public void Update()
    {
        if (FullApplyPending)
        {
            ApplyAll();
            FullApplyPending = false;
            PendingReapply.Clear();
            return;
        }

        if (PendingReapply.Count == 0) return;
        foreach (var name in PendingReapply)
            Apply(name);
        PendingReapply.Clear();
    }

    public void OnWindowLmbDown(string windowName)
    {
        if (LiftableForeground.Count > 0)
        {
            foreach (var addonName in LiftableForeground)
                Invalidate(addonName);
            LiftableForeground.Clear();
        }
    }

    public void OnAddonLmbDown(string addonName)
    {
        if (IsLiftable(addonName) && LiftableForeground.Add(addonName))
            Invalidate(addonName);
    }

    public void RestoreAll()
    {
        PendingReapply.Clear();
        LiftableForeground.Clear();

        var manager = (AtkUnitManager*)RaptureAtkUnitManager.Instance();
        if (manager == null) return;

        var list = &manager->AllLoadedUnitsList;
        for (var i = 0; i < list->Count; i++)
        {
            var addon = list->Entries[i].Value;
            if (addon == null) continue;
            ApplyDepthToAddon(addon, useDepth: false, BackgroundDepth);
        }
    }

    public List<string> GetForegroundAddonNames()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var manager = (AtkUnitManager*)RaptureAtkUnitManager.Instance();
        if (manager != null)
        {
            var list = &manager->AllLoadedUnitsList;
            for (var i = 0; i < list->Count; i++)
            {
                var addon = list->Entries[i].Value;
                if (addon == null || addon->DepthLayer < MaxBackgroundDepthLayer) continue;
                var name = addon->NameString;
                if (!IsLiftable(name)) names.Add(name);
            }
        }
        return [.. names];
    }

    private static float LayeredBackgroundDepth(AtkUnitBase* addon) =>
        BackgroundDepth + LayerDepthStep * addon->DepthLayer;

    private void Apply(string name) =>
        Apply((AtkUnitBase*)gameGui.GetAddonByName(name).Address, name);

    private void Apply(AtkUnitBase* addon, string name)
    {
        if (addon == null || NativeDepthOrderAddons.Contains(name)) return;

        bool useDepth = IsLiftable(name)
            ? !LiftableForeground.Contains(name)
            : addon->DepthLayer <= MaxBackgroundDepthLayer;

        ApplyDepthToAddon(addon, useDepth, LayeredBackgroundDepth(addon));
    }

    private void ApplyAll()
    {
        var manager = (AtkUnitManager*)RaptureAtkUnitManager.Instance();
        if (manager == null) return;

        var list = &manager->AllLoadedUnitsList;
        for (var i = 0; i < list->Count; i++)
        {
            var addon = list->Entries[i].Value;
            if (addon != null)
                Apply(addon, addon->NameString);
        }
    }

    public void ApplyDepthToAddon(AtkUnitBase* addon, bool useDepth, float depth)
    {
        if (addon == null) return;
        if (NativeDepthOrderAddons.Contains(addon->NameString))
        {
            addon->RootNode->SetUseDepthBasedPriority(useDepth);
            return;
        }
        ApplyDepthToUld(&addon->UldManager, useDepth, depth, addon->RootNode);
    }

    private void ApplyDepthToUld(AtkUldManager* uld, bool useDepth, float depth, AtkResNode* root)
    {
        if (uld == null) return;
        for (var i = 0; i < uld->NodeListCount; i++)
            ApplyDepthToNode(uld->NodeList[i], useDepth, depth, root);
    }

    private void ApplyDepthToNode(AtkResNode* node, bool useDepth, float depth, AtkResNode* root)
    {
        if (node == null) return;

        if (useDepth && node->Type == NodeType.Image)
        {
            var image = node->GetAsAtkImageNode();

            // Fix for the BRD job gauge black background that only shows with depth priority.
            // Hopefully doesn't affect anything else.
            bool hasNull = false;
            bool hasFill = image->NodeFlags.HasFlag(NodeFlags.Fill);
            if (image->PartsList != null && image->PartsList->PartCount > 0)
            {
                for (int i = 0; i < image->PartsList->PartCount; i++)
                {
                    if (image->PartsList->Parts[i].UldAsset->AtkTexture.KernelTexture == null)
                    {
                        hasNull = true;
                    }
                }
            }
            if (hasFill && hasNull)
            {
                node->ToggleVisibility(false);
            }
        }

        node->SetUseDepthBasedPriority(useDepth);
        if (useDepth)
            node->Depth = node == root ? depth : 0f;

        var component = node->GetComponent();
        if (component != null)
            ApplyDepthToUld(&component->UldManager, useDepth, depth, root);
    }
}
