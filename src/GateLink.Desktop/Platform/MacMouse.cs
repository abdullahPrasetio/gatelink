using System.Runtime.InteropServices;

namespace GateLink.Desktop;

/// <summary>
/// Reads the current global mouse X coordinate on macOS via CoreGraphics. Used
/// to anchor the popover under the menu-bar icon that was just clicked. This is
/// a single plain C-function call (no Objective-C class registration), so it is
/// low risk. On non-macOS it reports no value.
/// </summary>
internal static class MacMouse
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint { public double X; public double Y; }

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(CoreGraphics)]
    private static extern CGPoint CGEventGetLocation(IntPtr @event);

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "CFRelease")]
    private static extern void CFRelease(IntPtr cf);

    public static bool TryGetX(out int x)
    {
        x = 0;
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            var ev = CGEventCreate(IntPtr.Zero);
            if (ev == IntPtr.Zero) return false;
            try
            {
                var p = CGEventGetLocation(ev);
                x = (int)p.X;
                return true;
            }
            finally { CFRelease(ev); }
        }
        catch
        {
            return false;
        }
    }
}
