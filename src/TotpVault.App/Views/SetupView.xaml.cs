using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TotpVault.App.Services;
using TotpVault.App.ViewModels;

namespace TotpVault.App.Views;

public partial class SetupView : UserControl
{
    public SetupView()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordBox.Focus();
        PreviewKeyDown += (_, _) => UpdateCapsLock();
        Unloaded += (_, _) =>
        {
            PasswordBox.Clear();
            ConfirmBox.Clear();
        };
        UpdateCapsLock();
    }

    private SetupViewModel? ViewModel => DataContext as SetupViewModel;

    private void UpdateCapsLock()
        => CapsLockText.Visibility = Keyboard.IsKeyToggled(Key.CapsLock) ? Visibility.Visible : Visibility.Collapsed;

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
        => Meter.SetLevel(SecurePassword.Strength(PasswordBox.SecurePassword));

    private void OnConfirmKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnCreate(sender, e);
    }

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.CreateAsync(PasswordBox.SecurePassword, ConfirmBox.SecurePassword);
    }
}
