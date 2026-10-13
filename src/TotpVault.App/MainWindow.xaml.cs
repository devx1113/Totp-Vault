using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TotpVault.App.Services;
using TotpVault.App.ViewModels;

namespace TotpVault.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private NativeMethods.POINT _lastCursor;
    private IntPtr _hwnd;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        InputManager.Current.PreProcessInput += OnPreProcessInput;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) _vm.OnMinimized();
        };
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => DropOverlay.Visibility = Visibility.Collapsed;
        Drop += OnDrop;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        // Windows のロック（Win+L）を検知するため、セッション変更通知を登録
        NativeMethods.WTSRegisterSessionNotification(_hwnd, NativeMethods.NOTIFY_FOR_THIS_SESSION);
        WindowProtection.Attach(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_hwnd != IntPtr.Zero) NativeMethods.WTSUnRegisterSessionNotification(_hwnd);
        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        _vm.Shutdown();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_WTSSESSION_CHANGE && wParam.ToInt32() == NativeMethods.WTS_SESSION_LOCK)
            _vm.OnSessionLocked();
        else if (msg == NativeMethods.WM_POWERBROADCAST && wParam.ToInt32() == NativeMethods.PBT_APMSUSPEND)
            _vm.OnSystemSuspend();
        return IntPtr.Zero;
    }

    /// <summary>アプリ内のキーボード・マウス操作を自動ロック用に記録する（全ウィンドウ共通）。</summary>
    private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        var input = e.StagingItem.Input;
        if (input is KeyEventArgs or MouseButtonEventArgs or MouseWheelEventArgs)
        {
            _vm.RecordActivity();
        }
        else if (input is MouseEventArgs && input.RoutedEvent == Mouse.PreviewMouseMoveEvent)
        {
            // レイアウト変更による擬似的な MouseMove を除外するため、実際にカーソルが動いた場合のみ記録
            if (NativeMethods.GetCursorPos(out var p) && (p.X != _lastCursor.X || p.Y != _lastCursor.Y))
            {
                _lastCursor = p;
                _vm.RecordActivity();
            }
        }
    }

    private static List<string> GetImageFiles(DragEventArgs e)
        => e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(f => File.Exists(f) && QrImageReader.IsImageFile(f)).ToList()
            : [];

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool accept = _vm.Vault is not null && GetImageFiles(e).Count > 0;
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = accept ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        var files = GetImageFiles(e);
        if (_vm.Vault is not { } vault || files.Count == 0) return;
        try
        {
            await vault.ImportImageFilesAsync(files);
        }
        catch (Exception ex)
        {
            Mvvm.AsyncRelayCommand.ReportError(ex);
        }
    }
}
