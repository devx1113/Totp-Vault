using System.Security;
using TotpVault.App.Mvvm;
using TotpVault.App.Services;
using TotpVault.Core.Logging;
using TotpVault.Core.Security;
using TotpVault.Core.Storage;

namespace TotpVault.App.ViewModels;

/// <summary>初回起動時のマスターパスワード設定。</summary>
public sealed class SetupViewModel(MainViewModel main) : ObservableObject
{
    private string? _errorMessage;
    private bool _isBusy;
    private bool _acknowledged;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool Acknowledged
    {
        get => _acknowledged;
        set => SetProperty(ref _acknowledged, value);
    }

    public async Task CreateAsync(SecureString password, SecureString confirm)
    {
        if (IsBusy) return;
        ErrorMessage = null;

        if (password.Length < PasswordStrength.MinimumLength)
        {
            ErrorMessage = $"マスターパスワードは{PasswordStrength.MinimumLength}文字以上で入力してください。";
            return;
        }
        if (SecurePassword.Strength(password) < PasswordStrengthLevel.Fair)
        {
            ErrorMessage = "パスワードが単純すぎます。長くするか、英大文字・数字・記号を組み合わせてください。";
            return;
        }
        if (!SecurePassword.AreEqual(password, confirm))
        {
            ErrorMessage = "確認用のパスワードが一致しません。";
            return;
        }
        if (!Acknowledged)
        {
            ErrorMessage = "注意事項を確認し、チェックを入れてください。";
            return;
        }

        var bytes = SecurePassword.ToUtf8(password);
        IsBusy = true;
        try
        {
            var store = await Task.Run(() => VaultStore.Create(AppPaths.VaultFile, bytes, main.Protector));
            main.OnVaultCreated(store);
        }
        catch (Exception ex)
        {
            Log.Error("Vault creation failed.", ex);
            ErrorMessage = "保管庫を作成できませんでした。\n" + ex.Message;
        }
        finally
        {
            SecureMemory.Zero(bytes);
            IsBusy = false;
        }
    }
}
