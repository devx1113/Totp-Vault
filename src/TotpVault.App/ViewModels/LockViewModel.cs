using System.Security;
using System.Windows.Input;
using System.Windows.Threading;
using TotpVault.App.Mvvm;
using TotpVault.App.Services;
using TotpVault.Core.Logging;
using TotpVault.Core.Security;
using TotpVault.Core.Storage;

namespace TotpVault.App.ViewModels;

/// <summary>ロック画面。連続失敗時は待機時間を段階的に延ばす。</summary>
public sealed class LockViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _countdown;
    private string? _errorMessage;
    private bool _isBusy;
    private string? _lastFailureMessage;

    public LockViewModel(MainViewModel main)
    {
        _main = main;
        _countdown = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _countdown.Tick += (_, _) => UpdateThrottle();
        ForgotPasswordCommand = new RelayCommand(ShowForgotPassword);
        if (_main.Throttle.GetRemaining(DateTimeOffset.Now) > TimeSpan.Zero) _countdown.Start();
        UpdateThrottle();
    }

    public event EventHandler? InputEnabled;

    public ICommand ForgotPasswordCommand { get; }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(CanInput));
        }
    }

    public bool IsThrottled => _main.Throttle.GetRemaining(DateTimeOffset.Now) > TimeSpan.Zero;

    public bool CanInput => !IsBusy && !IsThrottled;

    public async Task UnlockAsync(SecureString password)
    {
        if (!CanInput || _main.Store is null) return;
        if (password.Length == 0)
        {
            ErrorMessage = "マスターパスワードを入力してください。";
            return;
        }

        var store = _main.Store;
        var bytes = SecurePassword.ToUtf8(password);
        IsBusy = true;
        ErrorMessage = null;
        bool unlocked = false;
        try
        {
            unlocked = await Task.Run(() => store.TryUnlock(bytes));
            if (!unlocked)
            {
                var delay = _main.Throttle.RegisterFailure(DateTimeOffset.Now);
                _lastFailureMessage = $"パスワードが正しくありません（{_main.Throttle.FailedAttempts}回失敗）。";
                ErrorMessage = _lastFailureMessage;
                if (delay > TimeSpan.Zero) _countdown.Start();
            }
        }
        catch (Exception ex) when (ex is VaultAccessDeniedException or VaultCorruptedException)
        {
            Log.Error("Unlock failed.", ex);
            ErrorMessage = ex.Message;
        }
        finally
        {
            SecureMemory.Zero(bytes);
            IsBusy = false;
            UpdateThrottle();
        }

        if (unlocked) _main.OnUnlocked();
        else if (CanInput) InputEnabled?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateThrottle()
    {
        var remaining = _main.Throttle.GetRemaining(DateTimeOffset.Now);
        if (remaining > TimeSpan.Zero)
        {
            ErrorMessage = (_lastFailureMessage ?? "パスワードの入力に連続して失敗しました。") +
                           $"\n{Math.Ceiling(remaining.TotalSeconds):0} 秒後に再試行できます。";
        }
        else if (_countdown.IsEnabled)
        {
            _countdown.Stop();
            ErrorMessage = _lastFailureMessage;
            InputEnabled?.Invoke(this, EventArgs.Empty);
        }
        OnPropertyChanged(nameof(IsThrottled));
        OnPropertyChanged(nameof(CanInput));
    }

    private void ShowForgotPassword()
    {
        var reset = Ui.Confirm("マスターパスワードを忘れた場合",
            "安全のため、マスターパスワードはどこにも保存されておらず、開発者を含め誰も解除や再発行はできません。\n\n" +
            "■ バックアップファイルがある場合\n保管庫を初期化し、新しいマスターパスワードを設定した後、「バックアップから復元」でデータを戻せます（バックアップ用パスワードが必要です）。\n\n" +
            "■ バックアップがない場合\n各サービスのアカウント設定画面で、リカバリーコード等を使って2段階認証を再設定してください。",
            "保管庫を初期化…", DialogKind.Warning, "閉じる");
        if (reset) _main.ConfirmAndResetVault();
    }

    public void Dispose() => _countdown.Stop();
}
