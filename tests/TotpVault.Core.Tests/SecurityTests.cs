using System.Security.Cryptography;
using System.Text;
using TotpVault.Core.Security;

namespace TotpVault.Core.Tests;

public class AeadCipherTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] Aad = "record:1"u8.ToArray();

    [Fact(DisplayName = "AES-256-GCM 暗号化・復号の往復")]
    public void RoundTrip()
    {
        var plain = "シークレット"u8.ToArray();
        var blob = AeadCipher.Encrypt(Key, plain, Aad);
        Assert.Equal(1 + 12 + plain.Length + 16, blob.Length);
        Assert.Equal(plain, AeadCipher.Decrypt(Key, blob, Aad));
    }

    [Fact(DisplayName = "同じ平文でも毎回異なる暗号文（nonce の再利用なし）")]
    public void NonceIsNeverReused()
    {
        var nonces = new HashSet<string>();
        for (int i = 0; i < 10_000; i++)
        {
            var blob = AeadCipher.Encrypt(Key, "same"u8, Aad);
            Assert.True(nonces.Add(Convert.ToHexString(blob, 1, 12)), "nonce collision");
        }
    }

    [Theory(DisplayName = "暗号文のどの部分を改ざんしても検出")]
    [InlineData(0)]   // バージョン
    [InlineData(5)]   // nonce
    [InlineData(14)]  // 暗号文
    [InlineData(-1)]  // 認証タグ
    public void DetectsTampering(int index)
    {
        var blob = AeadCipher.Encrypt(Key, "0123456789"u8, Aad);
        int i = index >= 0 ? index : blob.Length - 1;
        blob[i] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => AeadCipher.Decrypt(Key, blob, Aad));
    }

    [Fact(DisplayName = "鍵が異なると復号できない")]
    public void WrongKeyFails()
    {
        var blob = AeadCipher.Encrypt(Key, "data"u8, Aad);
        Assert.ThrowsAny<CryptographicException>(() => AeadCipher.Decrypt(RandomNumberGenerator.GetBytes(32), blob, Aad));
    }

    [Fact(DisplayName = "関連データ (AAD) が異なると復号できない（レコード差し替え対策）")]
    public void WrongAadFails()
    {
        var blob = AeadCipher.Encrypt(Key, "data"u8, Aad);
        Assert.ThrowsAny<CryptographicException>(() => AeadCipher.Decrypt(Key, blob, "record:2"u8));
    }

    [Fact(DisplayName = "短すぎるデータは拒否")]
    public void RejectsTruncated()
        => Assert.ThrowsAny<CryptographicException>(() => AeadCipher.Decrypt(Key, new byte[10], Aad));
}

public class KdfTests
{
    [Fact(DisplayName = "PBKDF2: 同じパスワードとソルトなら同じ鍵")]
    public void Deterministic()
    {
        var kdf = KdfParameters.CreateNew(1_000);
        Assert.Equal(kdf.DeriveKey("pass"u8), kdf.DeriveKey("pass"u8));
        Assert.NotEqual(kdf.DeriveKey("pass"u8), kdf.DeriveKey("pasS"u8));
    }

    [Fact(DisplayName = "PBKDF2: ソルトが異なれば別の鍵")]
    public void SaltMatters()
        => Assert.NotEqual(KdfParameters.CreateNew(1_000).DeriveKey("pass"u8), KdfParameters.CreateNew(1_000).DeriveKey("pass"u8));

    [Fact(DisplayName = "既定の反復回数は 600,000 回 (OWASP 推奨)")]
    public void DefaultIterations() => Assert.Equal(600_000, KdfParameters.CreateNew().Iterations);

    [Fact(DisplayName = "PBKDF2-HMAC-SHA256 既知ベクター")]
    public void KnownVector()
    {
        // P="password", S="saltSALTsaltSALT"(16B), c=1000 を独立実装 (Rfc2898DeriveBytes) と照合
        var salt = "saltSALTsaltSALT"u8.ToArray();
        var expected = Rfc2898DeriveBytes.Pbkdf2("password"u8, salt, 1000, HashAlgorithmName.SHA256, 32);
        Assert.Equal(expected, new KdfParameters(KdfParameters.Pbkdf2Sha256, 1000, salt).DeriveKey("password"u8));
    }

    [Theory(DisplayName = "PBKDF2 代替実装が OS 実装と同一の出力")]
    [InlineData("password", "saltSALTsaltSALT", 1, 32)]
    [InlineData("password", "saltSALTsaltSALT", 4096, 32)]
    [InlineData("パスワード🔑", "0123456789abcdef0123456789abcdef", 1000, 32)]
    [InlineData("", "saltSALTsaltSALT", 2, 64)]
    [InlineData("long", "saltSALTsaltSALT", 3, 40)]
    public void ManagedPbkdf2MatchesPlatform(string password, string salt, int iterations, int length)
    {
        var pw = Encoding.UTF8.GetBytes(password);
        var s = Encoding.UTF8.GetBytes(salt);
        var expected = Rfc2898DeriveBytes.Pbkdf2(pw, s, iterations, HashAlgorithmName.SHA256, length);
        var actual = new byte[length];
        KdfParameters.Pbkdf2HmacSha256(pw, s, iterations, actual);
        Assert.Equal(expected, actual);
    }

    [Fact(DisplayName = "PBKDF2-HMAC-SHA256 公開テストベクター (c=4096)")]
    public void PublishedVector()
    {
        var actual = new byte[32];
        KdfParameters.Pbkdf2HmacSha256("password"u8, "salt"u8, 4096, actual);
        Assert.Equal("C5E478D59288C841AA530DB6845C4C8D962893A001CE4E11A4963873AA98134A", Convert.ToHexString(actual));
    }

    [Theory(DisplayName = "異常な KDF パラメータは拒否（細工されたファイル対策）")]
    [InlineData(KdfParameters.Pbkdf2Sha256, 1, 32)]
    [InlineData(KdfParameters.Pbkdf2Sha256, 100_000_000, 32)]
    [InlineData(KdfParameters.Pbkdf2Sha256, 600_000, 4)]
    [InlineData("md5", 600_000, 32)]
    public void RejectsBadParameters(string alg, int iterations, int saltLength)
        => Assert.ThrowsAny<Exception>(() => new KdfParameters(alg, iterations, new byte[saltLength]));
}

public class SecurityPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "ロック解除の連続失敗で待機時間が段階的に増加（最大60秒）")]
    public void UnlockThrottleBackoff()
    {
        var t = new UnlockThrottle();
        Assert.Equal(TimeSpan.Zero, t.RegisterFailure(T0));
        Assert.Equal(TimeSpan.Zero, t.RegisterFailure(T0));
        Assert.Equal(TimeSpan.FromSeconds(2), t.RegisterFailure(T0));
        Assert.Equal(TimeSpan.FromSeconds(2), t.GetRemaining(T0));
        Assert.Equal(TimeSpan.FromSeconds(1), t.GetRemaining(T0.AddSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(4), t.RegisterFailure(T0));
        Assert.Equal(TimeSpan.FromSeconds(8), t.RegisterFailure(T0));
        for (int i = 0; i < 10; i++) t.RegisterFailure(T0);
        Assert.Equal(TimeSpan.FromSeconds(60), t.RegisterFailure(T0));
        t.Reset();
        Assert.Equal(0, t.FailedAttempts);
        Assert.Equal(TimeSpan.Zero, t.GetRemaining(T0));
    }

    [Fact(DisplayName = "自動ロック: 無操作時間が設定値に達したらロック")]
    public void InactivityTimeout()
    {
        var tracker = new InactivityTracker(TimeSpan.FromMinutes(5));
        tracker.RecordActivity(T0);
        Assert.False(tracker.IsExpired(T0.AddMinutes(4.9)));
        Assert.True(tracker.IsExpired(T0.AddMinutes(5)));
        tracker.RecordActivity(T0.AddMinutes(4));
        Assert.False(tracker.IsExpired(T0.AddMinutes(5)));
    }

    [Fact(DisplayName = "自動ロック: 無効設定 (0) ではロックしない")]
    public void InactivityDisabled()
    {
        var tracker = new InactivityTracker(TimeSpan.Zero);
        tracker.RecordActivity(T0);
        Assert.False(tracker.IsExpired(T0.AddDays(1)));
    }

    [Fact(DisplayName = "クリップボード: 時間経過後、内容が変わっていなければ消去")]
    public void ClipboardClearsOwnContent()
    {
        var c = new ClipboardClearTracker();
        c.RecordCopy("123456", 10, T0, TimeSpan.FromSeconds(20));
        Assert.Equal(ClipboardClearAction.None, c.Evaluate(10, () => "123456", T0.AddSeconds(19)));
        Assert.Equal(ClipboardClearAction.Clear, c.Evaluate(10, () => "123456", T0.AddSeconds(20)));
        Assert.False(c.IsTracking);
        Assert.Equal(ClipboardClearAction.None, c.Evaluate(10, () => "123456", T0.AddSeconds(30)));
    }

    [Fact(DisplayName = "クリップボード: 他のアプリが上書きした内容は消去しない")]
    public void ClipboardKeepsOtherAppsContent()
    {
        var c = new ClipboardClearTracker();
        c.RecordCopy("123456", 10, T0, TimeSpan.FromSeconds(20));
        Assert.Equal(ClipboardClearAction.Abandon, c.Evaluate(11, () => "ユーザーがコピーした文章", T0.AddSeconds(20)));
        Assert.False(c.IsTracking);
    }

    [Fact(DisplayName = "クリップボード: 更新番号が変わっても内容が同じコードなら消去")]
    public void ClipboardClearsWhenSequenceChangedButSameText()
    {
        var c = new ClipboardClearTracker();
        c.RecordCopy("123456", 10, T0, TimeSpan.FromSeconds(20));
        Assert.Equal(ClipboardClearAction.Clear, c.Evaluate(12, () => "123456", T0.AddSeconds(25)));
    }

    [Fact(DisplayName = "クリップボード: ロック時は時間前でも即時消去")]
    public void ClipboardForceOnLock()
    {
        var c = new ClipboardClearTracker();
        c.RecordCopy("123456", 10, T0, TimeSpan.FromSeconds(20));
        Assert.Equal(ClipboardClearAction.Clear, c.Evaluate(10, () => "123456", T0.AddSeconds(1), force: true));
    }

    [Theory(DisplayName = "パスワード強度の推定")]
    [InlineData("", PasswordStrengthLevel.Empty)]
    [InlineData("abc", PasswordStrengthLevel.VeryWeak)]
    [InlineData("aaaaaaaaaaaa", PasswordStrengthLevel.Weak)]
    [InlineData("correcthorse", PasswordStrengthLevel.Fair)]
    [InlineData("Tr0ub4dor&3xK!9z", PasswordStrengthLevel.VeryStrong)]
    public void PasswordStrengthLevels(string pw, PasswordStrengthLevel expected)
        => Assert.Equal(expected, PasswordStrength.Evaluate(pw));
}
