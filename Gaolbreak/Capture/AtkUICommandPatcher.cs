using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Component.GUI;
using TerraFX.Interop.DirectX;

namespace Gaolbreak.Capture;

internal static unsafe class AtkUICommandPatcher
{
    private const byte OpAdd = (byte)D3D11_BLEND_OP.D3D11_BLEND_OP_ADD;
    private const byte OpMin = (byte)D3D11_BLEND_OP.D3D11_BLEND_OP_MIN;
    private const byte OpMax = (byte)D3D11_BLEND_OP.D3D11_BLEND_OP_MAX;
    private const byte BlendZero = (byte)D3D11_BLEND.D3D11_BLEND_ZERO;
    private const byte BlendOne = (byte)D3D11_BLEND.D3D11_BLEND_ONE;
    private const byte WriteMaskRgb = 0b1110;

    private static bool NeedsAlphaPreserved(PackedBlendStateDesc* blend)
    {
        if (!blend->BlendEnable) return false;
        if (blend->BlendOp is OpMin or OpMax) return true;
        return blend->BlendOp == OpAdd && blend->DestBlend == BlendOne;
    }

    internal static void MaybePatchAdditiveAlpha(ref AtkUICommandEntry e)
    {
        var cmd = e.Command;
        if (cmd == null || !cmd->IsDraw) return;
        var blend = &((AtkUICommandDraw*)cmd)->BlendState;
        if ((blend->RenderTargetWriteMask & WriteMaskRgb) == 0)
        {
            blend->RenderTargetWriteMask = 0;
            return;
        }
        if (!NeedsAlphaPreserved(blend)) return;
        blend->BlendOpAlpha = OpAdd;
        blend->SrcBlendAlpha = BlendZero;
        blend->DestBlendAlpha = BlendOne;
    }
}
