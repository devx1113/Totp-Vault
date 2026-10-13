namespace TotpVault.Core.Storage;

/// <summary>保管庫ファイルの形式不正・破損・改ざん。</summary>
public sealed class VaultCorruptedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>保管庫がロックされている状態で機密操作を行おうとした。</summary>
public sealed class VaultLockedException() : InvalidOperationException("保管庫はロックされています。");

/// <summary>DPAPI 保護を解除できない（別のWindowsユーザー・別PCで作成された保管庫）。</summary>
public sealed class VaultAccessDeniedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>バックアップファイルの形式が不正。</summary>
public sealed class BackupFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>バックアップの復号失敗（パスワード誤り、または破損・改ざん）。</summary>
public sealed class BackupDecryptionException(string message, Exception? inner = null) : Exception(message, inner);
