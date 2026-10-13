namespace TotpVault.Core.Security;

/// <summary>
/// ローカル保管庫の鍵情報を、端末・OSユーザーに結び付けて追加保護する層。
/// Windows では DPAPI (CurrentUser) 実装を使用する。
/// これにより保管庫ファイルだけを別PCへ持ち出しても、マスターパスワードの総当たりすら開始できない。
/// 別PCへの移行はマスターパスワード方式のみで暗号化された「バックアップファイル」で行う。
/// </summary>
public interface ILocalKeyProtector
{
    /// <summary>保管庫に記録される保護方式の識別子。</summary>
    string Id { get; }

    byte[] Protect(byte[] data);

    /// <summary>別ユーザー・別PCなどで解除できない場合は CryptographicException を投げる。</summary>
    byte[] Unprotect(byte[] data);
}

/// <summary>追加保護なし（テスト・非Windows環境用）。</summary>
public sealed class NullKeyProtector : ILocalKeyProtector
{
    public static readonly NullKeyProtector Instance = new();
    public string Id => "none";
    public byte[] Protect(byte[] data) => (byte[])data.Clone();
    public byte[] Unprotect(byte[] data) => (byte[])data.Clone();
}
