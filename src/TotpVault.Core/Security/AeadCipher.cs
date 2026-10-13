using System.Security.Cryptography;

namespace TotpVault.Core.Security;

/// <summary>
/// AES-256-GCM による認証付き暗号化。
/// 出力形式: [version(1)] [nonce(12)] [ciphertext(n)] [tag(16)]
/// nonce は暗号化のたびに CSPRNG で 96bit 生成する（同一鍵での使用回数は 2^32 を大きく下回るため衝突は無視できる）。
/// 関連データ (AAD) にレコードIDなどを渡すことで、暗号文の差し替え・流用を検出する。
/// </summary>
public static class AeadCipher
{
    public const int KeySize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    private const byte FormatVersion = 1;
    private const int HeaderSize = 1 + NonceSize;

    public static byte[] Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        if (key.Length != KeySize) throw new ArgumentException("Key must be 256 bits.", nameof(key));

        var output = new byte[HeaderSize + plaintext.Length + TagSize];
        output[0] = FormatVersion;
        var nonce = output.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce,
            plaintext,
            output.AsSpan(HeaderSize, plaintext.Length),
            output.AsSpan(HeaderSize + plaintext.Length, TagSize),
            associatedData);
        return output;
    }

    /// <summary>
    /// 復号する。改ざん・鍵の不一致・AAD の不一致はすべて <see cref="CryptographicException"/> になる。
    /// 戻り値は GC で移動されないピン留め配列。使用後は <see cref="SecureMemory.Zero"/> で消去すること。
    /// </summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> blob, ReadOnlySpan<byte> associatedData)
    {
        if (key.Length != KeySize) throw new ArgumentException("Key must be 256 bits.", nameof(key));
        if (blob.Length < HeaderSize + TagSize || blob[0] != FormatVersion)
            throw new CryptographicException("Invalid ciphertext format.");

        int length = blob.Length - HeaderSize - TagSize;
        var plaintext = SecureMemory.AllocatePinned(length);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(blob.Slice(1, NonceSize),
                blob.Slice(HeaderSize, length),
                blob.Slice(HeaderSize + length, TagSize),
                plaintext,
                associatedData);
            return plaintext;
        }
        catch
        {
            SecureMemory.Zero(plaintext);
            throw;
        }
    }
}
