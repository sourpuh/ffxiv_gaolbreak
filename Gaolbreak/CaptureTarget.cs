using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using System.Runtime.InteropServices;
using Device = SharpDX.Direct3D11.Device;
using Format = SharpDX.DXGI.Format;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;
using TextureFlags = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFlags;
using TextureFormat = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFormat;

namespace Gaolbreak;


internal sealed unsafe class CaptureTarget(Device device, DeviceContext context) : IDisposable
{
    public Texture* NativeTex;
    public Texture2D? Tex;
    public RenderTargetView? Rtv;
    public ShaderResourceView? Srv;

    public nint Handle => Srv?.NativePointer ?? nint.Zero;
    public uint Width => (uint)(Tex?.Description.Width ?? 0);
    public uint Height => (uint)(Tex?.Description.Height ?? 0);
    public float Aspect => (float)Height / Width;
    public bool IsNull => Tex == null;
    public bool SizeEquals(Texture* sizeRef) => SizeEquals(NativeTex, sizeRef);

    public void Clear()
    {
        if (Rtv != null)
            context.ClearRenderTargetView(Rtv, new RawColor4(0, 0, 0, 0));
    }

    public bool Ensure(Texture* sizeRef)
    {
        if (sizeRef == null || sizeRef->D3D11Texture2D == null) return false;
        if (SizeEquals(NativeTex, sizeRef)) return true;

        Dispose();

        var tex = Texture.CreateTexture2D((int)sizeRef->AllocatedWidth, (int)sizeRef->AllocatedHeight, 1,
            TextureFormat.B8G8R8A8_UNORM, TextureFlags.TextureRenderTarget | TextureFlags.TextureType2D, 0);
        if (tex == null) return false;
        if (tex->D3D11Texture2D == null) { tex->DecRef(); return false; }

        var res = (nint)tex->D3D11Texture2D;
        Marshal.AddRef(res);
        Tex = new Texture2D(res);
        var fmt = ToUNorm(Tex.Description.Format);
        Rtv = new RenderTargetView(device, Tex, new RenderTargetViewDescription
        {
            Format = fmt,
            Dimension = RenderTargetViewDimension.Texture2D,
        });
        Srv = new ShaderResourceView(device, Tex, new ShaderResourceViewDescription
        {
            Format = fmt,
            Dimension = SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,
            Texture2D = new ShaderResourceViewDescription.Texture2DResource { MostDetailedMip = 0, MipLevels = 1 },
        });
        context.ClearRenderTargetView(Rtv, new RawColor4(0, 0, 0, 0));
        NativeTex = tex;
        return true;
    }

    public void Dispose()
    {
        Srv?.Dispose();
        Rtv?.Dispose();
        Tex?.Dispose();
        Srv = null;
        Rtv = null;
        Tex = null;
        if (NativeTex != null) { NativeTex->DecRef(); NativeTex = null; }
    }

    private static bool SizeEquals(Texture* a, Texture* b)
        => a != null && b != null
            && a->AllocatedWidth == b->AllocatedWidth
            && a->AllocatedHeight == b->AllocatedHeight;

    private static Format ToUNorm(Format f) => f switch
    {
        Format.B8G8R8A8_Typeless => Format.B8G8R8A8_UNorm,
        Format.R8G8B8A8_Typeless => Format.R8G8B8A8_UNorm,
        Format.R10G10B10A2_Typeless => Format.R10G10B10A2_UNorm,
        _ => f,
    };
}
