using ZXing;
using ZXing.Common;

namespace TotpVault.Core.Qr;

/// <summary>画像のピクセルデータから QR コードを読み取る（ZXing.Net, Apache-2.0）。</summary>
public static class QrDecoder
{
    /// <summary>
    /// BGRA32（1ピクセル4バイト、行間の余白なし）の画像から QR コードをすべて読み取る。
    /// 画面キャプチャのように複数の QR コードが写っている場合にも対応する。
    /// </summary>
    /// <param name="ignoreAlpha">
    /// true: アルファ値を無視して不透明として扱う（GDI の画面キャプチャはアルファが 0 や不定値になるため）。
    /// false: 透過部分は白背景に合成する（背景透過の QR 画像用）。ただしアルファが全て 0 の画像は不透明とみなす。
    /// </param>
    public static IReadOnlyList<string> DecodeBgra32(byte[] pixels, int width, int height, bool ignoreAlpha = false)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || pixels.Length < (long)width * height * 4)
            throw new ArgumentException("Invalid image dimensions.");

        int length = width * height * 4;
        if (ignoreAlpha || IsAlphaAllZero(pixels, length))
        {
            for (int i = 3; i < length; i += 4) pixels[i] = 0xFF;
        }

        var source = new RGBLuminanceSource(pixels, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true, // ダークモード（白黒反転）の QR にも対応
                PossibleFormats = [BarcodeFormat.QR_CODE],
            },
        };

        var results = reader.DecodeMultiple(source);
        if (results is null || results.Length == 0)
        {
            var single = reader.Decode(source);
            results = single is null ? [] : [single];
        }

        return results
            .Select(r => r.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsAlphaAllZero(byte[] pixels, int length)
    {
        for (int i = 3; i < length; i += 4)
        {
            if (pixels[i] != 0) return false;
        }
        return true;
    }
}
