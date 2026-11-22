using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ADControlCenter.App.Views;

internal static class WindowChrome
{
    internal static bool IsDark { get; set; } = true;
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    internal static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int enabled = IsDark ? 1 : 0;
        const int immersiveDarkMode = 20;
        DwmSetWindowAttribute(handle, immersiveDarkMode, ref enabled, sizeof(int));
    }
}
