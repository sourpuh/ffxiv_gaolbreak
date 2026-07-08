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

    extension(IDictionary<uint, string> d)
    {
        public string DebugString()
        {
            return string.Join(", ", d.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }
    }

    extension(ref Context c)
    {
        public unsafe byte SubViewLayer
        {
            get => *((byte*)Unsafe.AsPointer(ref c) + 11);
            set => *((byte*)Unsafe.AsPointer(ref c) + 11) = value;
        }

        public unsafe uint SortKey
        {
            get => *(uint*)((byte*)Unsafe.AsPointer(ref c) + 8);
            set => *(uint*)((byte*)Unsafe.AsPointer(ref c) + 8) = value;
        }
    }

    extension(RenderCommandSetTarget command)
    {
        public unsafe Texture* RenderTarget0 => command.RenderTargets[0].Value;
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
        public unsafe UICommandEntry* UICommandListPtr
        {
            get => *(UICommandEntry**)((byte*)Unsafe.AsPointer(ref s) + UICommandListOffset);
            set => *(UICommandEntry**)((byte*)Unsafe.AsPointer(ref s) + UICommandListOffset) = value;
        }
        public unsafe uint UICommandCnt
        {
            get => *(uint*)((byte*)Unsafe.AsPointer(ref s) + UICommandCountOffset);
            set => *(uint*)((byte*)Unsafe.AsPointer(ref s) + UICommandCountOffset) = value;
        }
        public unsafe Span<UICommandEntry> UICommandListSpan => new Span<UICommandEntry>(s.UICommandListPtr, (int)s.UICommandCnt);
        public unsafe uint UICommandPoolSize
            => *(uint*)((byte*)Unsafe.AsPointer(ref s) + UICommandPoolSizeOffset) / (uint)sizeof(UICommandEntry);
    }

    extension(ref UICommandEntry e)
    {
        public int AddonHash
        {
            get => (int) e.Padding;
            set => e.Padding = (uint) value;
        }

        public unsafe bool IsDepthPriority
        {
            get
            {
                var cmd = e.Command;
                return cmd != null && cmd->IsGeometry && cmd->IsDepthPriority;
            }
        }
    }

    extension(ref UIDrawCommand c)
    {
        public bool IsGeometry => (c.Opcode & 0xF0) != 0;
        public bool IsDepthPriority => (c.SortKey >> 8 & 0xF) == 1;
    }
}
