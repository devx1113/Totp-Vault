using System.Security.Cryptography;

namespace TotpVault.Core.Security;

/// <summary>
/// 機密データ用バッファの補助。
/// .NET のマネージドメモリでは全コピーの消去を保証できないため、
/// 「GC に移動されないピン留め配列を使い、使用後すぐにゼロ埋めする」ことでリスクを低減する。
/// </summary>
public static class SecureMemory
{
    public static byte[] AllocatePinned(int length) => GC.AllocateArray<byte>(length, pinned: true);

    public static void Zero(byte[]? buffer)
    {
        if (buffer is not null) CryptographicOperations.ZeroMemory(buffer);
    }

    public static void Zero(Span<byte> buffer) => CryptographicOperations.ZeroMemory(buffer);
}
