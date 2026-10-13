using TotpVault.Core.Otp;
using TotpVault.Core.Qr;
using ZXing;
using ZXing.QrCode;

namespace TotpVault.Core.Tests;

public class QrDecoderTests
{
    private const string Uri1 = "otpauth://totp/Example:alice@example.com?secret=JBSWY3DPEHPK3PXP&issuer=Example";
    private const string Uri2 = "otpauth://totp/GitHub:bob?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&issuer=GitHub&algorithm=SHA256&digits=8";

    [Fact(DisplayName = "QRコード画像から otpauth URI を読み取り、登録情報に変換")]
    public void DecodesRenderedQr()
    {
        var (pixels, w, h) = Render([Uri1], scale: 4);
        var texts = QrDecoder.DecodeBgra32(pixels, w, h);
        Assert.Equal(Uri1, Assert.Single(texts));

        var entry = OtpAuthParser.ParseTotp(texts[0]);
        Assert.Equal("Example", entry.Issuer);
        Assert.Equal("alice@example.com", entry.AccountName);
    }

    [Fact(DisplayName = "白黒反転（ダークモード）の QR コードも読み取れる")]
    public void DecodesInvertedQr()
    {
        var (pixels, w, h) = Render([Uri1], scale: 4, inverted: true);
        Assert.Equal(Uri1, Assert.Single(QrDecoder.DecodeBgra32(pixels, w, h)));
    }

    [Fact(DisplayName = "画面キャプチャ内の複数の QR コードを読み取れる")]
    public void DecodesMultipleQr()
    {
        var (pixels, w, h) = Render([Uri1, Uri2], scale: 4);
        var texts = QrDecoder.DecodeBgra32(pixels, w, h);
        Assert.Contains(Uri1, texts);
        Assert.Contains(Uri2, texts);
    }

    [Fact(DisplayName = "画面キャプチャ（アルファ値が全て 0）の画像も読み取れる")]
    public void DecodesCaptureWithZeroAlpha()
    {
        var (pixels, w, h) = Render([Uri1], scale: 4);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 0; // GDI の BitBlt 結果を再現
        Assert.Equal(Uri1, Assert.Single(QrDecoder.DecodeBgra32(pixels, w, h)));
    }

    [Fact(DisplayName = "画面キャプチャ（アルファ値が不定）は ignoreAlpha で読み取れる")]
    public void DecodesCaptureWithGarbageAlpha()
    {
        var (pixels, w, h) = Render([Uri1], scale: 4);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = (byte)((i / 4) % 7 == 0 ? 255 : 0);
        Assert.Equal(Uri1, Assert.Single(QrDecoder.DecodeBgra32(pixels, w, h, ignoreAlpha: true)));
    }

    [Fact(DisplayName = "背景透過の QR 画像は白背景に合成して読み取れる")]
    public void DecodesTransparentBackground()
    {
        var (pixels, w, h) = Render([Uri1], scale: 4);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] == 0xFF) { pixels[i] = pixels[i + 1] = pixels[i + 2] = 0; pixels[i + 3] = 0; } // 透明な黒
        }
        Assert.Equal(Uri1, Assert.Single(QrDecoder.DecodeBgra32(pixels, w, h)));
    }

    [Fact(DisplayName = "QRコードのない画像は空の結果")]
    public void BlankImage()
    {
        var pixels = Enumerable.Repeat((byte)0xFF, 200 * 200 * 4).ToArray();
        Assert.Empty(QrDecoder.DecodeBgra32(pixels, 200, 200));
    }

    /// <summary>QR コードを横に並べた BGRA32 画像を生成する。</summary>
    private static (byte[] Pixels, int Width, int Height) Render(string[] contents, int scale, bool inverted = false)
    {
        var writer = new QRCodeWriter();
        var matrices = contents.Select(c => writer.encode(c, BarcodeFormat.QR_CODE, 0, 0)).ToList();
        const int margin = 40;
        int width = matrices.Sum(m => m.Width * scale + margin) + margin;
        int height = matrices.Max(m => m.Height * scale) + margin * 2;
        var pixels = new byte[width * height * 4];

        byte light = inverted ? (byte)0x20 : (byte)0xFF;
        byte dark = inverted ? (byte)0xF0 : (byte)0x00;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = light;
            pixels[i + 3] = 0xFF;
        }

        int ox = margin;
        foreach (var m in matrices)
        {
            for (int y = 0; y < m.Height * scale; y++)
            for (int x = 0; x < m.Width * scale; x++)
            {
                if (!m[x / scale, y / scale]) continue;
                int p = ((y + margin) * width + ox + x) * 4;
                pixels[p] = pixels[p + 1] = pixels[p + 2] = dark;
            }
            ox += m.Width * scale + margin;
        }
        return (pixels, width, height);
    }
}
