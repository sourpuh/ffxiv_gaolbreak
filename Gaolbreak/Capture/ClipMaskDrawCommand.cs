using System.Numerics;
using System.Runtime.InteropServices;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;

namespace Gaolbreak.Capture;

[StructLayout(LayoutKind.Explicit, Size = 128)]
public unsafe struct ClipMaskDrawCommand
{
    private const uint ClipMaskOpcode = 1;
    private const ushort VariantBeginEnd = 3;
    private const uint KeyBegin = 1;
    private const uint KeyDepth = 2;

    [FieldOffset(0)] public UIDrawCommand Header;
    [FieldOffset(4)] public ushort Variant;
    [FieldOffset(8)] public uint Key;
    [FieldOffset(16)] public Texture* Target;
    [FieldOffset(32)] public Matrix4x4 Transform;

    // Init a sentinel that redirects to a capture target via Capturer.ClipMaskDetour.
    public void Initialize(Texture* target, bool depth, bool inheritSeq = false)
    {
        Header.Opcode = ClipMaskOpcode;
        Variant = VariantBeginEnd;
        Key = KeyBegin | (depth ? KeyDepth : 0);
        Target = target;
        Transform = Matrix4x4.Identity;
        // Passes the inheritSeq flag to Capturer.ClipMaskDetour.
        if (inheritSeq) Transform.M12 = 1f;
    }
}
