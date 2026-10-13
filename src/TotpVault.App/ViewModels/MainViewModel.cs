using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TotpVault.App.Mvvm;
using TotpVault.App.Services;
using TotpVault.App.Views;
using TotpVault.Core.Logging;
using TotpVault.Core.Security;
using TotpVault.Core.Storage;

namespace TotpVault.App.ViewModels;

/// <summary>
/// アプリ全体の状態（初回設定 / ロック中 / ロック解除中）と、自動ロック・クリップボード消去・バックアップを管理する。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _toastTimer;
    private readonly InactivityTracker _inactivity = new(TimeSpan.FromMinutes(5));
    private object? _currentView;
    private string? _toastMessage;
    private bool _isToastVisible;
    private bool _isBusy;
    private string _busyText = "";

    public MainViewModel(ILocalKeyProtector protector)
    {
        Protector = protector;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTick, Dispatcher.CurrentDispatcher);
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };
    }

    public ILocalKeyProtector Protector { get; }
    public VaultStore? Store { get; private set; }
    public AppSettings? Settings { get; private set; }
    public ClipboardService Clipboard { get; } = new();
    public UnlockThrottle Throttle { get; } = new();

    public object? CurrentView
    {
        get => _currentView;
        private set
        {
            var old = _currentView;
            if (SetProperty(ref _currentView, value) && old is IDisposable d) d.Dispose();
        }
    }

    public VaultViewModel? Vault => CurrentView as VaultViewModel;

    public bool IsUnlocked => Store?.IsUnlocked == true;

    public string? ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    // ───────────────────────────── 起動・状態遷移 ─────────────────────────────

    public void Start()
    {
        _timer.Start();
        if (!VaultStore.Exists(AppPaths.VaultFile))
        {
            CurrentView = new SetupViewModel(this);
            return;
        }

        try
        {
            Store = VaultStore.Open(AppPaths.VaultFile, Protector);
            Settings = new AppSettings(Store);
            ApplySettings();
            CurrentView = new LockViewModel(this);
        }
        catch (VaultCorruptedException ex)
        {
            Log.Error("Vault could not be opened at startup.", ex);
            HandleBrokenVault(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Log.Error("Vault file is not accessible.", ex);
            Ui.Error("保管庫ファイルを開けません", "ファイルが他のプログラムで使用中か、アクセス権がありません。\n" + ex.Message);
            Application.Current.Shutdown();
        }
    }

    private void HandleBrokenVault(string reason)
    {
        var keep = Ui.Confirm("保管庫を開けません",
            reason + "\n\n破損したファイルを別名で退避し、新しい保管庫を作成できます。バックアップファイルがあれば作成後に復元してください。",
            "退避して新規作成", DialogKind.Error, "終了");
        if (!keep)
        {
            Application.Current.Shutdown();
            return;
        }

        var backupName = AppPaths.VaultFile + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        File.Move(AppPaths.VaultFile, backupName);
        Log.Warn("Broken vault moved aside.");
        CurrentView = new SetupViewModel(this);
    }

    public void OnVaultCreated(VaultStore store)
    {
        Store = store;
        Settings = new AppSettings(store);
        ApplySettings();
        EnterVault();
        ShowToast("保管庫を作成しました");
    }

    public void OnUnlocked()
    {
        Throttle.Reset();
        ApplySettings();
        EnterVault();
    }

    private void EnterVault()
    {
        _inactivity.RecordActivity(DateTimeOffset.Now);
        CurrentView = new VaultViewModel(this, Store!, Settings!);
        OnPropertyChanged(nameof(IsUnlocked));
    }

    /// <summary>ロックする。開いているダイアログを閉じ、コピー中のコードを消去し、鍵をメモリから消去する。</summary>
    public void Lock(string reason)
    {
        if (Store is null || !Store.IsUnlocked) return;
        Log.Info($"Locking ({reason}).");
        Ui.CloseChildWindows();
        Clipboard.ClearNowIfOurs();
        CurrentView = null; // VaultViewModel を破棄（表示中のコードを消去）
        Store.Lock();
        CurrentView = new LockViewModel(this);
        OnPropertyChanged(nameof(IsUnlocked));
        IsToastVisible = false;
    }

    public void ConfirmAndResetVault()
    {
        if (!Ui.Confirm("保管庫の初期化",
                "登録されているすべてのアカウントが削除されます。この操作は元に戻せません。\n\n" +
                "初期化後、新しいマスターパスワードを設定してから「バックアップから復元」でデータを戻すことができます。",
                "初期化する", DialogKind.Danger, requiredText: "初期化"))
            return;

        Ui.CloseChildWindows();
        Clipboard.ClearNowIfOurs();
        CurrentView = null;
        Store?.Dispose();
        Store = null;
        Settings = null;
        VaultStore.DeleteFiles(AppPaths.VaultFile);
        Log.Warn("Vault reset by user.");
        Throttle.Reset();
        CurrentView = new SetupViewModel(this);
        OnPropertyChanged(nameof(IsUnlocked));
    }

    public void ApplySettings()
    {
        if (Settings is null) return;
        _inactivity.Timeout = TimeSpan.FromMinutes(Math.Max(0, Settings.AutoLockMinutes));
        WindowProtection.SetEnabled(Settings.HideFromScreenCapture);
        Vault?.RefreshStatus();
    }

    // ───────────────────────────── ウィンドウ・OSイベント ─────────────────────────────

    public void RecordActivity() => _inactivity.RecordActivity(DateTimeOffset.Now);

    public void OnSessionLocked()
    {
        if (Settings?.LockOnSessionLock != false) Lock("Windows session locked");
    }

    public void OnSystemSuspend()
    {
        if (Settings?.LockOnSessionLock != false) Lock("system suspend");
    }

    public void OnMinimized()
    {
        if (Settings?.LockOnMinimize == true) Lock("window minimized");
    }

    public void Shutdown()
    {
        _timer.Stop();
        Clipboard.ClearNowIfOurs();
        CurrentView = null;
        Store?.Dispose();
        Log.Info("Application exit.");
    }

    private void OnTick(object? sender, EventArgs e)
    {
        Clipboard.Tick();
        if (IsUnlocked && _inactivity.IsExpired(DateTimeOffset.Now)) Lock("inactivity timeout");
    }

    // ───────────────────────────── 共通UI ─────────────────────────────

    public void ShowToast(string message)
    {
        ToastMessage = message;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public async Task<T> RunBusyAsync<T>(string text, Func<Task<T>> work)
    {
        BusyText = text;
        IsBusy = true;
        try
        {
            return await work();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ───────────────────────────── パスワード・バックアップ ─────────────────────────────

    public void ChangeMasterPassword()
    {
        if (Store is not { IsUnlocked: true } store) return;
        var dialog = new PasswordDialog(PasswordDialogMode.Change, "マスターパスワードの変更",
            "変更すると、データの暗号鍵も新しく生成され、すべてのアカウントが再暗号化されます。", "変更する")
        {
            Submit = async input =>
            {
                bool ok = await Task.Run(() => store.ChangePassword(input.Current, input.New));
                return ok ? null : "現在のマスターパスワードが正しくありません。";
            },
        };
        if (Ui.ShowDialog(dialog) == true) ShowToast("マスターパスワードを変更しました");
    }

    public void ExportBackup()
    {
        if (Store is not { IsUnlocked: true } store) return;

        var save = new SaveFileDialog
        {
            Title = "バックアップの保存先",
            Filter = $"TOTP Vault バックアップ (*{BackupFile.Extension})|*{BackupFile.Extension}",
            FileName = $"TotpVault-backup-{DateTime.Now:yyyyMMdd}{BackupFile.Extension}",
            DefaultExt = BackupFile.Extension,
            AddExtension = true,
        };
        if (!ShowFileDialog(save) || !store.IsUnlocked) return; // ファイル選択中に自動ロックされた場合は中断
        var path = save.FileName;

        var dialog = new PasswordDialog(PasswordDialogMode.Create, "バックアップ用パスワードの設定",
            "復元するときに必要です。忘れるとバックアップを復元できません。マスターパスワードと同じでも構いません。",
            "バックアップを作成")
        {
            Submit = async input =>
            {
                try
                {
                    await Task.Run(() =>
                    {
                        var accounts = store.ExportAccounts();
                        try { BackupFile.Write(path, accounts, input.New); }
                        finally { accounts.ForEach(a => a.ClearSecret()); }
                    });
                    return null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return "ファイルを保存できませんでした。保存先を確認してください。\n" + ex.Message;
                }
            },
        };

        if (Ui.ShowDialog(dialog) != true) return;
        if (Settings is not null) Settings.LastBackup = DateTimeOffset.Now;
        ShowToast("暗号化バックアップを作成しました");
    }

    public void ImportBackup()
    {
        if (Store is not { IsUnlocked: true } store) return;

        var open = new OpenFileDialog
        {
            Title = "バックアップファイルを選択",
            Filter = $"TOTP Vault バックアップ (*{BackupFile.Extension})|*{BackupFile.Extension}|すべてのファイル (*.*)|*.*",
        };
        if (!ShowFileDialog(open) || !store.IsUnlocked) return;
        var path = open.FileName;

        ImportResult? result = null;
        var dialog = new PasswordDialog(PasswordDialogMode.Enter, "バックアップから復元",
            $"「{Path.GetFileName(path)}」を作成したときのバックアップ用パスワードを入力してください。", "復元する")
        {
            Submit = async input =>
            {
                try
                {
                    result = await Task.Run(() =>
                    {
                        var accounts = BackupFile.Read(path, input.Current);
                        try { return store.ImportAccounts(accounts); }
                        finally { accounts.ForEach(a => a.ClearSecret()); }
                    });
                    return null;
                }
                catch (BackupDecryptionException ex)
                {
                    return ex.Message;
                }
                catch (BackupFormatException ex)
                {
                    return ex.Message;
                }
                catch (IOException ex)
                {
                    return "ファイルを読み込めませんでした。\n" + ex.Message;
                }
            },
        };

        if (Ui.ShowDialog(dialog) != true || result is null) return;
        Vault?.Reload();
        var message = $"{result.Added} 件のアカウントを追加しました。";
        if (result.SkippedDuplicates > 0) message += $"\n{result.SkippedDuplicates} 件は登録済みのためスキップしました。";
        Ui.Info("復元が完了しました", message, DialogKind.Success);
    }

    private static bool ShowFileDialog(CommonDialog dialog)
    {
        var owner = Ui.ActiveOwner();
        return (owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true;
    }
}
