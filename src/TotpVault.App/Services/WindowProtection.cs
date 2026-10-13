using System.Windows;
using System.Windows.Interop;

namespace TotpVault.App.Services;

/// <summary>
/// 画面キャプチャ・画面共有からウィンドウを除外する (SetWindowDisplayAffinity)。
/// Windows 10 2004 以降は WDA_EXCLUDEFROMCAPTURE、それ以前は WDA_MONITOR（黒く表示）にフォールバック。
/// </summary>
public static class WindowProtection
{
    public static bool Enabled { get; private set; }

    public static void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        foreach (Window w in Application.Current.Windows) Apply(w);
    }

    public static void Attach(Window window)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Apply(window);
        else window.SourceInitialized += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!Enabled)
        {
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_NONE);
            return;
        }
        if (!NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE))
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_MONITOR);
    }
}
