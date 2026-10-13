using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TotpVault.Core.Security;

/// <summary>
/// マスターパスワード → 鍵暗号化鍵 (KEK) の導出パラメータ。
/// PBKDF2-HMAC-SHA256（.NET 標準実装、OWASP 推奨の 600,000 回）を使用する。
/// アルゴリズム名を保存しているため、将来 Argon2id 等へ移行してもデータ互換性を保てる。
/// </summary>
public sealed class KdfParameters
{
    public const string Pbkdf2Sha256 = "pbkdf2-sha256";
    public const int DefaultIterations = 600_000;
    public const int MinIterations = 1_000;          // テスト用の下限
    public const int MaxIterations = 10_000_000;     // 細工されたファイルによる長時間ハングを防止
    public const int SaltSize = 32;

    public string Algorithm { get; }
    public int Iterations { get; }
    public byte[] Salt { get; }

    public KdfParameters(string algorithm, int iterations, byte[] salt)
    {
        if (algorithm != Pbkdf2Sha256) throw new NotSupportedException($"Unsupported KDF: {algorithm}");
        if (iterations is < MinIterations or > MaxIterations) throw new ArgumentOutOfRangeException(nameof(iterations));
        if (salt.Length < 16) throw new ArgumentException("Salt too short.", nameof(salt));
        Algorithm = algorithm;
        Iterations = iterations;
        Salt = salt;
    }

    public static KdfParameters CreateNew(int iterations = DefaultIterations)
        => new(Pbkdf2Sha256, iterations, RandomNumberGenerator.GetBytes(SaltSize));

    /// <summary>256bit 鍵を導出する。戻り値はピン留め配列。使用後に消去すること。</summary>
    public byte[] DeriveKey(ReadOnlySpan<byte> passwordUtf8)
    {
        var key = SecureMemory.AllocatePinned(AeadCipher.KeySize);
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(passwordUtf8, Salt, key, Iterations, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            // OS の PBKDF2 API が使えない環境（一部の互換レイヤー等）では、同一結果を返す標準実装で代替する
            Pbkdf2HmacSha256(passwordUtf8, Salt, Iterations, key);
        }
        return key;
    }

    /// <summary>RFC 8018 PBKDF2-HMAC-SHA256。OS 実装と同一の出力（テストで照合済み）。</summary>
    internal static void Pbkdf2HmacSha256(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, int iterations, Span<byte> destination)
    {
        const int hashSize = 32;
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, password);
        Span<byte> u = stackalloc byte[hashSize];
        Span<byte> t = stackalloc byte[hashSize];
        Span<byte> blockIndex = stackalloc byte[4];
        try
        {
            for (int block = 1, offset = 0; offset < destination.Length; block++, offset += hashSize)
            {
                BinaryPrimitives.WriteInt32BigEndian(blockIndex, block);
                hmac.AppendData(salt);
                hmac.AppendData(blockIndex);
                hmac.GetHashAndReset(u);
                u.CopyTo(t);
                for (int i = 1; i < iterations; i++)
                {
                    hmac.AppendData(u);
                    hmac.GetHashAndReset(u);
                    for (int k = 0; k < hashSize; k++) t[k] ^= u[k];
                }
                t[..Math.Min(hashSize, destination.Length - offset)].CopyTo(destination[offset..]);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(u);
            CryptographicOperations.ZeroMemory(t);
        }
    }

    /// <summary>パラメータ自体を AAD として暗号文に結び付けるための正規化表現。</summary>
    public byte[] ToAssociatedData(string context)
        => Encoding.UTF8.GetBytes(string.Join('|', context, Algorithm,
            Iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(Salt)));
}
