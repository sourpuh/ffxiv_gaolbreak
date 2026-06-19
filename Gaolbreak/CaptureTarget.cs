using TerraFX.Interop.DirectX;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;
using TextureFlags = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFlags;
using TextureFormat = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFormat;

namespace Gaolbreak;

internal sealed unsafe class CaptureTarget : IDisposable
{
    private readonly ID3D11Device* device;
    private readonly ID3D11DeviceContext* context;

    public Texture* NativeTex;
    private ID3D11Texture2D* tex;
    private ID3D11RenderTargetView* rtv;
    private ID3D11ShaderResourceView* srv;
    private uint width;
    private uint height;
    private bool hasCleared;

    public CaptureTarget(ID3D11Device* device, ID3D11DeviceContext* context)
    {
        this.device = device;
        this.context = context;
    }

    public nint Handle => (nint)srv;
    public uint Width => width;
    public uint Height => height;
    public float Aspect => width == 0 ? 0 : (float)height / width;
    public bool IsNull => tex == null;
    public bool SizeEquals(Texture* sizeRef) => SizeEquals(NativeTex, sizeRef);

    public void Clear()
    {
        if (!hasCleared && rtv != null)
        {
            float* black = stackalloc float[] { 0f, 0f, 0f, 0f };
            context->ClearRenderTargetView(rtv, black);
            hasCleared = true;
        }
    }

    public bool BeginFrame(Texture* sizeRef)
    {
        hasCleared = false;
        if (sizeRef == null || sizeRef->D3D11Texture2D == null) return false;
        if (SizeEquals(NativeTex, sizeRef)) return true;

        Dispose();

        {
            var nativeTex = Texture.CreateTexture2D((int)sizeRef->AllocatedWidth, (int)sizeRef->AllocatedHeight, 1,
                TextureFormat.B8G8R8A8_UNORM, TextureFlags.TextureRenderTarget | TextureFlags.TextureType2D, 0);
            if (nativeTex == null) return false;
            if (nativeTex->D3D11Texture2D == null) { nativeTex->DecRef(); return false; }
            NativeTex = nativeTex;
        }

        tex = (ID3D11Texture2D*)NativeTex->D3D11Texture2D;
        tex->AddRef();

        D3D11_TEXTURE2D_DESC td;
        tex->GetDesc(&td);
        width = td.Width;
        height = td.Height;
        var fmt = ToUNorm(td.Format);

        {
            var rtvDesc = new D3D11_RENDER_TARGET_VIEW_DESC
            {
                Format = fmt,
                ViewDimension = D3D11_RTV_DIMENSION.D3D11_RTV_DIMENSION_TEXTURE2D,
            };

            ID3D11RenderTargetView* rtv;
            if (device->CreateRenderTargetView((ID3D11Resource*)tex, &rtvDesc, &rtv) < 0)
            {
                Dispose();
                return false;
            }
            this.rtv = rtv;
        }

        {
            var srvDesc = new D3D11_SHADER_RESOURCE_VIEW_DESC
            {
                Format = fmt,
                ViewDimension = D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURE2D,
            };
            srvDesc.Anonymous.Texture2D.MostDetailedMip = 0;
            srvDesc.Anonymous.Texture2D.MipLevels = 1;
            ID3D11ShaderResourceView* srv;
            if (device->CreateShaderResourceView((ID3D11Resource*)tex, &srvDesc, &srv) < 0)
            {
                Dispose();
                return false;
            }
            this.srv = srv;
        }

        Clear();
        return true;
    }

    public void Dispose()
    {
        if (srv != null) { srv->Release(); srv = null; }
        if (rtv != null) { rtv->Release(); rtv = null; }
        if (tex != null) { tex->Release(); tex = null; }
        if (NativeTex != null) { NativeTex->DecRef(); NativeTex = null; }
        width = 0;
        height = 0;
    }

    private static bool SizeEquals(Texture* a, Texture* b)
        => a != null && b != null
            && a->AllocatedWidth == b->AllocatedWidth
            && a->AllocatedHeight == b->AllocatedHeight;

    private static DXGI_FORMAT ToUNorm(DXGI_FORMAT f) => f switch
    {
        DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_TYPELESS => DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
        DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_TYPELESS => DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
        DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_TYPELESS => DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM,
        _ => f,
    };
}
