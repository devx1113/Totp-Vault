using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace TotpVault.Core.Otp;

/// <summary>
/// RFC 4226 (HOTP) / RFC 6238 (TOTP) によるワンタイムパスワード生成。
/// </summary>
public static class Totp
{
    public const int DefaultDigits = 6;
    public const int DefaultPeriod = 30;
    public const int MinDigits = 6;
    public const int MaxDigits = 8;
    public const int MinPeriod = 10;
    public const int MaxPeriod = 300;
    /// <summary>80bit 未満のシークレットは入力ミスとみなして拒否する。</summary>
    public const int MinSecretBytes = 10;

    private static readonly int[] Pow10 = [1, 10, 100, 1_000, 10_000, 100_000, 1_000_000, 10_000_000, 100_000_000];

    public static string ComputeHotp(ReadOnlySpan<byte> key, long counter, OtpAlgorithm algorithm, int digits)
    {
        if (digits is < MinDigits or > MaxDigits) throw new ArgumentOutOfRangeException(nameof(digits));
        if (key.IsEmpty) throw new ArgumentException("Key must not be empty.", nameof(key));

        Span<byte> message = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(message, counter);

        Span<byte> hash = stackalloc byte[64];
        int length = algorithm switch
        {
            OtpAlgorithm.Sha1 => HMACSHA1.HashData(key, message, hash),
            OtpAlgorithm.Sha256 => HMACSHA256.HashData(key, message, hash),
            OtpAlgorithm.Sha512 => HMACSHA512.HashData(key, message, hash),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };

        // Dynamic truncation (RFC 4226 5.3)
        int offset = hash[length - 1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24)
                   | (hash[offset + 1] << 16)
                   | (hash[offset + 2] << 8)
                   | hash[offset + 3];
        CryptographicOperations.ZeroMemory(hash);

        int otp = binary % Pow10[digits];
        return otp.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    public static string Compute(ReadOnlySpan<byte> key, long unixTimeSeconds,
        OtpAlgorithm algorithm = OtpAlgorithm.Sha1, int digits = DefaultDigits, int period = DefaultPeriod)
        => ComputeHotp(key, GetCounter(unixTimeSeconds, period), algorithm, digits);

    public static long GetCounter(long unixTimeSeconds, int period)
    {
        if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        return unixTimeSeconds / period;
    }

    /// <summary>現在のコードが切り替わるまでの残りミリ秒。</summary>
    public static long GetMillisecondsRemaining(long unixTimeMilliseconds, int period)
    {
        long periodMs = period * 1000L;
        return periodMs - (unixTimeMilliseconds % periodMs);
    }

    public static bool IsValidDigits(int digits) => digits is >= MinDigits and <= MaxDigits;

    public static bool IsValidPeriod(int period) => period is >= MinPeriod and <= MaxPeriod;

    /// <summary>"123456" → "123 456"、"12345678" → "1234 5678"。</summary>
    public static string FormatCode(string code) => code.Length switch
    {
        6 => string.Concat(code.AsSpan(0, 3), " ", code.AsSpan(3)),
        7 => string.Concat(code.AsSpan(0, 3), " ", code.AsSpan(3)),
        8 => string.Concat(code.AsSpan(0, 4), " ", code.AsSpan(4)),
        _ => code,
    };
}
