using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using TotpVault.Core.Logging;
using TotpVault.Core.Otp;
using TotpVault.Core.Security;

namespace TotpVault.Core.Storage;

/// <summary>
/// 暗号化ローカル保管庫 (SQLite)。
///
/// 鍵階層:
///   マスターパスワード ──PBKDF2-SHA256──▶ KEK（鍵暗号化鍵）
///   KEK ──AES-256-GCM──▶ DEK（ランダム生成の256bitデータ暗号化鍵）を包む
///   包んだDEK ──DPAPI(CurrentUser)──▶ meta.wrapped_dek に保存
///   DEK ──AES-256-GCM──▶ 各アカウントの「表示情報」と「シークレットキー」を個別に暗号化
///                         （AAD = レコード種別 + アカウントID。行の差し替えを検出）
///
/// DB に平文で保存されるのは ID・並び順・KDF パラメータ・アプリ設定のみ。
/// サービス名やアカウント名も暗号化されるため、DBファイルから利用サービスは判別できない。
/// ロック解除中もシークレットキーは暗号化されたままメモリに保持し、コード生成の瞬間だけ復号して即消去する。
/// </summary>
public sealed class VaultStore : IDisposable
{
    public const int CurrentSchemaVersion = 1;
    private const string FormatId = "TotpVault.Vault";
    private const string DekContext = "TotpVault.dek.v1";
    private const string SettingPrefix = "setting.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly SqliteConnection _db;
    private readonly ILocalKeyProtector _protector;
    private readonly Dictionary<Guid, byte[]> _secretBlobs = [];
    private byte[]? _dek;
    private bool _disposed;

    private VaultStore(string path, SqliteConnection db, ILocalKeyProtector protector)
    {
        FilePath = path;
        _db = db;
        _protector = protector;
    }

    public string FilePath { get; }

    public bool IsUnlocked
    {
        get { lock (_gate) return _dek is not null; }
    }

    public static bool Exists(string path) => File.Exists(path);

    public static VaultStore Create(string path, ReadOnlySpan<byte> password, ILocalKeyProtector protector,
        int kdfIterations = KdfParameters.DefaultIterations)
    {
        if (File.Exists(path)) throw new InvalidOperationException("Vault already exists.");
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var db = OpenConnection(path, SqliteOpenMode.ReadWriteCreate);
        var dek = SecureMemory.AllocatePinned(AeadCipher.KeySize);
        RandomNumberGenerator.Fill(dek);
        try
        {
            using (var tx = db.BeginTransaction())
            {
                Execute(db, tx, """
                    CREATE TABLE meta (
                        key   TEXT PRIMARY KEY,
                        value TEXT NOT NULL
                    );
                    CREATE TABLE accounts (
                        id         TEXT PRIMARY KEY,
                        sort_order INTEGER NOT NULL,
                        info       BLOB NOT NULL,
                        secret     BLOB NOT NULL
                    );
                    """);
                SetMeta(db, tx, "format", FormatId);
                SetMeta(db, tx, "schema_version", CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
                SetMeta(db, tx, "key_protector", protector.Id);
                SetMeta(db, tx, "created_at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

                var kdf = KdfParameters.CreateNew(kdfIterations);
                var kek = kdf.DeriveKey(password);
                try { WriteKeyMaterial(db, tx, protector, kdf, dek, kek); }
                finally { SecureMemory.Zero(kek); }

                tx.Commit();
            }

            Log.Info("Vault created.");
            return new VaultStore(path, db, protector) { _dek = dek };
        }
        catch
        {
            SecureMemory.Zero(dek);
            db.Dispose();
            DeleteFiles(path);
            throw;
        }
    }

    public static VaultStore Open(string path, ILocalKeyProtector protector)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Vault not found.", path);

        SqliteConnection? db = null;
        try
        {
            db = OpenConnection(path, SqliteOpenMode.ReadWrite);
            if (GetMeta(db, null, "format") != FormatId)
                throw new VaultCorruptedException("保管庫ファイルの形式が正しくありません。");

            if (!int.TryParse(GetMeta(db, null, "schema_version"), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
                throw new VaultCorruptedException("保管庫ファイルのバージョン情報が不正です。");
            if (version > CurrentSchemaVersion)
                throw new VaultCorruptedException("このデータは新しいバージョンのアプリで作成されています。アプリを更新してください。");

            if (GetMeta(db, null, "key_protector") != protector.Id)
                throw new VaultCorruptedException("保管庫の保護方式が一致しません。");

            // 将来のスキーマ変更時はここで version に応じた移行処理を行う
            return new VaultStore(path, db, protector);
        }
        catch (SqliteException ex)
        {
            db?.Dispose();
            throw new VaultCorruptedException("保管庫ファイルを読み込めません（破損している可能性があります）。", ex);
        }
        catch
        {
            db?.Dispose();
            throw;
        }
    }

    /// <summary>保管庫ファイルを削除する（初期化用）。事前に Dispose しておくこと。</summary>
    public static void DeleteFiles(string path)
    {
        foreach (var file in new[] { path, path + "-journal", path + "-wal", path + "-shm" })
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    // ───────────────────────────── ロック / ロック解除 ─────────────────────────────

    /// <summary>マスターパスワードでロックを解除する。パスワード誤りの場合は false。</summary>
    /// <exception cref="VaultAccessDeniedException">別ユーザー・別PCの保管庫で DPAPI を解除できない。</exception>
    /// <exception cref="VaultCorruptedException">鍵情報が破損している。</exception>
    public bool TryUnlock(ReadOnlySpan<byte> password)
    {
        KdfParameters kdf;
        byte[] wrapped;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_dek is not null) return true;
            (kdf, wrapped) = ReadWrappedKey();
        }

        // 鍵導出（重い処理）はロック外で行う
        var dek = UnwrapDek(kdf, wrapped, password);
        if (dek is null)
        {
            Log.Warn("Unlock failed (password mismatch).");
            return false;
        }

        lock (_gate)
        {
            if (_dek is not null) { SecureMemory.Zero(dek); return true; }
            _dek = dek;
            ReloadSecretBlobs();
        }
        Log.Info("Vault unlocked.");
        return true;
    }

    /// <summary>データ鍵をメモリから消去し、暗号化済みシークレットのキャッシュも破棄する。</summary>
    public void Lock()
    {
        lock (_gate)
        {
            if (_dek is null) return;
            SecureMemory.Zero(_dek);
            _dek = null;
            _secretBlobs.Clear();
        }
        Log.Info("Vault locked.");
    }

    /// <summary>
    /// マスターパスワードを変更する。現在のパスワードが誤っている場合は false。
    /// 変更時はデータ鍵 (DEK) も新しく生成し、全アカウントを再暗号化する。
    /// </summary>
    public bool ChangePassword(ReadOnlySpan<byte> currentPassword, ReadOnlySpan<byte> newPassword)
    {
        KdfParameters oldKdf;
        byte[] wrapped;
        lock (_gate)
        {
            RequireKey();
            (oldKdf, wrapped) = ReadWrappedKey();
        }

        var verified = UnwrapDek(oldKdf, wrapped, currentPassword);
        if (verified is null)
        {
            Log.Warn("Password change rejected (current password mismatch).");
            return false;
        }
        SecureMemory.Zero(verified);

        var newKdf = KdfParameters.CreateNew(oldKdf.Iterations);
        var newKek = newKdf.DeriveKey(newPassword);
        var newDek = SecureMemory.AllocatePinned(AeadCipher.KeySize);
        RandomNumberGenerator.Fill(newDek);
        bool installed = false;
        try
        {
            lock (_gate)
            {
                var oldDek = RequireKey();
                using (var tx = _db.BeginTransaction())
                {
                    foreach (var (id, infoBlob, secretBlob) in ReadAllRows(tx))
                    {
                        var newInfo = Reencrypt(oldDek, newDek, infoBlob, Aad("info", id));
                        var newSecret = Reencrypt(oldDek, newDek, secretBlob, Aad("secret", id));
                        using var cmd = Command(tx, "UPDATE accounts SET info = $info, secret = $secret WHERE id = $id");
                        cmd.Parameters.AddWithValue("$info", newInfo);
                        cmd.Parameters.AddWithValue("$secret", newSecret);
                        cmd.Parameters.AddWithValue("$id", id.ToString("N"));
                        cmd.ExecuteNonQuery();
                    }
                    WriteKeyMaterial(_db, tx, _protector, newKdf, newDek, newKek);
                    tx.Commit();
                }

                SecureMemory.Zero(oldDek);
                _dek = newDek;
                installed = true;
                ReloadSecretBlobs();
            }
        }
        catch (CryptographicException ex)
        {
            throw new VaultCorruptedException("破損したアカウントデータがあるため、パスワードを変更できませんでした。", ex);
        }
        finally
        {
            SecureMemory.Zero(newKek);
            if (!installed) SecureMemory.Zero(newDek);
        }

        Log.Info("Master password changed (data key rotated).");
        return true;
    }

    // ───────────────────────────── アカウント操作 ─────────────────────────────

    public VaultLoadResult LoadAccounts()
    {
        lock (_gate)
        {
            var dek = RequireKey();
            var accounts = new List<AccountInfo>();
            int corrupted = 0;
            _secretBlobs.Clear();

            using var cmd = Command(null, "SELECT id, sort_order, info, secret FROM accounts ORDER BY sort_order, rowid");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!Guid.TryParseExact(reader.GetString(0), "N", out var id))
                {
                    corrupted++;
                    continue;
                }

                try
                {
                    var info = DecryptInfo(dek, id, reader.GetFieldValue<byte[]>(2)) with { SortOrder = reader.GetInt32(1) };
                    accounts.Add(info);
                    _secretBlobs[id] = reader.GetFieldValue<byte[]>(3);
                }
                catch (Exception ex) when (ex is CryptographicException or JsonException)
                {
                    corrupted++;
                    Log.Warn($"Account record {id:N} could not be decrypted (tampered or corrupted).");
                }
            }
            return new VaultLoadResult(accounts, corrupted);
        }
    }

    public AccountInfo AddAccount(AccountInfo info, ReadOnlySpan<byte> secret)
    {
        info.Validate();
        ValidateSecret(secret);
        lock (_gate)
        {
            var dek = RequireKey();
            var stored = InsertAccount(dek, info, secret, null);
            Log.Info($"Account added: {stored.Id:N}");
            return stored;
        }
    }

    /// <summary>表示情報（サービス名・アカウント名・アルゴリズム等・お気に入り）を更新する。シークレットは変更しない。</summary>
    public void UpdateAccount(AccountInfo info)
    {
        info.Validate();
        lock (_gate)
        {
            var dek = RequireKey();
            using var cmd = Command(null, "UPDATE accounts SET info = $info WHERE id = $id");
            cmd.Parameters.AddWithValue("$info", EncryptInfo(dek, info));
            cmd.Parameters.AddWithValue("$id", info.Id.ToString("N"));
            if (cmd.ExecuteNonQuery() == 0) throw new KeyNotFoundException("Account not found.");
        }
        Log.Info($"Account updated: {info.Id:N}");
    }

    public void DeleteAccount(Guid id)
    {
        lock (_gate)
        {
            RequireKey();
            using var cmd = Command(null, "DELETE FROM accounts WHERE id = $id");
            cmd.Parameters.AddWithValue("$id", id.ToString("N"));
            cmd.ExecuteNonQuery();
            _secretBlobs.Remove(id);
        }
        Log.Info($"Account deleted: {id:N}");
    }

    public void SetOrder(IReadOnlyList<Guid> orderedIds)
    {
        lock (_gate)
        {
            RequireKey();
            using var tx = _db.BeginTransaction();
            for (int i = 0; i < orderedIds.Count; i++)
            {
                using var cmd = Command(tx, "UPDATE accounts SET sort_order = $order WHERE id = $id");
                cmd.Parameters.AddWithValue("$order", i);
                cmd.Parameters.AddWithValue("$id", orderedIds[i].ToString("N"));
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    /// <summary>認証コードを生成する。シークレットキーは生成の間だけ復号され、直後に消去される。</summary>
    public string GenerateCode(AccountInfo account, long unixTimeSeconds)
    {
        lock (_gate)
        {
            var dek = RequireKey();
            if (!_secretBlobs.TryGetValue(account.Id, out var blob)) throw new KeyNotFoundException("Account not found.");
            var secret = AeadCipher.Decrypt(dek, blob, Aad("secret", account.Id));
            try
            {
                return Totp.Compute(secret, unixTimeSeconds, account.Algorithm, account.Digits, account.Period);
            }
            finally
            {
                SecureMemory.Zero(secret);
            }
        }
    }

    /// <summary>同じシークレットキーのアカウントが既に登録されているか。</summary>
    public bool ContainsSecret(ReadOnlySpan<byte> secret)
    {
        lock (_gate)
        {
            var dek = RequireKey();
            return ContainsSecretCore(dek, secret);
        }
    }

    /// <summary>バックアップ用に全アカウントをシークレット付きで取り出す。呼び出し側で ClearSecret すること。</summary>
    public List<PortableAccount> ExportAccounts()
    {
        lock (_gate)
        {
            var dek = RequireKey();
            var result = new List<PortableAccount>();
            try
            {
                using var cmd = Command(null, "SELECT id, info, secret FROM accounts ORDER BY sort_order, rowid");
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = Guid.ParseExact(reader.GetString(0), "N");
                    var info = DecryptInfo(dek, id, reader.GetFieldValue<byte[]>(1));
                    var secret = AeadCipher.Decrypt(dek, reader.GetFieldValue<byte[]>(2), Aad("secret", id));
                    result.Add(new PortableAccount
                    {
                        Issuer = info.Issuer,
                        AccountName = info.AccountName,
                        Secret = secret,
                        Algorithm = info.Algorithm,
                        Digits = info.Digits,
                        Period = info.Period,
                        IsFavorite = info.IsFavorite,
                    });
                }
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
            {
                foreach (var a in result) a.ClearSecret();
                throw new VaultCorruptedException("破損したアカウントデータがあるため、バックアップを作成できませんでした。", ex);
            }

            Log.Info($"Accounts exported for backup ({result.Count}).");
            return result;
        }
    }

    /// <summary>バックアップから取り込む。同じシークレットキーのアカウントは重複としてスキップする。</summary>
    public ImportResult ImportAccounts(IEnumerable<PortableAccount> accounts)
    {
        lock (_gate)
        {
            var dek = RequireKey();
            int added = 0, skipped = 0;
            try
            {
                using var tx = _db.BeginTransaction();
                foreach (var a in accounts)
                {
                    if (ContainsSecretCore(dek, a.Secret))
                    {
                        skipped++;
                        continue;
                    }

                    var info = new AccountInfo
                    {
                        Issuer = a.Issuer,
                        AccountName = a.AccountName,
                        Algorithm = a.Algorithm,
                        Digits = a.Digits,
                        Period = a.Period,
                        IsFavorite = a.IsFavorite,
                    };
                    info.Validate();
                    ValidateSecret(a.Secret);
                    InsertAccount(dek, info, a.Secret, tx);
                    added++;
                }
                tx.Commit();
            }
            finally
            {
                // 失敗してロールバックされた場合に備え、キャッシュは DB から再読込する
                ReloadSecretBlobs();
            }

            Log.Info($"Accounts imported (added={added}, skipped={skipped}).");
            return new ImportResult(added, skipped);
        }
    }

    // ───────────────────────────── 設定（非機密） ─────────────────────────────

    public string? GetSetting(string key)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return GetMeta(_db, null, SettingPrefix + key);
        }
    }

    public void SetSetting(string key, string value)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            SetMeta(_db, null, SettingPrefix + key, value);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Lock();
        lock (_gate)
        {
            _db.Dispose();
            _disposed = true;
        }
    }

    // ───────────────────────────── 内部処理 ─────────────────────────────

    private AccountInfo InsertAccount(byte[] dek, AccountInfo info, ReadOnlySpan<byte> secret, SqliteTransaction? tx)
    {
        var id = info.Id == Guid.Empty ? Guid.NewGuid() : info.Id;
        int sortOrder;
        using (var max = Command(tx, "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM accounts"))
            sortOrder = Convert.ToInt32(max.ExecuteScalar(), CultureInfo.InvariantCulture);

        var stored = info with
        {
            Id = id,
            SortOrder = sortOrder,
            CreatedAt = info.CreatedAt == default ? DateTimeOffset.UtcNow : info.CreatedAt,
        };
        var secretBlob = AeadCipher.Encrypt(dek, secret, Aad("secret", id));

        using var cmd = Command(tx, "INSERT INTO accounts (id, sort_order, info, secret) VALUES ($id, $order, $info, $secret)");
        cmd.Parameters.AddWithValue("$id", id.ToString("N"));
        cmd.Parameters.AddWithValue("$order", sortOrder);
        cmd.Parameters.AddWithValue("$info", EncryptInfo(dek, stored));
        cmd.Parameters.AddWithValue("$secret", secretBlob);
        cmd.ExecuteNonQuery();

        _secretBlobs[id] = secretBlob;
        return stored;
    }

    private bool ContainsSecretCore(byte[] dek, ReadOnlySpan<byte> secret)
    {
        foreach (var (id, blob) in _secretBlobs)
        {
            byte[]? existing = null;
            try
            {
                existing = AeadCipher.Decrypt(dek, blob, Aad("secret", id));
                if (CryptographicOperations.FixedTimeEquals(existing, secret)) return true;
            }
            catch (CryptographicException)
            {
                // 破損レコードは比較対象外
            }
            finally
            {
                SecureMemory.Zero(existing);
            }
        }
        return false;
    }

    private void ReloadSecretBlobs()
    {
        _secretBlobs.Clear();
        using var cmd = Command(null, "SELECT id, secret FROM accounts");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (Guid.TryParseExact(reader.GetString(0), "N", out var id))
                _secretBlobs[id] = reader.GetFieldValue<byte[]>(1);
        }
    }

    private List<(Guid Id, byte[] Info, byte[] Secret)> ReadAllRows(SqliteTransaction tx)
    {
        var rows = new List<(Guid, byte[], byte[])>();
        using var cmd = Command(tx, "SELECT id, info, secret FROM accounts");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((Guid.ParseExact(reader.GetString(0), "N"), reader.GetFieldValue<byte[]>(1), reader.GetFieldValue<byte[]>(2)));
        }
        return rows;
    }

    private (KdfParameters Kdf, byte[] Wrapped) ReadWrappedKey()
    {
        KdfParameters kdf;
        byte[] protectedBlob;
        try
        {
            var algorithm = GetMeta(_db, null, "kdf") ?? throw new FormatException("kdf missing");
            var iterations = int.Parse(GetMeta(_db, null, "kdf_iterations") ?? "", NumberStyles.None, CultureInfo.InvariantCulture);
            var salt = Convert.FromBase64String(GetMeta(_db, null, "kdf_salt") ?? "");
            kdf = new KdfParameters(algorithm, iterations, salt);
            protectedBlob = Convert.FromBase64String(GetMeta(_db, null, "wrapped_dek") ?? throw new FormatException("key missing"));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException or OverflowException)
        {
            throw new VaultCorruptedException("保管庫の鍵情報が破損しています。", ex);
        }

        try
        {
            return (kdf, _protector.Unprotect(protectedBlob));
        }
        catch (CryptographicException ex)
        {
            throw new VaultAccessDeniedException(
                "この保管庫は別のWindowsユーザーまたは別のPCで作成されたため開けません。バックアップファイルから復元してください。", ex);
        }
    }

    private static byte[]? UnwrapDek(KdfParameters kdf, byte[] wrapped, ReadOnlySpan<byte> password)
    {
        var kek = kdf.DeriveKey(password);
        try
        {
            var dek = AeadCipher.Decrypt(kek, wrapped, kdf.ToAssociatedData(DekContext));
            if (dek.Length == AeadCipher.KeySize) return dek;
            SecureMemory.Zero(dek);
            throw new VaultCorruptedException("保管庫の鍵情報が破損しています。");
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            SecureMemory.Zero(kek);
        }
    }

    private static void WriteKeyMaterial(SqliteConnection db, SqliteTransaction tx, ILocalKeyProtector protector,
        KdfParameters kdf, byte[] dek, byte[] kek)
    {
        var wrapped = AeadCipher.Encrypt(kek, dek, kdf.ToAssociatedData(DekContext));
        SetMeta(db, tx, "kdf", kdf.Algorithm);
        SetMeta(db, tx, "kdf_iterations", kdf.Iterations.ToString(CultureInfo.InvariantCulture));
        SetMeta(db, tx, "kdf_salt", Convert.ToBase64String(kdf.Salt));
        SetMeta(db, tx, "wrapped_dek", Convert.ToBase64String(protector.Protect(wrapped)));
    }

    private static byte[] Reencrypt(byte[] oldKey, byte[] newKey, byte[] blob, byte[] aad)
    {
        var plaintext = AeadCipher.Decrypt(oldKey, blob, aad);
        try { return AeadCipher.Encrypt(newKey, plaintext, aad); }
        finally { SecureMemory.Zero(plaintext); }
    }

    private static AccountInfo DecryptInfo(byte[] dek, Guid id, byte[] blob)
    {
        var json = AeadCipher.Decrypt(dek, blob, Aad("info", id));
        try
        {
            var p = JsonSerializer.Deserialize<InfoPayload>(json, JsonOptions) ?? throw new JsonException("Empty payload.");
            return new AccountInfo
            {
                Id = id,
                Issuer = p.Issuer ?? "",
                AccountName = p.AccountName ?? "",
                Algorithm = p.Algorithm,
                Digits = p.Digits,
                Period = p.Period,
                IsFavorite = p.IsFavorite,
                CreatedAt = p.CreatedAt,
            };
        }
        finally
        {
            SecureMemory.Zero(json);
        }
    }

    private static byte[] EncryptInfo(byte[] dek, AccountInfo info)
    {
        var payload = new InfoPayload
        {
            Issuer = info.Issuer,
            AccountName = info.AccountName,
            Algorithm = info.Algorithm,
            Digits = info.Digits,
            Period = info.Period,
            IsFavorite = info.IsFavorite,
            CreatedAt = info.CreatedAt,
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        try { return AeadCipher.Encrypt(dek, json, Aad("info", info.Id)); }
        finally { SecureMemory.Zero(json); }
    }

    private static byte[] Aad(string kind, Guid id)
        => Encoding.UTF8.GetBytes($"TotpVault.account.{kind}.v1:{id:N}");

    private static void ValidateSecret(ReadOnlySpan<byte> secret)
    {
        if (secret.Length < Totp.MinSecretBytes) throw new ArgumentException("シークレットキーが短すぎます。");
        if (secret.Length > 256) throw new ArgumentException("シークレットキーが長すぎます。");
    }

    private byte[] RequireKey()
    {
        ThrowIfDisposed();
        return _dek ?? throw new VaultLockedException();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private SqliteCommand Command(SqliteTransaction? tx, string sql)
    {
        var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static SqliteConnection OpenConnection(string path, SqliteOpenMode mode)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false, // Dispose 時に確実にファイルを閉じる（初期化時の削除のため）
        }.ToString();

        var db = new SqliteConnection(connectionString);
        db.Open();
        // 削除したレコード領域をゼロで上書きする
        Execute(db, null, "PRAGMA secure_delete = ON;");
        return db;
    }

    private static void Execute(SqliteConnection db, SqliteTransaction? tx, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string? GetMeta(SqliteConnection db, SqliteTransaction? tx, string key)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT value FROM meta WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    private static void SetMeta(SqliteConnection db, SqliteTransaction? tx, string key, string value)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private sealed class InfoPayload
    {
        public string? Issuer { get; set; }
        public string? AccountName { get; set; }
        public OtpAlgorithm Algorithm { get; set; } = OtpAlgorithm.Sha1;
        public int Digits { get; set; } = Totp.DefaultDigits;
        public int Period { get; set; } = Totp.DefaultPeriod;
        public bool IsFavorite { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
