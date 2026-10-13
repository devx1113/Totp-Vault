using System.Windows;
using System.Windows.Media;
using TotpVault.App.Services;

namespace TotpVault.App.Views;

public partial class MessageDialog : Window
{
    private readonly string? _requiredText;

    public MessageDialog(string title, string message, DialogKind kind, string confirmText, string? cancelText, string? requiredText)
    {
        InitializeComponent();
        _requiredText = requiredText;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;

        if (cancelText is null)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            ConfirmButton.IsCancel = true;
        }
        else
        {
            CancelButton.Content = cancelText;
        }

        var (glyph, fg, bg) = kind switch
        {
            DialogKind.Question => ("", "Brush.Accent", "Brush.AccentSoft"),
            DialogKind.Warning => ("", "Brush.Warning", "Brush.WarningSoft"),
            DialogKind.Danger => ("", "Brush.Danger", "Brush.DangerSoft"),
            DialogKind.Error => ("", "Brush.Danger", "Brush.DangerSoft"),
            DialogKind.Success => ("", "Brush.Success", "Brush.SuccessSoft"),
            _ => ("", "Brush.Accent", "Brush.AccentSoft"),
        };
        IconGlyph.Text = glyph;
        IconGlyph.Foreground = (Brush)FindResource(fg);
        IconBadge.Background = (Brush)FindResource(bg);

        if (kind == DialogKind.Danger)
            ConfirmButton.Style = (Style)FindResource("Button.Danger");

        if (!string.IsNullOrEmpty(requiredText))
        {
            TypingPanel.Visibility = Visibility.Visible;
            TypingLabel.Text = $"確認のため「{requiredText}」と入力してください";
            ConfirmButton.IsEnabled = false;
            ConfirmButton.IsDefault = false;
            Loaded += (_, _) => TypingBox.Focus();
        }
        else
        {
            Loaded += (_, _) => ConfirmButton.Focus();
        }
    }

    private void OnTypingChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => ConfirmButton.IsEnabled = string.Equals(TypingBox.Text.Trim(), _requiredText, StringComparison.Ordinal);

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
