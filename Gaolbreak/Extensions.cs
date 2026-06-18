using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Gaolbreak;

internal static class Extensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ToUint(this Vector4 color)
    {
        return ImGui.ColorConvertFloat4ToU32(color);
    }

    extension(ImGuiWindowPtr imGuiWindowPtr)
    {
        public unsafe string GetName()
        {
            return Encoding.UTF8.GetString(imGuiWindowPtr.Name, imGuiWindowPtr.NameBufLen);
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
        public unsafe byte SubViewLayer {
            get => *((byte*)Unsafe.AsPointer(ref c) + 11);
            set => *((byte*)Unsafe.AsPointer(ref c) + 11) = value;
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
}
