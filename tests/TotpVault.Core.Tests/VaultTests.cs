using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TotpVault.Core.Logging;
using TotpVault.Core.Otp;
using TotpVault.Core.Security;
using TotpVault.Core.Storage;

namespace TotpVault.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "totpvault-tests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, recursive: true); } catch { /* ignore */ }
    }
}

internal static class Fixtures
{
    public const int FastIterations = 1_000; // テスト高速化のため（製品は 600,000 回）
    public static readonly byte[] Password = "correct horse battery staple"u8.ToArray();
    public static readonly byte[] Secret = Convert.FromHexString("48656C6C6F21DEADBEEF0102030405060708090A"); // 20 bytes

    public static AccountInfo Account(string issuer = "SuperSecretBank", string name = "alice@example.com") => new()
    {
        Issuer = issuer,
        AccountName = name,
        Algorithm = OtpAlgorithm.Sha1,
        Digits = 6,
        Period = 30,
    };

    public static byte[] RandomSecret() => RandomNumberGenerator.GetBytes(20);
}

/// <summary>DPAPI を模したテスト用保護層（XOR）。</summary>
internal sealed class FakeProtector(string id = "fake", bool failUnprotect = false) : ILocalKeyProtector
{
    public string Id => id;
    public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();
    public byte[] Unprotect(byte[] data)
        => failUnprotect ? throw new CryptographicException("Key not valid for use in specified state.") : Protect(data);
}

public class VaultStoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private string DbPath => _dir.File("vault.db");

    public void Dispose() => _dir.Dispose();

    private VaultStore NewVault(ILocalKeyProtector? protector = null)
        => VaultStore.Create(DbPath, Fixtures.Password, protector ?? NullKeyProtector.Instance, Fixtures.FastIterations);

    [Fact(DisplayName = "保管庫の作成 → 再オープン → 正しいパスワードで解除")]
    public void CreateReopenUnlock()
    {
        Guid id;
        using (var v = NewVault())
        {
            Assert.True(v.IsUnlocked);
            id = v.AddAccount(Fixtures.Account(), Fixtures.Secret).Id;
        }

        using var reopened = VaultStore.Open(DbPath, NullKeyProtector.Instance);
        Assert.False(reopened.IsUnlocked);
        Assert.False(reopened.TryUnlock("wrong password"u8));
        Assert.False(reopened.IsUnlocked);
        Assert.True(reopened.TryUnlock(Fixtures.Password));

        var accounts = reopened.LoadAccounts();
        Assert.Equal(0, accounts.CorruptedCount);
        var a = Assert.Single(accounts.Accounts);
        Assert.Equal(id, a.Id);
        Assert.Equal("SuperSecretBank", a.Issuer);
        Assert.Equal("alice@example.com", a.AccountName);
    }

    [Fact(DisplayName = "保管庫から生成したコードが RFC 実装と一致")]
    public void GeneratesCorrectCode()
    {
        using var v = NewVault();
        var a = v.AddAccount(Fixtures.Account() with { Algorithm = OtpAlgorithm.Sha512, Digits = 8, Period = 60 }, Fixtures.Secret);
        const long now = 1_700_000_000;
        Assert.Equal(Totp.Compute(Fixtures.Secret, now, OtpAlgorithm.Sha512, 8, 60), v.GenerateCode(a, now));
    }

    [Fact(DisplayName = "DBファイルに平文のシークレット・サービス名・アカウント名が含まれない")]
    public void NoPlaintextOnDisk()
    {
        using (var v = NewVault())
        {
            v.AddAccount(Fixtures.Account(), Fixtures.Secret);
            v.AddAccount(Fixtures.Account("日本サンプル銀行", "山田太郎"), Fixtures.RandomSecret());
        }
        SqliteConnection.ClearAllPools();

        var bytes = File.ReadAllBytes(DbPath);
        Assert.False(Contains(bytes, Fixtures.Secret), "raw secret found");
        Assert.False(Contains(bytes, Encoding.ASCII.GetBytes(Base32.Encode(Fixtures.Secret))), "base32 secret found");
        Assert.False(Contains(bytes, Encoding.UTF8.GetBytes("SuperSecretBank")), "issuer found");
        Assert.False(Contains(bytes, Encoding.UTF8.GetBytes("alice@example.com")), "account name found");
        Assert.False(Contains(bytes, Encoding.UTF8.GetBytes("日本サンプル銀行")), "japanese issuer found");
        Assert.False(Contains(bytes, Fixtures.Password), "password found");
        Assert.False(File.Exists(DbPath + "-journal"));
    }

    [Fact(DisplayName = "ロック中はコード生成・一覧取得ができない")]
    public void LockedOperationsFail()
    {
        using var v = NewVault();
        var a = v.AddAccount(Fixtures.Account(), Fixtures.Secret);
        v.Lock();
        Assert.False(v.IsUnlocked);
        Assert.Throws<VaultLockedException>(() => v.GenerateCode(a, 0));
        Assert.Throws<VaultLockedException>(() => v.LoadAccounts());
        Assert.Throws<VaultLockedException>(() => v.ExportAccounts());
        Assert.Throws<VaultLockedException>(() => v.AddAccount(Fixtures.Account(), Fixtures.Secret));
    }

    [Fact(DisplayName = "アカウントの編集・お気に入り・削除・並び替えが保存される")]
    public void EditDeleteReorder()
    {
        using (var v = NewVault())
        {
            var a = v.AddAccount(Fixtures.Account("A", "1"), Fixtures.RandomSecret());
            var b = v.AddAccount(Fixtures.Account("B", "2"), Fixtures.RandomSecret());
            var c = v.AddAccount(Fixtures.Account("C", "3"), Fixtures.RandomSecret());
            v.UpdateAccount(b with { Issuer = "B2", IsFavorite = true, Digits = 8 });
            v.DeleteAccount(a.Id);
            v.SetOrder([c.Id, b.Id]);
        }

        using var r = VaultStore.Open(DbPath, NullKeyProtector.Instance);
        Assert.True(r.TryUnlock(Fixtures.Password));
        var list = r.LoadAccounts().Accounts;
        Assert.Equal(["C", "B2"], list.Select(x => x.Issuer));
        Assert.True(list[1].IsFavorite);
        Assert.Equal(8, list[1].Digits);
    }

    [Fact(DisplayName = "入力値の検証（空の名前・短いシークレット・不正な桁数）")]
    public void ValidatesInput()
    {
        using var v = NewVault();
        Assert.Throws<ArgumentException>(() => v.AddAccount(Fixtures.Account("", ""), Fixtures.Secret));
        Assert.Throws<ArgumentException>(() => v.AddAccount(Fixtures.Account(), new byte[5]));
        Assert.Throws<ArgumentException>(() => v.AddAccount(Fixtures.Account() with { Digits = 5 }, Fixtures.Secret));
        Assert.Throws<ArgumentException>(() => v.AddAccount(Fixtures.Account() with { Period = 0 }, Fixtures.Secret));
    }

    [Fact(DisplayName = "マスターパスワード変更: 旧パスワード無効・新パスワード有効・データ鍵も更新")]
    public void ChangePassword()
    {
        var newPassword = "新しいパスワード-2026!"u8.ToArray();
        string codeBefore;
        byte[] infoBefore;
        using (var v = NewVault())
        {
            var a = v.AddAccount(Fixtures.Account(), Fixtures.Secret);
            codeBefore = v.GenerateCode(a, 1_000_000);
            infoBefore = ReadBlob("info");

            Assert.False(v.ChangePassword("not the password"u8, newPassword));
            Assert.True(v.ChangePassword(Fixtures.Password, newPassword));
            Assert.Equal(codeBefore, v.GenerateCode(a, 1_000_000)); // 変更直後もそのまま使える
        }

        Assert.NotEqual(infoBefore, ReadBlob("info")); // DEK ローテーションで再暗号化されている

        using var r = VaultStore.Open(DbPath, NullKeyProtector.Instance);
        Assert.False(r.TryUnlock(Fixtures.Password));
        Assert.True(r.TryUnlock(newPassword));
        var acc = Assert.Single(r.LoadAccounts().Accounts);
        Assert.Equal(codeBefore, r.GenerateCode(acc, 1_000_000));
    }

    [Fact(DisplayName = "暗号化レコードの差し替え（行の入れ替え）を検出")]
    public void DetectsRowSwap()
    {
        using (var v = NewVault())
        {
            v.AddAccount(Fixtures.Account("A", "1"), Fixtures.RandomSecret());
            v.AddAccount(Fixtures.Account("B", "2"), Fixtures.RandomSecret());
        }

        using (var db = OpenRaw())
        {
            var rows = new List<(string Id, byte[] Info)>();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT id, info FROM accounts";
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) rows.Add((rd.GetString(0), rd.GetFieldValue<byte[]>(1)));
            }
            Exec(db, "UPDATE accounts SET info = $v WHERE id = $id", ("$v", rows[1].Info), ("$id", rows[0].Id));
            Exec(db, "UPDATE accounts SET info = $v WHERE id = $id", ("$v", rows[0].Info), ("$id", rows[1].Id));
        }

        using var r = VaultStore.Open(DbPath, NullKeyProtector.Instance);
        Assert.True(r.TryUnlock(Fixtures.Password));
        var result = r.LoadAccounts();
        Assert.Empty(result.Accounts);
        Assert.Equal(2, result.CorruptedCount);
    }

    [Fact(DisplayName = "暗号文の1バイト改ざんを検出")]
    public void DetectsBitFlip()
    {
        using (var v = NewVault()) v.AddAccount(Fixtures.Account(), Fixtures.Secret);
        var blob = ReadBlob("info");
        blob[20] ^= 0xFF;
        using (var db = OpenRaw()) Exec(db, "UPDATE accounts SET info = $v", ("$v", blob));

        using var r = VaultStore.Open(DbPath, NullKeyProtector.Instance);
        Assert.True(r.TryUnlock(Fixtures.Password));
        Assert.Equal(1, r.LoadAccounts().CorruptedCount);
    }

    [Fact(DisplayName = "KDF パラメータの改ざんを検出（解除不可）")]
    public void DetectsKdfTampering()
    {
        using (NewVault()) { }
        using (var db = OpenRaw()) Exec(db, "UPDATE meta SET value = '1001' WHERE key = 'kdf_iterations'");

        using var r = VaultStore.Open(DbPath, NullKeyProtector.Instance);
        Assert.False(r.TryUnlock(Fixtures.Password));
    }

    [Fact(DisplayName = "破損したファイル（DBではない）を開くとエラー")]
    public void RejectsGarbageFile()
    {
        File.WriteAllBytes(DbPath, RandomNumberGenerator.GetBytes(4096));
        Assert.Throws<VaultCorruptedException>(() => VaultStore.Open(DbPath, NullKeyProtector.Instance));
    }

    [Fact(DisplayName = "新しいバージョンで作成された保管庫は開かない")]
    public void RejectsNewerSchema()
    {
        using (NewVault()) { }
        using (var db = OpenRaw()) Exec(db, "UPDATE meta SET value = '99' WHERE key = 'schema_version'");
        Assert.Throws<VaultCorruptedException>(() => VaultStore.Open(DbPath, NullKeyProtector.Instance));
    }

    [Fact(DisplayName = "DPAPI 層: 別ユーザー/別PCでは解除できない")]
    public void ProtectorFailureMeansOtherMachine()
    {
        using (NewVault(new FakeProtector())) { }
        using var r = VaultStore.Open(DbPath, new FakeProtector(failUnprotect: true));
        Assert.Throws<VaultAccessDeniedException>(() => r.TryUnlock(Fixtures.Password));
    }

    [Fact(DisplayName = "DPAPI 層: 保護方式が異なる保管庫は開かない")]
    public void ProtectorMismatch()
    {
        using (NewVault(new FakeProtector("dpapi-user"))) { }
        Assert.Throws<VaultCorruptedException>(() => VaultStore.Open(DbPath, NullKeyProtector.Instance));
    }

    [Fact(DisplayName = "同じシークレットの重複登録を検出")]
    public void DetectsDuplicateSecret()
    {
        using var v = NewVault();
        v.AddAccount(Fixtures.Account(), Fixtures.Secret);
        Assert.True(v.ContainsSecret(Fixtures.Secret));
        Assert.False(v.ContainsSecret(Fixtures.RandomSecret()));
    }

    [Fact(DisplayName = "設定値の保存と読み込み")]
    public void Settings()
    {
        using (var v = NewVault()) v.SetSetting("autolock_minutes", "10");
        using var r = VaultStore.Open(DbPath, NullKeyProtector.Instance);
        Assert.Equal("10", r.GetSetting("autolock_minutes"));
        Assert.Null(r.GetSetting("missing"));
    }

    private SqliteConnection OpenRaw()
    {
        SqliteConnection.ClearAllPools();
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    private byte[] ReadBlob(string column)
    {
        using var db = OpenRaw();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT {column} FROM accounts LIMIT 1";
        return (byte[])cmd.ExecuteScalar()!;
    }

    private static void Exec(SqliteConnection db, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    internal static bool Contains(byte[] haystack, byte[] needle)
        => haystack.AsSpan().IndexOf(needle) >= 0;
}

public class BackupTests : IDisposable
{
    private readonly TempDir _dir = new();
    private static readonly byte[] BackupPassword = "backup-pass-123"u8.ToArray();

    public void Dispose() => _dir.Dispose();

    private static List<PortableAccount> Sample() =>
    [
        new() { Issuer = "SuperSecretBank", AccountName = "alice@example.com", Secret = (byte[])Fixtures.Secret.Clone(), IsFavorite = true },
        new() { Issuer = "GitHub", AccountName = "bob", Secret = Fixtures.RandomSecret(), Algorithm = OtpAlgorithm.Sha256, Digits = 8, Period = 60 },
    ];

    [Fact(DisplayName = "バックアップの作成と復元（往復）")]
    public void RoundTrip()
    {
        var path = _dir.File("b.tvbackup");
        var original = Sample();
        BackupFile.Write(path, original, BackupPassword, Fixtures.FastIterations);

        var restored = BackupFile.Read(path, BackupPassword);
        Assert.Equal(2, restored.Count);
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(original[i].Issuer, restored[i].Issuer);
            Assert.Equal(original[i].AccountName, restored[i].AccountName);
            Assert.Equal(original[i].Secret, restored[i].Secret);
            Assert.Equal(original[i].Algorithm, restored[i].Algorithm);
            Assert.Equal(original[i].Digits, restored[i].Digits);
            Assert.Equal(original[i].Period, restored[i].Period);
            Assert.Equal(original[i].IsFavorite, restored[i].IsFavorite);
        }
    }

    [Fact(DisplayName = "バックアップファイルに平文の機密情報が含まれない")]
    public void NoPlaintext()
    {
        var path = _dir.File("b.tvbackup");
        BackupFile.Write(path, Sample(), BackupPassword, Fixtures.FastIterations);
        var bytes = File.ReadAllBytes(path);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("SuperSecretBank", text);
        Assert.DoesNotContain("alice@example.com", text);
        Assert.DoesNotContain(Base32.Encode(Fixtures.Secret), text);
        Assert.DoesNotContain(Convert.ToBase64String(Fixtures.Secret), text);
        Assert.False(VaultStoreTests.Contains(bytes, Fixtures.Secret));
    }

    [Fact(DisplayName = "パスワード誤りは BackupDecryptionException")]
    public void WrongPassword()
    {
        var path = _dir.File("b.tvbackup");
        BackupFile.Write(path, Sample(), BackupPassword, Fixtures.FastIterations);
        Assert.Throws<BackupDecryptionException>(() => BackupFile.Read(path, "wrong"u8));
    }

    [Fact(DisplayName = "暗号化データの改ざんを検出")]
    public void TamperedPayload()
    {
        var path = _dir.File("b.tvbackup");
        BackupFile.Write(path, Sample(), BackupPassword, Fixtures.FastIterations);
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        var bytes = Convert.FromBase64String(node["payload"]!.GetValue<string>());
        bytes[^5] ^= 0x01;
        node["payload"] = Convert.ToBase64String(bytes);
        File.WriteAllText(path, node.ToJsonString());
        Assert.Throws<BackupDecryptionException>(() => BackupFile.Read(path, BackupPassword));
    }

    [Theory(DisplayName = "ヘッダーの改ざんを検出（作成日時・反復回数）")]
    [InlineData("\"iterations\": 1000", "\"iterations\": 1001")]
    [InlineData("\"createdAt\": \"20", "\"createdAt\": \"19")]
    public void TamperedHeader(string from, string to)
    {
        var path = _dir.File("b.tvbackup");
        BackupFile.Write(path, Sample(), BackupPassword, Fixtures.FastIterations);
        var json = File.ReadAllText(path);
        Assert.Contains(from, json);
        File.WriteAllText(path, json.Replace(from, to));
        Assert.Throws<BackupDecryptionException>(() => BackupFile.Read(path, BackupPassword));
    }

    [Fact(DisplayName = "途中で切れた（破損した）ファイルは BackupFormatException")]
    public void TruncatedFile()
    {
        var path = _dir.File("b.tvbackup");
        BackupFile.Write(path, Sample(), BackupPassword, Fixtures.FastIterations);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);
        Assert.Throws<BackupFormatException>(() => BackupFile.Read(path, BackupPassword));
    }

    [Theory(DisplayName = "形式違い・新しいバージョンのファイルは BackupFormatException")]
    [InlineData("{\"hello\":\"world\"}")]
    [InlineData("not json at all")]
    [InlineData("{\"format\":\"TotpVault.Backup\",\"version\":99}")]
    [InlineData("{\"format\":\"TotpVault.Backup\",\"version\":1,\"kdf\":\"pbkdf2-sha256\",\"iterations\":1000,\"salt\":\"!!\",\"payload\":\"\"}")]
    public void InvalidFormat(string content)
    {
        var path = _dir.File("b.tvbackup");
        File.WriteAllText(path, content);
        Assert.Throws<BackupFormatException>(() => BackupFile.Read(path, BackupPassword));
    }

    [Fact(DisplayName = "保管庫 → バックアップ → 別の保管庫へ復元（別PC移行を想定）、重複はスキップ")]
    public void VaultToVaultMigration()
    {
        var path = _dir.File("b.tvbackup");
        using (var source = VaultStore.Create(_dir.File("a.db"), Fixtures.Password, new FakeProtector(), Fixtures.FastIterations))
        {
            source.AddAccount(Fixtures.Account("A", "1"), Fixtures.Secret);
            source.AddAccount(Fixtures.Account("B", "2") with { IsFavorite = true, Digits = 8 }, Fixtures.RandomSecret());
            var exported = source.ExportAccounts();
            BackupFile.Write(path, exported, BackupPassword, Fixtures.FastIterations);
            exported.ForEach(a => a.ClearSecret());
        }

        // 別PC = 別の DPAPI 鍵、別のマスターパスワード
        using var target = VaultStore.Create(_dir.File("b.db"), "other-master"u8, new FakeProtector("other"), Fixtures.FastIterations);
        target.AddAccount(Fixtures.Account("A (既存)", "1"), Fixtures.Secret);

        var result = target.ImportAccounts(BackupFile.Read(path, BackupPassword));
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.SkippedDuplicates);

        var list = target.LoadAccounts().Accounts;
        Assert.Equal(2, list.Count);
        var b = list.Single(x => x.Issuer == "B");
        Assert.True(b.IsFavorite);
        Assert.Equal(8, b.Digits);

        // 再度取り込んでも増えない
        Assert.Equal(0, target.ImportAccounts(BackupFile.Read(path, BackupPassword)).Added);
    }
}

public class LogSafetyTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose()
    {
        Log.Configure(null);
        _dir.Dispose();
    }

    [Fact(DisplayName = "ログにシークレット・コード・パスワード・アカウント名を出力しない")]
    public void LogsContainNoSecrets()
    {
        var sink = new MemoryLogSink();
        Log.Configure(sink);
        var codes = new List<string>();
        var backup = _dir.File("x.tvbackup");
        var backupPassword = "backup-PW-xyz"u8.ToArray();
        var newPassword = "new-MASTER-pw-987"u8.ToArray();

        using (var v = VaultStore.Create(_dir.File("v.db"), Fixtures.Password, NullKeyProtector.Instance, Fixtures.FastIterations))
        {
            var a = v.AddAccount(Fixtures.Account(), Fixtures.Secret);
            codes.Add(v.GenerateCode(a, 1_234_567_890));
            v.UpdateAccount(a with { IsFavorite = true });
            v.ChangePassword("bad"u8, newPassword);
            v.ChangePassword(Fixtures.Password, newPassword);
            var exported = v.ExportAccounts();
            BackupFile.Write(backup, exported, backupPassword, Fixtures.FastIterations);
            Assert.Throws<BackupDecryptionException>(() => BackupFile.Read(backup, "nope"u8));
            v.ImportAccounts(BackupFile.Read(backup, backupPassword));
            v.Lock();
            v.TryUnlock("Wr0ng-Pa55-zz"u8);
            v.TryUnlock(newPassword);
            v.DeleteAccount(a.Id);
        }

        var all = string.Join("\n", sink.Lines);
        Assert.NotEmpty(sink.Lines);
        Assert.Contains("Unlock failed", all);
        foreach (var forbidden in new[]
                 {
                     Encoding.UTF8.GetString(Fixtures.Password), Encoding.UTF8.GetString(newPassword),
                     Encoding.UTF8.GetString(backupPassword), "Wr0ng-Pa55-zz", Base32.Encode(Fixtures.Secret),
                     Convert.ToHexString(Fixtures.Secret), Convert.ToBase64String(Fixtures.Secret),
                     "SuperSecretBank", "alice@example.com",
                 }.Concat(codes))
        {
            Assert.DoesNotContain(forbidden, all, StringComparison.OrdinalIgnoreCase);
        }
    }
}
