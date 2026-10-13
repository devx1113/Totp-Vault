using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TotpVault.Core.Logging;
using TotpVault.Core.Otp;
using TotpVault.Core.Security;

namespace TotpVault.Core.Storage;

/// <summary>
/// 暗号化バックアップファイル (.tvbackup)。
///
/// 端末に依存しない（DPAPI を使わない）ため、別のWindows PCへの移行にも使用できる。
///   ファイル = JSON { ヘッダー(形式・バージョン・作成日時・KDFパラメータ), payload }
///   payload  = AES-256-GCM(PBKDF2(バックアップ用パスワード), アカウント一覧JSON, AAD = ヘッダー全体)
/// ヘッダーも AAD で保護されるため、KDF パラメータ等の改ざんは復号失敗として検出される。
/// アカウント数などのメタ情報もヘッダーには含めない。
/// </summary>
public static class BackupFile
{
    public const string Extension = ".tvbackup";
    public const int CurrentVersion = 1;
    private const string FormatId = "TotpVault.Backup";
    private const long MaxFileSize = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions EnvelopeOptions = new() { WriteIndented = true };

    public static void Write(string path, IReadOnlyList<PortableAccount> accounts, ReadOnlySpan<byte> password,
        int kdfIterations = KdfParameters.DefaultIterations)
    {
        var kdf = KdfParameters.CreateNew(kdfIterations);
        var envelope = new Envelope
        {
            Format = FormatId,
            Version = CurrentVersion,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Kdf = kdf.Algorithm,
            Iterations = kdf.Iterations,
            Salt = Convert.ToBase64String(kdf.Salt),
        };

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new Payload { Accounts = accounts.ToList() });
        var key = kdf.DeriveKey(password);
        try
        {
            envelope.Payload = Convert.ToBase64String(AeadCipher.Encrypt(key, plaintext, HeaderAad(envelope)));
        }
        finally
        {
            SecureMemory.Zero(key);
            SecureMemory.Zero(plaintext);
        }

        // 書き込み途中で失敗しても既存ファイルを壊さないよう、一時ファイル経由で置き換える
        var fullPath = Path.GetFullPath(path);
        var temp = fullPath + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(envelope, EnvelopeOptions));
        File.Move(temp, fullPath, overwrite: true);
        Log.Info($"Backup written ({accounts.Count} accounts).");
    }

    /// <exception cref="BackupFormatException">ファイル形式が不正。</exception>
    /// <exception cref="BackupDecryptionException">パスワード誤り、または破損・改ざん。</exception>
    public static List<PortableAccount> Read(string path, ReadOnlySpan<byte> password)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new BackupFormatException("バックアップファイルが見つかりません。");
        if (info.Length > MaxFileSize) throw new BackupFormatException("バックアップファイルとして大きすぎます。");

        Envelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path))
                       ?? throw new BackupFormatException("バックアップファイルの形式が正しくありません。");
        }
        catch (JsonException ex)
        {
            throw new BackupFormatException("バックアップファイルの形式が正しくありません（破損している可能性があります）。", ex);
        }

        if (envelope.Format != FormatId)
            throw new BackupFormatException("TOTP Vault のバックアップファイルではありません。");
        if (envelope.Version > CurrentVersion)
            throw new BackupFormatException("新しいバージョンのアプリで作成されたバックアップです。アプリを更新してください。");
        if (envelope.Version < 1)
            throw new BackupFormatException("バックアップファイルのバージョン情報が不正です。");

        KdfParameters kdf;
        byte[] blob;
        try
        {
            kdf = new KdfParameters(envelope.Kdf ?? "", envelope.Iterations, Convert.FromBase64String(envelope.Salt ?? ""));
            blob = Convert.FromBase64String(envelope.Payload ?? "");
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException)
        {
            throw new BackupFormatException("バックアップファイルのヘッダーが不正です（破損している可能性があります）。", ex);
        }

        byte[] plaintext;
        var key = kdf.DeriveKey(password);
        try
        {
            plaintext = AeadCipher.Decrypt(key, blob, HeaderAad(envelope));
        }
        catch (CryptographicException ex)
        {
            Log.Warn("Backup decryption failed (wrong password or tampered file).");
            throw new BackupDecryptionException("パスワードが正しくないか、ファイルが破損・改ざんされています。", ex);
        }
        finally
        {
            SecureMemory.Zero(key);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(plaintext)
                          ?? throw new BackupFormatException("バックアップの内容が不正です。");
            var accounts = payload.Accounts ?? [];
            foreach (var a in accounts)
            {
                a.Issuer ??= "";
                a.AccountName ??= "";
                a.Secret ??= [];
                if (a.Secret.Length < Totp.MinSecretBytes || !Enum.IsDefined(a.Algorithm)
                    || !Totp.IsValidDigits(a.Digits) || !Totp.IsValidPeriod(a.Period))
                {
                    foreach (var x in accounts) x.ClearSecret();
                    throw new BackupFormatException("バックアップに不正なアカウントデータが含まれています。");
                }
            }
            Log.Info($"Backup read ({accounts.Count} accounts).");
            return accounts;
        }
        catch (JsonException ex)
        {
            throw new BackupFormatException("バックアップの内容が不正です。", ex);
        }
        finally
        {
            SecureMemory.Zero(plaintext);
        }
    }

    private static byte[] HeaderAad(Envelope e)
        => Encoding.UTF8.GetBytes(string.Join('|', e.Format, e.Version.ToString(CultureInfo.InvariantCulture),
            e.CreatedAt, e.Kdf, e.Iterations.ToString(CultureInfo.InvariantCulture), e.Salt));

    private sealed class Envelope
    {
        [JsonPropertyName("format")] public string? Format { get; set; }
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("createdAt")] public string? CreatedAt { get; set; }
        [JsonPropertyName("kdf")] public string? Kdf { get; set; }
        [JsonPropertyName("iterations")] public int Iterations { get; set; }
        [JsonPropertyName("salt")] public string? Salt { get; set; }
        [JsonPropertyName("payload")] public string? Payload { get; set; }
    }

    private sealed class Payload
    {
        public List<PortableAccount>? Accounts { get; set; }
    }
}
