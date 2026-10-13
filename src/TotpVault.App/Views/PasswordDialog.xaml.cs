using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TotpVault.App.Services;
using TotpVault.Core.Security;

namespace TotpVault.App.Views;

public enum PasswordDialogMode
{
    /// <summary>既存のパスワードを1回入力（バックアップの復元など）。</summary>
    Enter,
    /// <summary>新しいパスワードを設定（確認入力・強度表示あり）。</summary>
    Create,
    /// <summary>現在のパスワード＋新しいパスワード（マスターパスワード変更）。</summary>
    Change,
}

/// <summary>入力されたパスワード（UTF-8）。ダイアログ側で処理後に消去される。</summary>
public sealed record PasswordInput(byte[] Current, byte[] New);

public partial class PasswordDialog : Window, IForceClosable
{
    private readonly PasswordDialogMode _mode;
    private bool _busy;
    private bool _closed;
    private bool _forceClose;

    public PasswordDialog(PasswordDialogMode mode, string title, string description, string okText)
    {
        InitializeComponent();
        _mode = mode;
        TitleText.Text = title;
        DescriptionText.Text = description;
        DescriptionText.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;
        OkButton.Content = okText;

        switch (mode)
        {
            case PasswordDialogMode.Enter:
                CurrentLabel.Text = "パスワード";
                NewPanel.Visibility = Visibility.Collapsed;
                ConfirmPanel.Visibility = Visibility.Collapsed;
                break;
            case PasswordDialogMode.Create:
                CurrentPanel.Visibility = Visibility.Collapsed;
                NewLabel.Text = "パスワード（8文字以上）";
                break;
            case PasswordDialogMode.Change:
                NewLabel.Text = "新しいマスターパスワード（8文字以上）";
                break;
        }

        Loaded += (_, _) => (mode == PasswordDialogMode.Create ? NewBox : CurrentBox).Focus();
        PreviewKeyDown += (_, _) => UpdateCapsLock();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closed = true;
            CurrentBox.Clear();
            NewBox.Clear();
            ConfirmBox.Clear();
        };
        UpdateCapsLock();
    }

    /// <summary>OK 時に呼ばれる処理。エラーメッセージを返すとダイアログに表示して再入力を求める。null で成功。</summary>
    public Func<PasswordInput, Task<string?>>? Submit { get; init; }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_busy && !_forceClose) e.Cancel = true; // 処理中は閉じない
    }

    private void UpdateCapsLock()
        => CapsLockText.Visibility = Keyboard.IsKeyToggled(Key.CapsLock) ? Visibility.Visible : Visibility.Collapsed;

    private void OnAnyPasswordChanged(object sender, RoutedEventArgs e) => ErrorText.Visibility = Visibility.Collapsed;

    private void OnNewPasswordChanged(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        Meter.SetLevel(SecurePassword.Strength(NewBox.SecurePassword));
    }

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var error = Validate();
        if (error is not null)
        {
            ShowError(error);
            return;
        }

        var current = _mode == PasswordDialogMode.Create ? [] : SecurePassword.ToUtf8(CurrentBox.SecurePassword);
        var @new = _mode == PasswordDialogMode.Enter ? [] : SecurePassword.ToUtf8(NewBox.SecurePassword);
        SetBusy(true);
        try
        {
            var result = Submit is null ? null : await Submit(new PasswordInput(current, @new));
            if (_closed) return;
            if (result is null)
            {
                SetBusy(false); // 処理中フラグを解除してから閉じる（Closing でキャンセルされないように）
                DialogResult = true;
                return;
            }

            SetBusy(false); // 入力欄を有効に戻してからフォーカスする
            ShowError(result);
            CurrentBox.Clear();
            (_mode == PasswordDialogMode.Create ? NewBox : CurrentBox).Focus();
        }
        catch (Exception ex)
        {
            if (!_closed) ShowError(ex.Message);
        }
        finally
        {
            SecureMemory.Zero(current);
            SecureMemory.Zero(@new);
            if (!_closed) SetBusy(false);
        }
    }

    private string? Validate()
    {
        if (_mode != PasswordDialogMode.Create && CurrentBox.SecurePassword.Length == 0)
            return "パスワードを入力してください。";

        if (_mode == PasswordDialogMode.Enter) return null;

        if (NewBox.SecurePassword.Length < PasswordStrength.MinimumLength)
            return $"パスワードは{PasswordStrength.MinimumLength}文字以上で入力してください。";
        if (SecurePassword.Strength(NewBox.SecurePassword) < PasswordStrengthLevel.Fair)
            return "パスワードが単純すぎます。長くするか、英大文字・数字・記号を組み合わせてください。";
        if (!SecurePassword.AreEqual(NewBox.SecurePassword, ConfirmBox.SecurePassword))
            return "確認用のパスワードが一致しません。";
        return null;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        OkButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        foreach (var box in new Control[] { CurrentBox, NewBox, ConfirmBox }) box.IsEnabled = !busy;
    }
}
