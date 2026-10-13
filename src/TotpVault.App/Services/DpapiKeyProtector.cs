using System.Security.Cryptography;
using TotpVault.Core.Security;

namespace TotpVault.App.Services;

/// <summary>
/// Windows DPAPI (CurrentUser) による追加保護。
/// 保管庫ファイルを別のユーザー・別のPCにコピーしても鍵情報を復号できない。
/// </summary>
public sealed class DpapiKeyProtector : ILocalKeyProtector
{
    private static readonly byte[] Entropy = "TotpVault.LocalKey.v1"u8.ToArray();

    public string Id => "dpapi-user";

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
