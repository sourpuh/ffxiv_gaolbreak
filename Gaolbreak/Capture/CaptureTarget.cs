using TerraFX.Interop.DirectX;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;
using TextureFormat = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFormat;

namespace Gaolbreak.Capture;

internal sealed unsafe class CaptureTarget : IDisposable
{
    // The render thread clears+fills the capture texture while the game thread presents it.
    // At low framerates, a single shared texture flickers due to race between presentation and clear/fill.
    // Double buffering gives the render thread and game thread separate textures to avoid the race.
    private const int BufferCount = 2;

    private readonly ID3D11DeviceContext* context;

    private readonly GameTexture[] bufs = new GameTexture[BufferCount];
    private int frameCounter;
    private int renderIndex;
    private int presentIndex;
    private int lastBoundIndex = -1;
    private bool boundThisFrame;

    private uint width;
    private uint height;

    public CaptureTarget(ID3D11DeviceContext* context)
    {
        this.context = context;
        for (int i = 0; i < BufferCount; i++) bufs[i] = new GameTexture();
    }

    public Texture* NativeTex => bufs[renderIndex].Tex;
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
    public bool IsNull => !bufs[renderIndex].IsValid;

    public bool Matches(Texture* t) => BufferIndex(t) >= 0;

    private int BufferIndex(Texture* t)
    {
        if (t == null) return -1;
        for (int i = 0; i < BufferCount; i++)
            if (bufs[i].Tex == t) return i;
        return -1;
    }

    public bool BeginFrame(Texture* sizeRef)
    {
        if (sizeRef == null || sizeRef->D3D11Texture2D == null) return false;
        if (!bufs[renderIndex].SizeEquals(sizeRef) && !Recreate(sizeRef)) return false;
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
        bufs[i].Clear(context);
        Volatile.Write(ref lastBoundIndex, i);
        return true;
    }

    public void EndFrame(ToneAdjustPass.Scope pass)
    {
        if (!Volatile.Read(ref boundThisFrame)) return;
        int i = Volatile.Read(ref lastBoundIndex);
        if (i < 0) return;
        pass.Apply(bufs[i]);
        Volatile.Write(ref presentIndex, i);
    }

    private bool Recreate(Texture* sizeRef)
    {
        ReleaseBuffers();
        for (int i = 0; i < BufferCount; i++)
        {
            if (!bufs[i].Create(sizeRef->AllocatedWidth, sizeRef->AllocatedHeight, TextureFormat.B8G8R8A8_UNORM))
            {
                ReleaseBuffers();
                return false;
            }
            bufs[i].Clear(context);
        }

        width = bufs[0].Width;
        height = bufs[0].Height;

        frameCounter = 0;
        renderIndex = 0;
        lastBoundIndex = -1;
        presentIndex = -1;
        return true;
    }

    public void Dispose() => ReleaseBuffers();

    private void ReleaseBuffers()
    {
        for (int i = 0; i < BufferCount; i++) bufs[i].Dispose();
        width = 0;
        height = 0;
    }
}
