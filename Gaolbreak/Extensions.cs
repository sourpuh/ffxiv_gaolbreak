using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
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
        public unsafe uint Key {
            get => *(uint*)((byte*)Unsafe.AsPointer(ref c) + 8);
            set => *(uint*)((byte*)Unsafe.AsPointer(ref c) + 8) = value;
        }
    }

    extension(RenderCommandSetTarget command)
    {
        public unsafe Texture* RenderTarget0 => command.RenderTargets[0].Value;
    }
}
