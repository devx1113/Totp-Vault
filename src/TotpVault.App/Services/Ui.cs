using System.Windows;
using TotpVault.App.Views;

namespace TotpVault.App.Services;

public enum DialogKind
{
    Info,
    Question,
    Warning,
    Danger,
    Error,
    Success,
}

/// <summary>ダイアログ表示の共通処理。</summary>
public static class Ui
{
    public static Window? ActiveOwner()
    {
        var app = Application.Current;
        if (app is null) return null;
        var active = app.Windows.OfType<Window>().LastOrDefault(w => w.IsActive && w.IsVisible);
        return active ?? (app.MainWindow is { IsVisible: true } main ? main : null);
    }

    public static bool? ShowDialog(Window dialog)
    {
        var owner = ActiveOwner();
        if (owner is not null && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        WindowProtection.Attach(dialog);
        return dialog.ShowDialog();
    }

    public static bool Confirm(string title, string message, string confirmText = "OK",
        DialogKind kind = DialogKind.Question, string cancelText = "キャンセル", string? requiredText = null)
        => ShowDialog(new MessageDialog(title, message, kind, confirmText, cancelText, requiredText)) == true;

    public static void Info(string title, string message, DialogKind kind = DialogKind.Info)
        => ShowDialog(new MessageDialog(title, message, kind, "OK", null, null));

    public static void Error(string title, string message) => Info(title, message, DialogKind.Error);

    /// <summary>ロック時に、開いているダイアログ（編集画面・設定画面など）をすべて閉じる。</summary>
    public static void CloseChildWindows()
    {
        var app = Application.Current;
        if (app is null) return;
        foreach (var w in app.Windows.OfType<Window>().Where(w => !ReferenceEquals(w, app.MainWindow)).ToList())
        {
            try
            {
                if (w is IForceClosable fc) fc.ForceClose();
                else w.Close();
            }
            catch (InvalidOperationException)
            {
                // 既に閉じている
            }
        }
    }
}

/// <summary>処理中でも強制的に閉じられるダイアログ。</summary>
public interface IForceClosable
{
    void ForceClose();
}
