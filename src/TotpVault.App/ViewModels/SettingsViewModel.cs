using System.Diagnostics;
using System.Reflection;
using System.Windows.Input;
using TotpVault.App.Mvvm;
using TotpVault.App.Services;
using TotpVault.Core.Security;

namespace TotpVault.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly AppSettings _settings;

    public SettingsViewModel(MainViewModel main, AppSettings settings)
    {
        _main = main;
        _settings = settings;
        ChangePasswordCommand = new RelayCommand(_main.ChangeMasterPassword);
        ExportBackupCommand = new RelayCommand(() =>
        {
            _main.ExportBackup();
            OnPropertyChanged(nameof(LastBackupText));
            OnPropertyChanged(nameof(BackupRecommended));
        });
        ImportBackupCommand = new RelayCommand(_main.ImportBackup);
        OpenDataFolderCommand = new RelayCommand(() =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDirectory}\"") { UseShellExecute = true }));
        ResetVaultCommand = new RelayCommand(_main.ConfirmAndResetVault);
    }

    public IReadOnlyList<Option<int>> AutoLockOptions { get; } =
    [
        new(1, "1分"), new(3, "3分"), new(5, "5分"), new(10, "10分"), new(15, "15分"), new(30, "30分"), new(0, "自動ロックしない"),
    ];

    public IReadOnlyList<Option<int>> ClipboardOptions { get; } =
    [
        new(10, "10秒後"), new(20, "20秒後"), new(30, "30秒後"), new(60, "60秒後"), new(0, "消去しない"),
    ];

    public ICommand ChangePasswordCommand { get; }
    public ICommand ExportBackupCommand { get; }
    public ICommand ImportBackupCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand ResetVaultCommand { get; }

    public int AutoLockMinutes
    {
        get => _settings.AutoLockMinutes;
        set
        {
            _settings.AutoLockMinutes = value;
            OnPropertyChanged();
            _main.ApplySettings();
        }
    }

    public int ClipboardClearSeconds
    {
        get => _settings.ClipboardClearSeconds;
        set
        {
            _settings.ClipboardClearSeconds = value;
            OnPropertyChanged();
        }
    }

    public bool LockOnSessionLock
    {
        get => _settings.LockOnSessionLock;
        set
        {
            _settings.LockOnSessionLock = value;
            OnPropertyChanged();
        }
    }

    public bool LockOnMinimize
    {
        get => _settings.LockOnMinimize;
        set
        {
            _settings.LockOnMinimize = value;
            OnPropertyChanged();
        }
    }

    public bool HideFromScreenCapture
    {
        get => _settings.HideFromScreenCapture;
        set
        {
            _settings.HideFromScreenCapture = value;
            OnPropertyChanged();
            _main.ApplySettings();
        }
    }

    public string LastBackupText => _settings.LastBackup is { } t ? t.ToLocalTime().ToString("yyyy/MM/dd HH:mm") : "未作成";

    public bool BackupRecommended => _settings.LastBackup is not { } t || DateTimeOffset.Now - t > TimeSpan.FromDays(30);

    public string DataDirectory => AppPaths.DataDirectory;

    public string KdfText => $"PBKDF2-HMAC-SHA256（{KdfParameters.DefaultIterations:N0} 回）";

    public string VersionText
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return $"TOTP Vault バージョン {version?.ToString(3) ?? "1.0.0"} ・ .NET {Environment.Version.ToString(2)}";
        }
    }
}
