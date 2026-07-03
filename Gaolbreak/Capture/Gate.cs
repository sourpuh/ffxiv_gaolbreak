using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Gaolbreak.Capture;

internal static unsafe class Gate
{
    public static string? BlockedReason()
    {
        try
        {
            //if (!Plugin.ClientState.IsLoggedIn) return "Not logged in";
            //if (Plugin.ObjectTable.LocalPlayer == null) return "No local player";
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

    public static bool IsZoning()
        => Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];

    public static bool IsInCutscene()
        => !Plugin.ClientState.IsGPosing && Plugin.Condition[ConditionFlag.WatchingCutscene] || Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Plugin.Condition[ConditionFlag.WatchingCutscene78];

    public static bool IsFaded()
    {
        // There's a brief period when loading the character creator that the game destroys and recreates all addons while faded.
        // The fade addons are also deleted yet the screen stays faded, so there seems to be another fade mechanism I'm missing.
        var fadeMiddle = (AtkUnitBase*)Plugin.GameGui.GetAddonByName("FadeMiddle").Address;
        var fadeBlack = (AtkUnitBase*)Plugin.GameGui.GetAddonByName("FadeBlack").Address;
        return (fadeMiddle != null && fadeMiddle->IsVisible)
            || (fadeBlack != null && fadeBlack->IsVisible);
    }

    public static bool IsTitleScreen()
    {
        return IsAddonVisible("Title") || IsAddonVisible("CharaSelect");
    }

    private static bool IsAddonVisible(string addonName)
    {
        var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(addonName).Address;
        return addon != null && addon->IsVisible;
    }
}
