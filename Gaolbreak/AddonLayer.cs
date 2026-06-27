using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Gaolbreak;

internal unsafe class AddonLayer(Config config, IGameGui gameGui, ICondition condition)
{
    private const uint MaxBackgroundDepthLayerDefault = 3;
    private const uint MaxBackgroundDepthLayerCharacterCreator = 1;
    // Pop-up Addons whose show events should be ignored.
    private static readonly HashSet<string> ShowIgnoreAddons =
        [
            "Tooltip",
            "ActionDetail",
            "ItemDetail",
            "LovmActionDetail",
            "MiragePrismPrismItemDetail",
            "XBMItemDetail",
            "XBMPetActionDetail",
            "XBMBattleMonsterDetail",
        ];
    private static readonly string[] ContextAddons =
        ["ContextMenu", "ContextIconMenu", "AddonContextMenuTitle"];

    public readonly HashSet<string> LiftedAddons = [];
    public event Action? OnForegroundAddonShown;

    private uint MaxBackgroundDepthLayer => condition[ConditionFlag.CreatingCharacter]
        ? MaxBackgroundDepthLayerCharacterCreator
        : MaxBackgroundDepthLayerDefault;

    public void OnAddonPostShow(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null) return;
        HideCapturedFillBackdrops(&addon->UldManager);
        if (addon->DepthLayer > MaxBackgroundDepthLayer && !ShowIgnoreAddons.Contains(addon->NameString))
            OnForegroundAddonShown?.Invoke();
    }

    // The BRD job gauge backdrop is a Fill image node with no texture.
    // It normally acts as an invisible alpha-only draw, but stamps a black rectangle in the capture so it needs hiding.
    private static void HideCapturedFillBackdrops(AtkUldManager* uld)
    {
        if (uld == null) return;
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null) continue;
            if (node->Type == NodeType.Image && node->NodeFlags.HasFlag(NodeFlags.Fill)
                && HasNullTexturePart(node->GetAsAtkImageNode()))
                node->ToggleVisibility(false);
            var component = node->GetComponent();
            if (component != null)
                HideCapturedFillBackdrops(&component->UldManager);
        }
    }

    private static bool HasNullTexturePart(AtkImageNode* image)
    {
        if (image == null || image->PartsList == null) return false;
        for (var i = 0; i < image->PartsList->PartCount; i++)
            if (image->PartsList->Parts[i].UldAsset->AtkTexture.KernelTexture == null)
                return true;
        return false;
    }

    public void OnWindowLmbDown(string windowName)
    {
        LiftedAddons.Clear();
        foreach (var name in ContextAddons)
        {
            var addon = (AtkUnitBase*)gameGui.GetAddonByName(name).Address;
            if (addon != null && addon->IsVisible)
                addon->Close(fireCallback: true);
        }
    }

    public void OnAddonLmbDown(string addonName)
    {
        if (!config.IsAddonLiftable(addonName)) return;
        LiftedAddons.Add(addonName);
        foreach (var member in LinkedGroupMembers(addonName))
            LiftedAddons.Add(member);
    }

    private List<string> LinkedGroupMembers(string addonName)
    {
        var members = new List<string>();
        if (!addonName.StartsWith("ChatLog", StringComparison.Ordinal))
            return members;
        var chatLog = (AddonChatLog*)gameGui.GetAddonByName("ChatLog").Address;
        if (chatLog == null) return members;
        members.Add("ChatLog");
        foreach (var child in chatLog->AddonControl.ChildAddons)
        {
            var info = child.Value;
            if (info != null && info->AtkUnitBase != null)
                members.Add(info->AtkUnitBase->NameString);
        }
        return members;
    }

    public bool IsBackground(AtkUnitBase* addon)
    {
        if (addon == null) return false;
        var name = addon->NameString;
        if (ShowIgnoreAddons.Contains(name)) return false;
        return config.IsAddonLiftable(name)
            ? !LiftedAddons.Contains(name)
            : addon->DepthLayer <= MaxBackgroundDepthLayer;
    }

    private int currentHash;
    private readonly Stack<int> hashStack = new();
    private readonly Dictionary<int, bool> backgroundByHash = new();
    private readonly Dictionary<int, string> nameByHash = new();

    public int CurrentHash => currentHash;
    public bool IsBackground(int hash) => backgroundByHash.GetValueOrDefault(hash);
    public string? NameOf(int hash) => nameByHash.GetValueOrDefault(hash);

    public void ResetHashes()
    {
        hashStack.Clear();
        currentHash = 0;
    }

    public void OnAddonPreDraw(AddonEvent type, AddonArgs args)
    {
        hashStack.Push(currentHash);
        currentHash = 0;
        if (!config.Enable) return;
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null) return;
        var hash = args.AddonName.GetHashCode();
        backgroundByHash[hash] = IsBackground(addon);
        nameByHash[hash] = args.AddonName;
        currentHash = hash;
    }

    public void OnAddonPostDraw(AddonEvent type, AddonArgs args)
        => currentHash = hashStack.Count > 0 ? hashStack.Pop() : 0;
}
