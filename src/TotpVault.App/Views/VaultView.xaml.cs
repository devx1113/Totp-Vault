using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using TotpVault.App.ViewModels;
using TotpVault.Core.Otp;

namespace TotpVault.App.Views;

public partial class VaultView : UserControl
{
    public VaultView()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Focus());
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private VaultViewModel? ViewModel => DataContext as VaultViewModel;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        if (e.Key == Key.F && ViewModel?.HasAccounts == true)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.V)
        {
            // 検索欄にテキストを貼り付ける場合はそのまま。画像や otpauth URI の場合は登録処理へ
            bool inSearch = SearchBox.IsKeyboardFocusWithin;
            bool isOtpText = Clipboard.ContainsText() && OtpAuthParser.LooksLikeOtpAuth(Clipboard.GetText());
            bool hasImage = Clipboard.ContainsImage() || Clipboard.ContainsFileDropList();
            if (!inSearch || isOtpText || hasImage)
            {
                ViewModel?.AddFromClipboardCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || ViewModel is null) return;
        ViewModel.SearchText = "";
        Focus();
        e.Handled = true;
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => OpenMenu((Button)sender, ((Button)sender).ContextMenu);

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (button.Tag is FrameworkElement card) OpenMenu(button, card.ContextMenu);
    }

    private static void OpenMenu(FrameworkElement target, ContextMenu? menu)
    {
        if (menu is null) return;
        menu.DataContext = target.DataContext;
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
