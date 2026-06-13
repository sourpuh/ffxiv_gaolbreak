using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Gaolbreak;

// The results from RaptureAtkUnitManager.GetAddonCollision are poisoned
// by Dalamud Window's "InhibitAtkCollision" feature.
// This class bypasses the poison Dalamud hook by calling the base
// AtkUnitManager.GetAddonCollision function which gives accurate results.
internal static unsafe class AddonCollisionResolver
{
    public static AtkUnitBase* GetAddonCollision(short x, short y)
    {
        var raum = RaptureAtkUnitManager.Instance();
        if (raum == null) return null;
        AddonCollision info = default;
        var getAddonCollision = AtkUnitManager.StaticVirtualTablePointer->GetAddonCollision;
        getAddonCollision((AtkUnitManager*)raum, &info, x, y);
        return info.UnitBase;
    }
}
