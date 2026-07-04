using TerraFX.Interop.DirectX;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;
using TextureFlags = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFlags;
using TextureFormat = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFormat;

namespace Gaolbreak;

internal sealed unsafe class CaptureTarget : IDisposable
{
    // The render thread clears+fills the capture texture while the game thread presents it.
    // At low framerates, a single shared texture flickers due to race between presentation and clear/fill.
    // Double buffering gives the render thread and game thread separate textures to avoid the race.
    private const int BufferCount = 2;

    private struct Buffer
    {
        public Texture* Native;
        public ID3D11Texture2D* Tex;
        public ID3D11RenderTargetView* Rtv;
        public ID3D11ShaderResourceView* Srv;
    }

    private readonly ID3D11Device* device;
    private readonly ID3D11DeviceContext* context;

    private readonly Buffer[] bufs = new Buffer[BufferCount];
    private int frameCounter;
    private int renderIndex;
    private int presentIndex;
    private int lastBoundIndex = -1;
    private bool boundThisFrame;

    private uint width;
    private uint height;

    public CaptureTarget(ID3D11Device* device, ID3D11DeviceContext* context)
    {
        this.device = device;
        this.context = context;
    }

    public Texture* NativeTex => bufs[renderIndex].Native;
    public nint PresentHandle
    {
        get
        {
            int p = Volatile.Read(ref presentIndex);
            return p < 0 ? 0 : (nint)bufs[p].Srv;
        }
    }

    public void Invalidate()
    {
        Volatile.Write(ref boundThisFrame, false);
        Volatile.Write(ref lastBoundIndex, -1);
        Volatile.Write(ref presentIndex, -1);
    }
    public uint Width => width;
    public uint Height => height;
    public float Aspect => width == 0 ? 0 : (float)height / width;
    public bool IsNull => bufs[renderIndex].Tex == null;
    public bool SizeEquals(Texture* sizeRef) => SizeEquals(bufs[renderIndex].Native, sizeRef);

    public bool Matches(Texture* t)
    {
        return BufferIndex(t) >= 0;
    }

    private int BufferIndex(Texture* t)
    {
        if (t == null) return -1;
        for (int i = 0; i < BufferCount; i++)
            if (bufs[i].Native == t) return i;
        return -1;
    }

    public bool BeginFrame(Texture* sizeRef)
    {
        if (sizeRef == null || sizeRef->D3D11Texture2D == null) return false;
        if (!SizeEquals(bufs[renderIndex].Native, sizeRef) && !Recreate(sizeRef)) return false;
        if (!Volatile.Read(ref boundThisFrame)) Volatile.Write(ref presentIndex, -1);
        frameCounter++;
        renderIndex = frameCounter % BufferCount;
        Volatile.Write(ref boundThisFrame, false);
        return true;
    }

    public bool MaybeBind(Texture* t)
    {
        var i = BufferIndex(t);
        if (i < 0) return false;
        Volatile.Write(ref boundThisFrame, true);
        if (i == Volatile.Read(ref lastBoundIndex)) return true;
        if (bufs[i].Rtv != null)
        {
            float* black = stackalloc float[] { 0f, 0f, 0f, 0f };
            context->ClearRenderTargetView(bufs[i].Rtv, black);
        }
        Volatile.Write(ref lastBoundIndex, i);
        return true;
    }

    public void EndFrame()
    {
        if (!Volatile.Read(ref boundThisFrame)) return;
        int i = Volatile.Read(ref lastBoundIndex);
        if (i >= 0) Volatile.Write(ref presentIndex, i);
    }

    private bool Recreate(Texture* sizeRef)
    {
        Dispose();
        for (int i = 0; i < BufferCount; i++)
            if (!CreateBuf(ref bufs[i], sizeRef)) { Dispose(); return false; }

        D3D11_TEXTURE2D_DESC td;
        bufs[0].Tex->GetDesc(&td);
        width = td.Width;
        height = td.Height;

        frameCounter = 0;
        renderIndex = 0;
        lastBoundIndex = -1;
        presentIndex = -1;
        return true;
    }

    private bool CreateBuf(ref Buffer buf, Texture* sizeRef)
    {
        var nativeTex = Texture.CreateTexture2D((int)sizeRef->AllocatedWidth, (int)sizeRef->AllocatedHeight, 1,
            TextureFormat.B8G8R8A8_UNORM, TextureFlags.TextureRenderTarget | TextureFlags.TextureType2D, 0);
        if (nativeTex == null) return false;
        if (nativeTex->D3D11Texture2D == null) { nativeTex->DecRef(); return false; }
        buf.Native = nativeTex;

        buf.Tex = (ID3D11Texture2D*)nativeTex->D3D11Texture2D;
        buf.Tex->AddRef();

        D3D11_TEXTURE2D_DESC td;
        buf.Tex->GetDesc(&td);
        var fmt = ToUNorm(td.Format);

        var rtvDesc = new D3D11_RENDER_TARGET_VIEW_DESC
        {
            Format = fmt,
            ViewDimension = D3D11_RTV_DIMENSION.D3D11_RTV_DIMENSION_TEXTURE2D,
        };
        ID3D11RenderTargetView* rtv;
        if (device->CreateRenderTargetView((ID3D11Resource*)buf.Tex, &rtvDesc, &rtv) < 0) return false;
        buf.Rtv = rtv;

        var srvDesc = new D3D11_SHADER_RESOURCE_VIEW_DESC
        {
            Format = fmt,
            ViewDimension = D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURE2D,
        };
        srvDesc.Anonymous.Texture2D.MostDetailedMip = 0;
        srvDesc.Anonymous.Texture2D.MipLevels = 1;
        ID3D11ShaderResourceView* srv;
        if (device->CreateShaderResourceView((ID3D11Resource*)buf.Tex, &srvDesc, &srv) < 0) return false;
        buf.Srv = srv;

        float* black = stackalloc float[] { 0f, 0f, 0f, 0f };
        context->ClearRenderTargetView(rtv, black);
        return true;
    }

    public void Dispose()
    {
        for (int i = 0; i < BufferCount; i++)
        {
            ref var buf = ref bufs[i];
            if (buf.Srv != null) { buf.Srv->Release(); buf.Srv = null; }
            if (buf.Rtv != null) { buf.Rtv->Release(); buf.Rtv = null; }
            if (buf.Tex != null) { buf.Tex->Release(); buf.Tex = null; }
            if (buf.Native != null) { buf.Native->DecRef(); buf.Native = null; }
        }
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
