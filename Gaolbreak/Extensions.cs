using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Gaolbreak;

internal static class Extensions
{
    private const int UICommandPoolSizeOffset = 0x550;

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
        public unsafe uint UICommandPoolSize
            => *(uint*)((byte*)Unsafe.AsPointer(ref s) + UICommandPoolSizeOffset) / (uint)sizeof(AtkUICommandEntry);
    }

    extension(ref AtkUICommandEntry e)
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

    extension(ref AtkUICommand c)
    {
        public bool IsDraw => (uint)c.Type > (uint)AtkUICommandType.ClipRect;
        public unsafe bool IsDepthPriority
            => c.IsDraw && (*(uint*)((byte*)Unsafe.AsPointer(ref c) + 0x30) >> 8 & 0xF) == 1;
    }

    private const uint ClipMaskFlagBegin = 1;
    private const uint ClipMaskFlagDepth = 2;
    extension (ref AtkUICommandClipMask cmd)
    {
        // Init a sentinel that redirects to a capture target via Capturer.ClipMaskDetour.
        public unsafe void InitSentinel(Texture* target, bool depth, int offset = 0)
        {
            cmd.Type = AtkUICommandType.ClipMask;
            cmd.Format = AtkUICommandFormat.ClipMask;
            cmd.Flags = ClipMaskFlagBegin | (depth ? ClipMaskFlagDepth : 0);
            cmd.MaskTexture = target;
            cmd.Transform = Matrix4x4.Identity;
            // Offset is only used for depth capture.
            cmd.Transform.M12 = offset;
        }
    }
}
