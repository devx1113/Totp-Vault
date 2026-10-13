using System.Text;
using TotpVault.Core.Otp;

namespace TotpVault.Core.Tests;

/// <summary>RFC 6238 Appendix B / RFC 4226 Appendix D のテストベクターによる検証。</summary>
public class TotpTests
{
    private static readonly byte[] Seed20 = Encoding.ASCII.GetBytes("12345678901234567890");
    private static readonly byte[] Seed32 = Encoding.ASCII.GetBytes("12345678901234567890123456789012");
    private static readonly byte[] Seed64 = Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234");

    [Theory(DisplayName = "RFC 6238 テストベクター (8桁, SHA-1/256/512)")]
    [InlineData(59L, OtpAlgorithm.Sha1, "94287082")]
    [InlineData(59L, OtpAlgorithm.Sha256, "46119246")]
    [InlineData(59L, OtpAlgorithm.Sha512, "90693936")]
    [InlineData(1111111109L, OtpAlgorithm.Sha1, "07081804")]
    [InlineData(1111111109L, OtpAlgorithm.Sha256, "68084774")]
    [InlineData(1111111109L, OtpAlgorithm.Sha512, "25091201")]
    [InlineData(1111111111L, OtpAlgorithm.Sha1, "14050471")]
    [InlineData(1111111111L, OtpAlgorithm.Sha256, "67062674")]
    [InlineData(1111111111L, OtpAlgorithm.Sha512, "99943326")]
    [InlineData(1234567890L, OtpAlgorithm.Sha1, "89005924")]
    [InlineData(1234567890L, OtpAlgorithm.Sha256, "91819424")]
    [InlineData(1234567890L, OtpAlgorithm.Sha512, "93441116")]
    [InlineData(2000000000L, OtpAlgorithm.Sha1, "69279037")]
    [InlineData(2000000000L, OtpAlgorithm.Sha256, "90698825")]
    [InlineData(2000000000L, OtpAlgorithm.Sha512, "38618901")]
    [InlineData(20000000000L, OtpAlgorithm.Sha1, "65353130")]
    [InlineData(20000000000L, OtpAlgorithm.Sha256, "77737706")]
    [InlineData(20000000000L, OtpAlgorithm.Sha512, "47863826")]
    public void Rfc6238Vectors(long time, OtpAlgorithm algorithm, string expected)
    {
        var seed = algorithm switch { OtpAlgorithm.Sha256 => Seed32, OtpAlgorithm.Sha512 => Seed64, _ => Seed20 };
        Assert.Equal(expected, Totp.Compute(seed, time, algorithm, digits: 8, period: 30));
    }

    [Theory(DisplayName = "RFC 6238 テストベクター (6桁)")]
    [InlineData(59L, OtpAlgorithm.Sha1, "287082")]
    [InlineData(1111111109L, OtpAlgorithm.Sha256, "084774")]
    [InlineData(20000000000L, OtpAlgorithm.Sha512, "863826")]
    public void Rfc6238Vectors6Digits(long time, OtpAlgorithm algorithm, string expected)
    {
        var seed = algorithm switch { OtpAlgorithm.Sha256 => Seed32, OtpAlgorithm.Sha512 => Seed64, _ => Seed20 };
        Assert.Equal(expected, Totp.Compute(seed, time, algorithm, digits: 6));
    }

    [Theory(DisplayName = "RFC 4226 HOTP テストベクター (6桁)")]
    [InlineData(0, "755224")]
    [InlineData(1, "287082")]
    [InlineData(2, "359152")]
    [InlineData(3, "969429")]
    [InlineData(4, "338314")]
    [InlineData(5, "254676")]
    [InlineData(6, "287922")]
    [InlineData(7, "162583")]
    [InlineData(8, "399871")]
    [InlineData(9, "520489")]
    public void Rfc4226Vectors(long counter, string expected)
        => Assert.Equal(expected, Totp.ComputeHotp(Seed20, counter, OtpAlgorithm.Sha1, 6));

    [Fact(DisplayName = "更新間隔60秒ではカウンターが60秒ごとに進む")]
    public void CustomPeriod()
    {
        Assert.Equal(Totp.Compute(Seed20, 60, period: 60), Totp.Compute(Seed20, 119, period: 60));
        Assert.NotEqual(Totp.Compute(Seed20, 119, period: 60), Totp.Compute(Seed20, 120, period: 60));
    }

    [Theory(DisplayName = "次の更新までの残り時間")]
    [InlineData(59_000L, 30, 1_000L)]
    [InlineData(60_000L, 30, 30_000L)]
    [InlineData(60_500L, 30, 29_500L)]
    [InlineData(119_999L, 60, 1L)]
    public void MillisecondsRemaining(long nowMs, int period, long expected)
        => Assert.Equal(expected, Totp.GetMillisecondsRemaining(nowMs, period));

    [Fact(DisplayName = "不正な桁数は拒否する")]
    public void RejectsInvalidDigits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Totp.ComputeHotp(Seed20, 0, OtpAlgorithm.Sha1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Totp.ComputeHotp(Seed20, 0, OtpAlgorithm.Sha1, 9));
    }

    [Theory(DisplayName = "コード表示の区切り")]
    [InlineData("123456", "123 456")]
    [InlineData("1234567", "123 4567")]
    [InlineData("12345678", "1234 5678")]
    public void FormatCode(string code, string expected) => Assert.Equal(expected, Totp.FormatCode(code));
}

public class Base32Tests
{
    [Theory(DisplayName = "RFC 4648 テストベクター")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Rfc4648Vectors(string plain, string encoded)
    {
        Assert.Equal(encoded, Base32.Encode(Encoding.ASCII.GetBytes(plain)));
        Assert.True(Base32.TryDecode(encoded, out var decoded));
        Assert.Equal(plain, Encoding.ASCII.GetString(decoded));
    }

    [Theory(DisplayName = "入力の揺れ（小文字・空白・ハイフン・パディング・全角）を許容")]
    [InlineData("mzxw6ytboi")]
    [InlineData("MZXW 6YTB OI")]
    [InlineData("MZXW-6YTB-OI")]
    [InlineData("MZXW6YTBOI======")]
    [InlineData("ＭＺＸＷ６ＹＴＢＯＩ")]
    [InlineData(" mzxw\t6ytb\noi ")]
    public void TolerantDecoding(string input)
    {
        Assert.True(Base32.TryDecode(input, out var decoded));
        Assert.Equal("foobar", Encoding.ASCII.GetString(decoded));
    }

    [Theory(DisplayName = "不正な Base32 は拒否")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MZXW1")]      // '1' は Base32 に含まれない
    [InlineData("MZXW8YTB")]   // '8' も同様
    [InlineData("MZ=XW6")]     // パディング後にデータ
    [InlineData("MZXW6!")]
    public void RejectsInvalid(string input) => Assert.False(Base32.TryDecode(input, out _));
}

public class OtpAuthParserTests
{
    [Fact(DisplayName = "標準的な otpauth URI を解析")]
    public void ParsesStandardUri()
    {
        var e = OtpAuthParser.ParseTotp("otpauth://totp/Example:alice@google.com?secret=JBSWY3DPEHPK3PXP&issuer=Example");
        Assert.Equal("Example", e.Issuer);
        Assert.Equal("alice@google.com", e.AccountName);
        Assert.Equal(Convert.FromHexString("48656C6C6F21DEADBEEF"), e.Secret);
        Assert.Equal(OtpAlgorithm.Sha1, e.Algorithm);
        Assert.Equal(6, e.Digits);
        Assert.Equal(30, e.Period);
    }

    [Fact(DisplayName = "URLエンコード・アルゴリズム・桁数・更新間隔を解析")]
    public void ParsesAllParameters()
    {
        var e = OtpAuthParser.ParseTotp(
            "otpauth://totp/ACME%20Co:john.doe@email.com?secret=HXDMVJECJJWSRB3HWIZR4IFUGFTMXBOZ&issuer=ACME%20Co&algorithm=SHA256&digits=8&period=60");
        Assert.Equal("ACME Co", e.Issuer);
        Assert.Equal("john.doe@email.com", e.AccountName);
        Assert.Equal(OtpAlgorithm.Sha256, e.Algorithm);
        Assert.Equal(8, e.Digits);
        Assert.Equal(60, e.Period);
    }

    [Fact(DisplayName = "日本語のサービス名（UTF-8エンコード）")]
    public void ParsesJapaneseIssuer()
    {
        var uri = "otpauth://totp/" + Uri.EscapeDataString("サンプル銀行:山田") + "?secret=JBSWY3DPEHPK3PXP&algorithm=sha512";
        var e = OtpAuthParser.ParseTotp(uri);
        Assert.Equal("サンプル銀行", e.Issuer);
        Assert.Equal("山田", e.AccountName);
        Assert.Equal(OtpAlgorithm.Sha512, e.Algorithm);
    }

    [Fact(DisplayName = "issuer パラメータはラベルより優先")]
    public void IssuerParameterWins()
    {
        var e = OtpAuthParser.ParseTotp("otpauth://totp/Old:bob?secret=JBSWY3DPEHPK3PXP&issuer=New");
        Assert.Equal("New", e.Issuer);
        Assert.Equal("bob", e.AccountName);
    }

    [Fact(DisplayName = "サービス名なしのラベル")]
    public void LabelWithoutIssuer()
    {
        var e = OtpAuthParser.ParseTotp("otpauth://totp/john?secret=JBSWY3DPEHPK3PXP");
        Assert.Equal("", e.Issuer);
        Assert.Equal("john", e.AccountName);
    }

    [Theory(DisplayName = "不正な QR コード内容はエラー")]
    [InlineData("https://example.com/")]
    [InlineData("otpauth://hotp/Example:alice?secret=JBSWY3DPEHPK3PXP&counter=1")]
    [InlineData("otpauth://totp/Example:alice?issuer=Example")]
    [InlineData("otpauth://totp/Example:alice?secret=NOT-BASE32-189!")]
    [InlineData("otpauth://totp/Example:alice?secret=JBSWY3DP")]
    [InlineData("otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&digits=5")]
    [InlineData("otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&digits=10")]
    [InlineData("otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&digits=abc")]
    [InlineData("otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&algorithm=MD5")]
    [InlineData("otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&period=0")]
    [InlineData("otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&period=-30")]
    [InlineData("otpauth://unknown/x?secret=JBSWY3DPEHPK3PXP")]
    public void RejectsInvalid(string uri)
        => Assert.Throws<OtpAuthFormatException>(() => OtpAuthParser.Parse(uri));

    [Fact(DisplayName = "HOTP は分かりやすいメッセージで拒否")]
    public void HotpMessage()
    {
        var ex = Assert.Throws<OtpAuthFormatException>(() => OtpAuthParser.Parse("otpauth://hotp/x?secret=JBSWY3DPEHPK3PXP"));
        Assert.Contains("HOTP", ex.Message);
    }

    [Fact(DisplayName = "Google Authenticator 移行QR (otpauth-migration) を解析")]
    public void ParsesGoogleMigration()
    {
        var secretA = Convert.FromHexString("48656C6C6F21DEADBEEF");
        var secretB = Encoding.ASCII.GetBytes("12345678901234567890");
        var payload = Proto.Message(
            Proto.Bytes(1, Proto.Message(
                Proto.Bytes(1, secretA), Proto.String(2, "Example:alice@example.com"), Proto.String(3, "Example"),
                Proto.Varint(4, 1), Proto.Varint(5, 1), Proto.Varint(6, 2))),
            Proto.Bytes(1, Proto.Message(
                Proto.Bytes(1, secretB), Proto.String(2, "GitHub:bob"),
                Proto.Varint(4, 2), Proto.Varint(5, 2), Proto.Varint(6, 2))),
            Proto.Bytes(1, Proto.Message(   // HOTP → スキップ
                Proto.Bytes(1, secretB), Proto.String(2, "Legacy"), Proto.Varint(6, 1), Proto.Varint(7, 5))),
            Proto.Varint(2, 1), Proto.Varint(3, 1), Proto.Varint(4, 0), Proto.Varint(5, 12345));

        var uri = "otpauth-migration://offline?data=" + Uri.EscapeDataString(Convert.ToBase64String(payload));
        Assert.True(OtpAuthParser.LooksLikeOtpAuth(uri));

        var result = OtpAuthParser.Parse(uri);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(1, result.SkippedUnsupported);

        var a = result.Entries[0];
        Assert.Equal("Example", a.Issuer);
        Assert.Equal("alice@example.com", a.AccountName);
        Assert.Equal(secretA, a.Secret);
        Assert.Equal(OtpAlgorithm.Sha1, a.Algorithm);
        Assert.Equal(6, a.Digits);

        var b = result.Entries[1];
        Assert.Equal("GitHub", b.Issuer);
        Assert.Equal("bob", b.AccountName);
        Assert.Equal(OtpAlgorithm.Sha256, b.Algorithm);
        Assert.Equal(8, b.Digits);
        Assert.Equal(30, b.Period);
    }

    [Theory(DisplayName = "壊れた移行データはエラー")]
    [InlineData("otpauth-migration://offline")]
    [InlineData("otpauth-migration://offline?data=%%%")]
    [InlineData("otpauth-migration://offline?data=CgQKAg")] // 途中で切れたデータ
    public void RejectsBrokenMigration(string uri)
        => Assert.Throws<OtpAuthFormatException>(() => OtpAuthParser.Parse(uri));

    /// <summary>テスト用の最小 protobuf エンコーダー。</summary>
    private static class Proto
    {
        public static byte[] Message(params byte[][] fields) => fields.SelectMany(f => f).ToArray();
        public static byte[] Varint(int field, long value) => [.. VarintBytes(((long)field << 3) | 0), .. VarintBytes(value)];
        public static byte[] String(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
        public static byte[] Bytes(int field, byte[] value)
            => [.. VarintBytes(((long)field << 3) | 2), .. VarintBytes(value.Length), .. value];

        private static byte[] VarintBytes(long value)
        {
            var list = new List<byte>();
            var v = (ulong)value;
            do
            {
                byte b = (byte)(v & 0x7F);
                v >>= 7;
                if (v != 0) b |= 0x80;
                list.Add(b);
            } while (v != 0);
            return list.ToArray();
        }
    }
}
