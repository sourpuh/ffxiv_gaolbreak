using Dalamud.Bindings.ImGui;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gaolbreak;

public unsafe partial class CImGui
{
    [LibraryImport("cimgui")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial void igBringWindowToDisplayFront(ImGuiWindow* window);

    [LibraryImport("cimgui")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial void igBringWindowToDisplayBack(ImGuiWindow* window);

    [LibraryImport("cimgui")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial void igBringWindowToDisplayBehind(ImGuiWindow* window, ImGuiWindow* above_window);

    [LibraryImport("cimgui")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial ImGuiWindow* igGetCurrentWindow();

    [LibraryImport("cimgui")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial ImGuiWindow* igFindWindowByName(byte* name);
}
