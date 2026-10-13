using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using TotpVault.App.ViewModels;

namespace TotpVault.App.Views;

public partial class AccountEditorWindow : Window
{
    private readonly AccountEditorViewModel _vm;

    public AccountEditorWindow(AccountEditorViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => _vm.PropertyChanged -= OnViewModelPropertyChanged;
        Loaded += (_, _) =>
        {
            if (_vm.ShowSecretInput && _vm.Secret is null && SecretBox.Text.Length == 0 && IssuerBox.Text.Length > 0) SecretBox.Focus();
            else IssuerBox.Focus();
        };
        UpdatePreviewStyle();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountEditorViewModel.HasPreview)) UpdatePreviewStyle();
    }

    /// <summary>コード表示時は大きな等幅フォント、未入力時は案内文。</summary>
    private void UpdatePreviewStyle()
    {
        if (_vm.HasPreview)
        {
            PreviewText.FontFamily = (FontFamily)FindResource("Font.Mono");
            PreviewText.FontSize = 26;
            PreviewText.FontWeight = FontWeights.SemiBold;
            PreviewText.Foreground = (Brush)FindResource("Brush.Accent");
        }
        else
        {
            PreviewText.FontFamily = (FontFamily)FindResource("Font.UI");
            PreviewText.FontSize = 13;
            PreviewText.FontWeight = FontWeights.Normal;
            PreviewText.Foreground = (Brush)FindResource("Brush.TextMuted");
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_vm.CanSave) DialogResult = true;
    }
}
