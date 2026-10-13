using System.Globalization;
using TotpVault.Core.Storage;

namespace TotpVault.App.Services;

/// <summary>アプリ設定（非機密）。保管庫ファイルの meta テーブルに保存する。</summary>
public sealed class AppSettings(VaultStore store)
{
    public int AutoLockMinutes
    {
        get => GetInt("autolock_minutes", 5);
        set => SetInt("autolock_minutes", value);
    }

    public int ClipboardClearSeconds
    {
        get => GetInt("clipboard_clear_seconds", 20);
        set => SetInt("clipboard_clear_seconds", value);
    }

    public bool LockOnMinimize
    {
        get => GetBool("lock_on_minimize", false);
        set => SetBool("lock_on_minimize", value);
    }

    public bool LockOnSessionLock
    {
        get => GetBool("lock_on_session_lock", true);
        set => SetBool("lock_on_session_lock", value);
    }

    public bool HideFromScreenCapture
    {
        get => GetBool("hide_from_capture", false);
        set => SetBool("hide_from_capture", value);
    }

    public string SortMode
    {
        get => store.GetSetting("sort_mode") ?? "custom";
        set => store.SetSetting("sort_mode", value);
    }

    public DateTimeOffset? LastBackup
    {
        get => DateTimeOffset.TryParse(store.GetSetting("last_backup"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : null;
        set => store.SetSetting("last_backup", value?.ToString("O", CultureInfo.InvariantCulture) ?? "");
    }

    private int GetInt(string key, int fallback)
        => int.TryParse(store.GetSetting(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private void SetInt(string key, int value) => store.SetSetting(key, value.ToString(CultureInfo.InvariantCulture));

    private bool GetBool(string key, bool fallback) => store.GetSetting(key) switch
    {
        "1" => true,
        "0" => false,
        _ => fallback,
    };

    private void SetBool(string key, bool value) => store.SetSetting(key, value ? "1" : "0");
}
