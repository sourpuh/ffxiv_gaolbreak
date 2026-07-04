using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace Gaolbreak.Capture;

internal static unsafe class Gate
{
    public static string? BlockedReason()
    {
        try
        {
            if (Plugin.GameGui.GameUiHidden) return "UI hidden";
            if (IsInCutscene()) return "Cutscene";
            if (IsFaded()) return "Faded";
            if (IsTransition()) return "Transition";
            return null;
        }
        catch (Exception e)
        {
            return e.GetType().Name;
        }
    }

    public static bool IsInCutscene()
        => !Plugin.ClientState.IsGPosing && Plugin.Condition[ConditionFlag.WatchingCutscene] || Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Plugin.Condition[ConditionFlag.WatchingCutscene78];

    public static bool IsTransition() => IsUiFading() && !IsTitleScreen();

    private static bool IsUiFading()
    {
        var raum = RaptureAtkUnitManager.Instance();
        return raum != null && raum->IsUiFading;
    }

    public static bool IsFaded()
    {
        var framework = Framework.Instance();
        if (framework == null) return false;
        var env = framework->EnvironmentManager;
        if (env == null) return false;
        return env->FadeActive || env->FadeColor.W - env->FadeCurrent > 0.01f;
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
