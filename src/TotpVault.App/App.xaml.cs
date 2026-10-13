using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using TotpVault.App.Mvvm;
using TotpVault.App.Services;
using TotpVault.App.ViewModels;
using TotpVault.Core.Logging;
using TotpVault.Core.Storage;

namespace TotpVault.App;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.Initialize(e.Args);
        Log.Configure(new FileLogSink(AppPaths.LogDirectory));
        Log.Info($"Application start (v{typeof(App).Assembly.GetName().Version}, {Environment.OSVersion}).");

        // 同じデータフォルダを使う二重起動を防止
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.DataDirectory.ToUpperInvariant())))[..16];
        _singleInstance = new Mutex(true, $@"Local\TotpVault.{key}", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("TOTP Vault は既に起動しています。", "TOTP Vault", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
        AsyncRelayCommand.ErrorHandler = ShowError;

        var vm = new MainViewModel(new DpapiKeyProtector());
        var window = new MainWindow(vm);
        MainWindow = window;
        window.Show();
        vm.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowError(e.Exception);
    }

    private static void ShowError(Exception ex)
    {
        // ロック処理と競合した操作は静かに中断する
        if (ex is VaultLockedException or ObjectDisposedException) return;
        Log.Error("Operation failed.", ex);
        try
        {
            Ui.Error("エラーが発生しました", ex is ArgumentException or VaultCorruptedException or InvalidOperationException
                ? ex.Message
                : "処理を完了できませんでした。\n" + ex.Message);
        }
        catch
        {
            MessageBox.Show(ex.Message, "TOTP Vault", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
