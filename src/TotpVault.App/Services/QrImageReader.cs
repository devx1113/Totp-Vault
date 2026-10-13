using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TotpVault.Core.Qr;

namespace TotpVault.App.Services;

public static class QrImageReader
{
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    public static bool IsImageFile(string path)
        => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static BitmapSource LoadFile(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    /// <summary>クリップボードの画像（スクリーンショット等）を取得する。PNG 形式を優先する。</summary>
    public static BitmapSource? GetClipboardImage()
    {
        try
        {
            if (Clipboard.GetData("PNG") is MemoryStream png)
            {
                var decoder = new PngBitmapDecoder(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return frame;
            }
        }
        catch
        {
            // PNG が壊れている場合は標準形式で再試行
        }

        if (!Clipboard.ContainsImage()) return null;
        var image = Clipboard.GetImage();
        image?.Freeze();
        return image;
    }

    /// <summary>UIスレッドでピクセルを取り出し、デコード（重い処理）はバックグラウンドで行う。</summary>
    /// <param name="isScreenCapture">画面キャプチャの場合 true（アルファ値を無視する）。</param>
    public static Task<IReadOnlyList<string>> DecodeAsync(BitmapSource source, bool isScreenCapture = false)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int width = bgra.PixelWidth, height = bgra.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        return Task.Run(() =>
        {
            try
            {
                return QrDecoder.DecodeBgra32(pixels, width, height, ignoreAlpha: isScreenCapture);
            }
            finally
            {
                Array.Clear(pixels); // 画面キャプチャ等の画像データを残さない
            }
        });
    }
}

public static class ScreenCapture
{
    /// <summary>全モニター（仮想スクリーン）をキャプチャする。</summary>
    public static BitmapSource CaptureVirtualScreen()
    {
        int x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("画面サイズを取得できませんでした。");

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        IntPtr bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
        IntPtr previous = NativeMethods.SelectObject(memoryDc, bitmap);
        try
        {
            if (!NativeMethods.BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                throw new InvalidOperationException("画面をキャプチャできませんでした。");

            NativeMethods.SelectObject(memoryDc, previous);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
