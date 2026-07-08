using System.Runtime.InteropServices;

namespace Gaolbreak.Capture;

[StructLayout(LayoutKind.Explicit, Size = 16)]
public unsafe struct UICommandEntry
{
    [FieldOffset(0)] public uint SortKey;
    [FieldOffset(4)] public uint Padding; // Unused by the game; Gaolbreak stamps with addon name hashcode.
    [FieldOffset(8)] public UIDrawCommand* Command;
}

[StructLayout(LayoutKind.Explicit)]
public struct UIDrawCommand
{
    [FieldOffset(0x00)] public uint Opcode;
    [FieldOffset(0x30)] private uint sortKey; // Only valid for Geometry opcodes (NOT ClipMask)
    public uint SortKey => this.IsGeometry ? sortKey : 0;
}
