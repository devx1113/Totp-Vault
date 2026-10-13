using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using TotpVault.Core.Security;

namespace TotpVault.App.Services;

/// <summary>
/// PasswordBox.SecurePassword を、マネージド文字列を経由せずに UTF-8 バイト列へ変換する。
/// アンマネージドバッファは使用後すぐにゼロ埋めして解放する。
/// </summary>
public static class SecurePassword
{
    public delegate T CharsFunc<T>(ReadOnlySpan<char> chars);

    public static unsafe T UseChars<T>(SecureString value, CharsFunc<T> func)
    {
        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.SecureStringToGlobalAllocUnicode(value);
            return func(new ReadOnlySpan<char>((void*)ptr, value.Length));
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    /// <summary>戻り値はピン留め配列。使用後に SecureMemory.Zero で消去すること。</summary>
    public static byte[] ToUtf8(SecureString value) => UseChars(value, chars =>
    {
        var bytes = SecureMemory.AllocatePinned(Encoding.UTF8.GetByteCount(chars));
        Encoding.UTF8.GetBytes(chars, bytes);
        return bytes;
    });

    public static bool AreEqual(SecureString a, SecureString b)
    {
        var x = ToUtf8(a);
        var y = ToUtf8(b);
        try
        {
            return CryptographicOperations.FixedTimeEquals(x, y);
        }
        finally
        {
            SecureMemory.Zero(x);
            SecureMemory.Zero(y);
        }
    }

    public static PasswordStrengthLevel Strength(SecureString value)
        => UseChars(value, chars => PasswordStrength.Evaluate(chars));
}
