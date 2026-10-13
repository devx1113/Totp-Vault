using System.Windows;
using TotpVault.App.ViewModels;

namespace TotpVault.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
