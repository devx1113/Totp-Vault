using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TotpVault.App.ViewModels;

namespace TotpVault.App.Views;

public partial class LockView : UserControl
{
    private LockViewModel? _subscribed;

    public LockView()
    {
        InitializeComponent();
        Loaded += (_, _) => FocusPassword();
        PreviewKeyDown += (_, _) => UpdateCapsLock();
        DataContextChanged += (_, _) =>
        {
            if (_subscribed is not null) _subscribed.InputEnabled -= OnInputEnabled;
            _subscribed = DataContext as LockViewModel;
            if (_subscribed is not null) _subscribed.InputEnabled += OnInputEnabled;
        };
        Unloaded += (_, _) => PasswordBox.Clear();
        UpdateCapsLock();
    }

    private void OnInputEnabled(object? sender, EventArgs e)
    {
        PasswordBox.Clear();
        FocusPassword();
    }

    private void FocusPassword()
        => Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            PasswordBox.Focus();
            Keyboard.Focus(PasswordBox);
        });

    private void UpdateCapsLock()
        => CapsLockText.Visibility = Keyboard.IsKeyToggled(Key.CapsLock) ? Visibility.Visible : Visibility.Collapsed;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnUnlock(sender, e);
    }

    private async void OnUnlock(object sender, RoutedEventArgs e)
    {
        if (DataContext is LockViewModel vm) await vm.UnlockAsync(PasswordBox.SecurePassword);
    }
}
