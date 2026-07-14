using TerraFX.Interop.DirectX;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;
using TextureFlags = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFlags;
using TextureFormat = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFormat;

namespace Gaolbreak.Capture;

internal sealed unsafe class GameTexture : IDisposable
{
    public Texture* Tex { get; private set; }
    public ID3D11ShaderResourceView* Srv { get; private set; }
    public ID3D11RenderTargetView* Rtv { get; private set; }

    public bool IsValid => Tex != null;

    public uint Width { get; private set; }
    public uint Height { get; private set; }

    public bool Create(uint width, uint height, TextureFormat format)
    {
        var tex = Texture.CreateTexture2D((int)width, (int)height, 1,
            format, TextureFlags.TextureRenderTarget | TextureFlags.TextureType2D, 0);
        if (tex == null) return false;

        if (tex->D3D11Texture2D == null || tex->D3D11ShaderResourceView == null || tex->MipRenderTargets == null)
        {
            tex->DecRef();
            return false;
        }
        var rtv = (ID3D11RenderTargetView*)tex->GetMipRenderTarget(0, 0)->D3D11RenderTargetViewOrDepthStencilView;
        if (rtv == null)
        {
            tex->DecRef();
            return false;
        }

        Tex = tex;
        Srv = (ID3D11ShaderResourceView*)tex->D3D11ShaderResourceView;
        Rtv = rtv;
        Width = width;
        Height = height;
        return true;
    }

    public void Clear(ID3D11DeviceContext* context)
    {
        if (Rtv == null) return;
        float* black = stackalloc float[] { 0f, 0f, 0f, 0f };
        context->ClearRenderTargetView(Rtv, black);
    }

    public bool SizeEquals(Texture* other)
        => Tex != null && other != null
            && Tex->AllocatedWidth == other->AllocatedWidth
            && Tex->AllocatedHeight == other->AllocatedHeight;

    public void Dispose()
    {
        Srv = null;
        Rtv = null;
        Width = 0;
        Height = 0;
        if (Tex != null)
        {
            Tex->DecRef();
            Tex = null;
        }
    }
}
