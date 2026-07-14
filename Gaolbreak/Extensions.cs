using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Gaolbreak.Capture;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Gaolbreak;

internal static class Extensions
{
    private const int UICommandPoolSizeOffset = 0x550;
    private const int UICommandListOffset = 0x580;
    private const int UICommandCountOffset = 0x588;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ToUint(this Vector4 color)
    {
        return ImGui.ColorConvertFloat4ToU32(color);
    }

    extension(ImGuiWindowPtr imGuiWindowPtr)
    {
        public unsafe string GetName()
        {
            return Encoding.UTF8.GetString(imGuiWindowPtr.Name, imGuiWindowPtr.NameBufLen - 1);
        }
    }

    extension(ref Context c)
    {
        public unsafe uint SortKeyGB
        {
            get => *(uint*)((byte*)Unsafe.AsPointer(ref c) + 8);
            set => *(uint*)((byte*)Unsafe.AsPointer(ref c) + 8) = value;
        }
    }

    extension(ref Texture t)
    {
        public bool IsFullSize
            => t.AllocatedWidth == t.ActualWidth
            && t.AllocatedHeight == t.ActualHeight;

        public unsafe bool AllocatedSizeEquals(Texture* t2)
            => t.AllocatedWidth == t2->AllocatedWidth
            && t.AllocatedHeight == t2->AllocatedHeight;
    }

    extension(ref AtkServer s)
    {
        public unsafe AtkUICommandEntryGB* UICommandListGB
        {
            get => *(AtkUICommandEntryGB**)((byte*)Unsafe.AsPointer(ref s) + UICommandListOffset);
            set => *(AtkUICommandEntryGB**)((byte*)Unsafe.AsPointer(ref s) + UICommandListOffset) = value;
        }
        public unsafe uint UICommandCountGB
        {
            get => *(uint*)((byte*)Unsafe.AsPointer(ref s) + UICommandCountOffset);
            set => *(uint*)((byte*)Unsafe.AsPointer(ref s) + UICommandCountOffset) = value;
        }

        public unsafe uint UICommandPoolSize
            => *(uint*)((byte*)Unsafe.AsPointer(ref s) + UICommandPoolSizeOffset) / (uint)sizeof(AtkUICommandEntryGB);
    }

    extension(ref AtkUICommandEntryGB e)
    {
        // The entry's +0x04 padding is unused by the game; Gaolbreak stamps it with the addon name hashcode.
        public unsafe int AddonHash
        {
            get => *((int*)Unsafe.AsPointer(ref e) + 1);
            set => *((int*)Unsafe.AsPointer(ref e) + 1) = value;
        }

        public unsafe bool IsDepthPriority
        {
            get
            {
                var cmd = e.Command;
                return cmd != null && cmd->IsDepthPriority;
            }
        }
    }

    extension(ref AtkUICommandGB c)
    {
        public bool IsDraw => (uint)c.Type > (uint)AtkUICommandTypeGB.ClipRect;
        public unsafe bool IsDepthPriority
            => c.IsDraw && (*(uint*)((byte*)Unsafe.AsPointer(ref c) + 0x30) >> 8 & 0xF) == 1;
    }

    private const uint ClipMaskFlagBegin = 1;
    private const uint ClipMaskFlagDepth = 2;
    extension (ref AtkUICommandClipMaskGB cmd)
    {
        // Init a sentinel that redirects to a capture target via Capturer.ClipMaskDetour.
        public unsafe void InitSentinel(Texture* target, bool depth, int offset = 0)
        {
            cmd.Type = AtkUICommandTypeGB.ClipMask;
            cmd.Format = AtkUICommandFormatGB.ClipMask;
            cmd.Flags = ClipMaskFlagBegin | (depth ? ClipMaskFlagDepth : 0);
            cmd.MaskTexture = target;
            cmd.Transform = Matrix4x4.Identity;
            // Offset is only used for depth capture.
            cmd.Transform.M12 = offset;
        }
    }
}
