using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Orcus.App;

internal static class WindowChrome
{
    // Styles this window only; never changes Windows personalization settings.
    public static void Apply(Window window)
    {
        if (RuntimeSafety.Simulation) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int enabled = 1;
        DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        var bg = ((SolidColorBrush)window.FindResource("Bg")).Color;
        var fg = ((SolidColorBrush)window.FindResource("Ink")).Color;
        int background = bg.R | (bg.G << 8) | (bg.B << 16);
        int foreground = fg.R | (fg.G << 8) | (fg.B << 16);
        DwmSetWindowAttribute(handle, 35, ref background, sizeof(int));
        DwmSetWindowAttribute(handle, 36, ref foreground, sizeof(int));
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
