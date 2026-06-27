using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using TerraFX.Interop.DirectX;

namespace Gaolbreak.Capture;

internal sealed unsafe class Renderer : IDisposable
{
    private readonly ID3D11DeviceContext* context;
    private readonly ID3D11BlendState* premultBlend;
    private readonly ID3D11BlendState* straightBlend;
    private readonly ImDrawCallback premultBlendCallback;
    private readonly ImDrawCallback straightBlendCallback;

    public Renderer(ID3D11Device* device, ID3D11DeviceContext* context)
    {
        this.context = context;
        premultBlend = CreateBlend(device, D3D11_BLEND.D3D11_BLEND_ONE);
        straightBlend = CreateBlend(device, D3D11_BLEND.D3D11_BLEND_SRC_ALPHA);
        premultBlendCallback = (list, cmd) => { try { SetBlend(premultBlend); } catch { } };
        straightBlendCallback = (list, cmd) => { try { SetBlend(straightBlend); } catch { } };
    }

    public void DrawTextureToDrawlist(ImDrawListPtr drawList, nint textureHandle)
    {
        if (textureHandle == 0) return;
        var pos = ImGuiHelpers.MainViewport.Pos;
        var size = ImGuiHelpers.MainViewport.Size;

        drawList.AddCallback(premultBlendCallback, null);
        drawList.AddImage((ImTextureID)(ulong)textureHandle, pos, pos + size);
        drawList.AddCallback(straightBlendCallback, null);
    }

    private void SetBlend(ID3D11BlendState* blend)
    {
        float* factor = stackalloc float[] { 0f, 0f, 0f, 0f };
        context->OMSetBlendState(blend, factor, 0xFFFFFFFF);
    }

    private static ID3D11BlendState* CreateBlend(ID3D11Device* device, D3D11_BLEND srcColorBlend)
    {
        var desc = new D3D11_BLEND_DESC();
        desc.RenderTarget[0] = new D3D11_RENDER_TARGET_BLEND_DESC
        {
            BlendEnable = true,
            SrcBlend = srcColorBlend,
            DestBlend = D3D11_BLEND.D3D11_BLEND_INV_SRC_ALPHA,
            BlendOp = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
            SrcBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE,
            DestBlendAlpha = D3D11_BLEND.D3D11_BLEND_INV_SRC_ALPHA,
            BlendOpAlpha = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
            RenderTargetWriteMask = (byte)D3D11_COLOR_WRITE_ENABLE.D3D11_COLOR_WRITE_ENABLE_ALL,
        };
        ID3D11BlendState* blend;
        device->CreateBlendState(&desc, &blend);
        return blend;
    }

    public void Dispose()
    {
        if (premultBlend != null) premultBlend->Release();
        if (straightBlend != null) straightBlend->Release();
    }
}
