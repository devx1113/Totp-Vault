namespace TotpVault.Core.Security;

/// <summary>ロック解除の連続失敗に対して待機時間を段階的に延ばす（オンライン総当たり対策）。</summary>
public sealed class UnlockThrottle
{
    public const int FreeAttempts = 3;
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    private DateTimeOffset _lockedUntil = DateTimeOffset.MinValue;

    public int FailedAttempts { get; private set; }

    public TimeSpan RegisterFailure(DateTimeOffset now)
    {
        FailedAttempts++;
        if (FailedAttempts < FreeAttempts) return TimeSpan.Zero;

        // 3回目: 2秒, 4回目: 4秒, 5回目: 8秒 ... 最大60秒
        int exponent = Math.Min(FailedAttempts - FreeAttempts + 1, 10);
        var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, exponent), MaxDelay.TotalSeconds));
        _lockedUntil = now + delay;
        return delay;
    }

    public TimeSpan GetRemaining(DateTimeOffset now)
        => _lockedUntil > now ? _lockedUntil - now : TimeSpan.Zero;

    public void Reset()
    {
        FailedAttempts = 0;
        _lockedUntil = DateTimeOffset.MinValue;
    }
}

/// <summary>一定時間操作がない場合の自動ロック判定。Timeout が Zero の場合は無効。</summary>
public sealed class InactivityTracker(TimeSpan timeout)
{
    private DateTimeOffset _lastActivity = DateTimeOffset.MinValue;

    public TimeSpan Timeout { get; set; } = timeout;

    public void RecordActivity(DateTimeOffset now) => _lastActivity = now;

    public bool IsExpired(DateTimeOffset now)
        => Timeout > TimeSpan.Zero && _lastActivity != DateTimeOffset.MinValue && now - _lastActivity >= Timeout;

    public TimeSpan GetRemaining(DateTimeOffset now)
    {
        if (Timeout <= TimeSpan.Zero) return TimeSpan.MaxValue;
        var remaining = Timeout - (now - _lastActivity);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}

public enum ClipboardClearAction
{
    /// <summary>何もしない（未コピー、または消去時刻前）。</summary>
    None,
    /// <summary>クリップボードを消去する。</summary>
    Clear,
    /// <summary>他のアプリが上書きしたため、消去せずに追跡を終了する。</summary>
    Abandon,
}

/// <summary>
/// コピーした認証コードの自動消去判定。
/// 消去時刻になった時点で、クリップボードがまだ本アプリのコピー内容のままである場合のみ消去する。
/// 他のアプリが別のデータをコピーしていた場合は、そのデータを誤って消さない。
/// </summary>
public sealed class ClipboardClearTracker
{
    private uint _sequenceAtCopy;
    private string? _copiedText;
    private DateTimeOffset _clearAt;

    public bool IsTracking => _copiedText is not null;

    public void RecordCopy(string text, uint clipboardSequence, DateTimeOffset now, TimeSpan clearAfter)
    {
        _copiedText = text;
        _sequenceAtCopy = clipboardSequence;
        _clearAt = now + clearAfter;
    }

    /// <param name="currentSequence">現在のクリップボード更新番号 (GetClipboardSequenceNumber)。</param>
    /// <param name="readCurrentText">更新番号が変わっていた場合のみ呼ばれる、現在のテキスト取得関数。</param>
    /// <param name="force">ロック時など、時刻に関係なく即時判定する場合 true。</param>
    public ClipboardClearAction Evaluate(uint currentSequence, Func<string?> readCurrentText, DateTimeOffset now, bool force = false)
    {
        if (_copiedText is null) return ClipboardClearAction.None;
        if (!force && now < _clearAt) return ClipboardClearAction.None;

        bool stillOurs = currentSequence == _sequenceAtCopy
            || string.Equals(readCurrentText(), _copiedText, StringComparison.Ordinal);

        _copiedText = null;
        return stillOurs ? ClipboardClearAction.Clear : ClipboardClearAction.Abandon;
    }

    public void Reset() => _copiedText = null;
}

public enum PasswordStrengthLevel
{
    Empty = 0,
    VeryWeak = 1,
    Weak = 2,
    Fair = 3,
    Strong = 4,
    VeryStrong = 5,
}

/// <summary>パスワード強度の簡易推定（文字種と長さによるエントロピー概算）。</summary>
public static class PasswordStrength
{
    public const int MinimumLength = 8;

    public static PasswordStrengthLevel Evaluate(ReadOnlySpan<char> password)
    {
        if (password.IsEmpty) return PasswordStrengthLevel.Empty;

        bool lower = false, upper = false, digit = false, symbol = false, other = false;
        int distinct = 0;
        Span<bool> seen = stackalloc bool[128];
        foreach (char c in password)
        {
            if (c < 128)
            {
                if (!seen[c]) { seen[c] = true; distinct++; }
                if (char.IsAsciiLetterLower(c)) lower = true;
                else if (char.IsAsciiLetterUpper(c)) upper = true;
                else if (char.IsAsciiDigit(c)) digit = true;
                else symbol = true;
            }
            else
            {
                other = true;
                distinct++;
            }
        }

        int pool = (lower ? 26 : 0) + (upper ? 26 : 0) + (digit ? 10 : 0) + (symbol ? 33 : 0) + (other ? 100 : 0);
        // 同じ文字の繰り返しは実効長を下げる
        double effectiveLength = Math.Min(password.Length, distinct * 1.5 + 1);
        double bits = effectiveLength * Math.Log2(Math.Max(pool, 2));

        if (password.Length < MinimumLength) return bits < 28 ? PasswordStrengthLevel.VeryWeak : PasswordStrengthLevel.Weak;
        return bits switch
        {
            < 36 => PasswordStrengthLevel.Weak,
            < 60 => PasswordStrengthLevel.Fair,
            < 80 => PasswordStrengthLevel.Strong,
            _ => PasswordStrengthLevel.VeryStrong,
        };
    }
}
