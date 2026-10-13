using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using TotpVault.App.Mvvm;
using TotpVault.Core.Otp;
using TotpVault.Core.Security;
using TotpVault.Core.Storage;

namespace TotpVault.App.ViewModels;

/// <summary>アカウントの追加・編集。入力中のシークレットはピン留めバッファに保持し、閉じるときに消去する。</summary>
public sealed class AccountEditorViewModel : ObservableObject, IDisposable
{
    private readonly AccountInfo? _original;
    private readonly Func<AccountInfo, long, string>? _storedCodeGenerator;
    private readonly DispatcherTimer _previewTimer;
    private byte[]? _secret;
    private string _issuer = "";
    private string _accountName = "";
    private string _secretText = "";
    private string _periodText = Totp.DefaultPeriod.ToString(CultureInfo.InvariantCulture);
    private OtpAlgorithm _algorithm = OtpAlgorithm.Sha1;
    private int _digits = Totp.DefaultDigits;
    private bool _showAdvanced;
    private bool _isSecretFromQr;
    private string? _secretError;
    private string? _periodError;
    private string _previewCode = "";
    private double _previewProgress;
    private int _previewSeconds;
    private bool _suppressSecretParsing;

    private AccountEditorViewModel(AccountInfo? original, Func<AccountInfo, long, string>? storedCodeGenerator)
    {
        _original = original;
        _storedCodeGenerator = storedCodeGenerator;
        ClearQrSecretCommand = new RelayCommand(() =>
        {
            SetSecret(null);
            IsSecretFromQr = false;
            Validate();
        });
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _previewTimer.Tick += (_, _) => UpdatePreview();
        _previewTimer.Start();
    }

    public static AccountEditorViewModel ForNew(OtpAuthEntry? entry = null, string? secretText = null)
    {
        var vm = new AccountEditorViewModel(null, null);
        if (entry is not null)
        {
            vm._issuer = entry.Issuer;
            vm._accountName = entry.AccountName;
            vm._algorithm = entry.Algorithm;
            vm._digits = entry.Digits;
            vm._periodText = entry.Period.ToString(CultureInfo.InvariantCulture);
            vm._showAdvanced = entry.Algorithm != OtpAlgorithm.Sha1 || entry.Digits != Totp.DefaultDigits || entry.Period != Totp.DefaultPeriod;
            vm.SetSecret(entry.Secret);
            vm._isSecretFromQr = true;
        }
        else if (secretText is not null)
        {
            vm.SecretText = secretText;
        }
        vm.Validate();
        vm.UpdatePreview();
        return vm;
    }

    public static AccountEditorViewModel ForEdit(AccountInfo info, Func<AccountInfo, long, string> storedCodeGenerator)
    {
        var vm = new AccountEditorViewModel(info, storedCodeGenerator)
        {
            _issuer = info.Issuer,
            _accountName = info.AccountName,
            _algorithm = info.Algorithm,
            _digits = info.Digits,
            _periodText = info.Period.ToString(CultureInfo.InvariantCulture),
            _showAdvanced = !info.HasDefaultParameters,
        };
        vm.Validate();
        vm.UpdatePreview();
        return vm;
    }

    public IReadOnlyList<Option<OtpAlgorithm>> AlgorithmOptions { get; } =
    [
        new(OtpAlgorithm.Sha1, "SHA-1（標準）"),
        new(OtpAlgorithm.Sha256, "SHA-256"),
        new(OtpAlgorithm.Sha512, "SHA-512"),
    ];

    public IReadOnlyList<int> DigitOptions { get; } = [6, 7, 8];

    public ICommand ClearQrSecretCommand { get; }

    public bool IsNew => _original is null;

    public string WindowTitle => IsNew ? "アカウントを追加" : "アカウントを編集";

    public string SaveText => IsNew ? "追加する" : "保存する";

    public bool IsSecretFromQr
    {
        get => _isSecretFromQr;
        private set
        {
            if (SetProperty(ref _isSecretFromQr, value)) OnPropertyChanged(nameof(ShowSecretInput));
        }
    }

    public bool ShowSecretInput => IsNew && !IsSecretFromQr;

    public string Issuer
    {
        get => _issuer;
        set
        {
            if (SetProperty(ref _issuer, value ?? "")) Validate();
        }
    }

    public string AccountName
    {
        get => _accountName;
        set
        {
            if (SetProperty(ref _accountName, value ?? "")) Validate();
        }
    }

    public string SecretText
    {
        get => _secretText;
        set
        {
            if (SetProperty(ref _secretText, value ?? "") && !_suppressSecretParsing) ParseSecretText();
        }
    }

    public OtpAlgorithm Algorithm
    {
        get => _algorithm;
        set
        {
            if (SetProperty(ref _algorithm, value)) UpdatePreview();
        }
    }

    public int Digits
    {
        get => _digits;
        set
        {
            if (SetProperty(ref _digits, value)) UpdatePreview();
        }
    }

    public string PeriodText
    {
        get => _periodText;
        set
        {
            if (!SetProperty(ref _periodText, value ?? "")) return;
            Validate();
            UpdatePreview();
        }
    }

    public bool ShowAdvanced
    {
        get => _showAdvanced;
        set => SetProperty(ref _showAdvanced, value);
    }

    public string? SecretError
    {
        get => _secretError;
        private set => SetProperty(ref _secretError, value);
    }

    public string? PeriodError
    {
        get => _periodError;
        private set => SetProperty(ref _periodError, value);
    }

    public bool CanSave
    {
        get
        {
            bool hasName = !string.IsNullOrWhiteSpace(Issuer) || !string.IsNullOrWhiteSpace(AccountName);
            return hasName && PeriodError is null && (!IsNew || _secret is not null);
        }
    }

    public string PreviewCode
    {
        get => _previewCode;
        private set => SetProperty(ref _previewCode, value);
    }

    public bool HasPreview => _previewCode.Length > 0 && char.IsDigit(_previewCode[0]);

    public double PreviewProgress
    {
        get => _previewProgress;
        private set => SetProperty(ref _previewProgress, value);
    }

    public int PreviewSeconds
    {
        get => _previewSeconds;
        private set => SetProperty(ref _previewSeconds, value);
    }

    /// <summary>新規登録用のシークレット。編集画面が閉じられる (Dispose) と消去される。</summary>
    public byte[]? Secret => _secret;

    public AccountInfo BuildInfo() => (_original ?? new AccountInfo()) with
    {
        Issuer = Issuer.Trim(),
        AccountName = AccountName.Trim(),
        Algorithm = Algorithm,
        Digits = Digits,
        Period = TryGetPeriod(out var p) ? p : Totp.DefaultPeriod,
    };

    private void ParseSecretText()
    {
        var text = SecretText.Trim();

        // otpauth:// の URI が貼り付けられた場合は各項目に展開する
        if (OtpAuthParser.LooksLikeOtpAuth(text))
        {
            try
            {
                var result = OtpAuthParser.Parse(text);
                var entry = result.Entries.FirstOrDefault()
                            ?? throw new OtpAuthFormatException("対応しているアカウントが含まれていません。");
                if (string.IsNullOrWhiteSpace(Issuer)) Issuer = entry.Issuer;
                if (string.IsNullOrWhiteSpace(AccountName)) AccountName = entry.AccountName;
                Algorithm = entry.Algorithm;
                Digits = entry.Digits;
                PeriodText = entry.Period.ToString(CultureInfo.InvariantCulture);
                if (!entry.Algorithm.Equals(OtpAlgorithm.Sha1) || entry.Digits != 6 || entry.Period != 30) ShowAdvanced = true;

                text = Base32.Encode(entry.Secret);
                foreach (var e in result.Entries) e.ClearSecret();
                _suppressSecretParsing = true;
                SecretText = FormatSecret(text);
                _suppressSecretParsing = false;
            }
            catch (OtpAuthFormatException ex)
            {
                SetSecret(null);
                SecretError = ex.Message;
                Validate();
                return;
            }
        }

        if (text.Length == 0)
        {
            SetSecret(null);
            SecretError = null;
        }
        else if (!Base32.TryDecode(text, out var decoded))
        {
            SetSecret(null);
            SecretError = "使用できない文字が含まれています（英字 A〜Z と数字 2〜7 のみ使用できます）。";
        }
        else if (decoded.Length < Totp.MinSecretBytes)
        {
            Array.Clear(decoded);
            SetSecret(null);
            SecretError = "シークレットキーが短すぎます（通常は16文字以上です）。";
        }
        else
        {
            SetSecret(decoded);
            SecretError = null;
        }
        Validate();
        UpdatePreview();
    }

    private static string FormatSecret(string base32)
        => string.Join(' ', Enumerable.Range(0, (base32.Length + 3) / 4).Select(i => base32.Substring(i * 4, Math.Min(4, base32.Length - i * 4))));

    private void SetSecret(byte[]? value)
    {
        SecureMemory.Zero(_secret);
        _secret = null;
        if (value is null) return;
        _secret = SecureMemory.AllocatePinned(value.Length);
        value.CopyTo(_secret, 0);
        SecureMemory.Zero(value);
    }

    private bool TryGetPeriod(out int period)
        => int.TryParse(PeriodText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out period) && Totp.IsValidPeriod(period);

    private void Validate()
    {
        PeriodError = TryGetPeriod(out _) ? null : $"{Totp.MinPeriod}〜{Totp.MaxPeriod} の数値を入力してください。";
        OnPropertyChanged(nameof(CanSave));
    }

    private void UpdatePreview()
    {
        if (!TryGetPeriod(out var period))
        {
            SetPreview("", 0, 0);
            return;
        }

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long nowSeconds = nowMs / 1000;
        string? code = null;
        try
        {
            if (_secret is not null)
                code = Totp.Compute(_secret, nowSeconds, Algorithm, Digits, period);
            else if (!IsNew && _storedCodeGenerator is not null)
                code = _storedCodeGenerator(BuildInfo(), nowSeconds);
        }
        catch
        {
            code = null; // ロック直後など
        }

        long remaining = Totp.GetMillisecondsRemaining(nowMs, period);
        SetPreview(code is null ? "" : Totp.FormatCode(code), remaining / (period * 1000.0), (int)Math.Ceiling(remaining / 1000.0));
    }

    private void SetPreview(string code, double progress, int seconds)
    {
        if (code.Length == 0) code = "シークレットキーを入力するとコードが表示されます";
        if (PreviewCode != code)
        {
            PreviewCode = code;
            OnPropertyChanged(nameof(HasPreview));
        }
        PreviewProgress = progress;
        PreviewSeconds = seconds;
    }

    public void Dispose()
    {
        _previewTimer.Stop();
        SetSecret(null);
        _secretText = "";
        _previewCode = "";
    }
}
