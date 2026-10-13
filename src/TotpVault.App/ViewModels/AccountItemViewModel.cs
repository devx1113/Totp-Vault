using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TotpVault.App.Mvvm;
using TotpVault.Core.Logging;
using TotpVault.Core.Otp;
using TotpVault.Core.Storage;

namespace TotpVault.App.ViewModels;

/// <summary>一覧の 1 アカウント。コードは周期が切り替わったときだけ生成する（そのたびにシークレットを一瞬だけ復号）。</summary>
public sealed class AccountItemViewModel : ObservableObject
{
    private static readonly Brush[] AvatarPalette =
        new[] { "#2563EB", "#0891B2", "#059669", "#D97706", "#DC2626", "#7C3AED", "#DB2777", "#0D9488", "#EA580C", "#4F46E5" }
            .Select(c =>
            {
                var b = (SolidColorBrush)new BrushConverter().ConvertFromString(c)!;
                b.Freeze();
                return (Brush)b;
            })
            .ToArray();

    private readonly VaultStore _store;
    private readonly DispatcherTimer _copiedTimer;
    private long _counter = long.MinValue;
    private long _nextCounter = long.MinValue;
    private string? _code;
    private string? _nextCode;
    private double _progress = 1;
    private int _secondsRemaining;
    private bool _isExpiring;
    private bool _isCopied;

    public AccountItemViewModel(VaultViewModel owner, VaultStore store, AccountInfo info)
    {
        _store = store;
        Info = info;
        _copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            IsCopied = false;
        };

        CopyCommand = new RelayCommand(() => owner.Copy(this));
        EditCommand = new RelayCommand(() => owner.Edit(this));
        DeleteCommand = new RelayCommand(() => owner.Delete(this));
        ToggleFavoriteCommand = new RelayCommand(() => owner.ToggleFavorite(this));
        MoveUpCommand = new RelayCommand(() => owner.Move(this, -1), () => owner.CanMove(this, -1));
        MoveDownCommand = new RelayCommand(() => owner.Move(this, +1), () => owner.CanMove(this, +1));
    }

    public AccountInfo Info { get; private set; }

    public ICommand CopyCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Info.Issuer) ? Info.AccountName : Info.Issuer;

    public string DisplaySubtitle => string.IsNullOrWhiteSpace(Info.Issuer) ? "" : Info.AccountName;

    public string Initial
    {
        get
        {
            var title = DisplayTitle.Trim();
            return title.Length == 0 ? "?" : StringInfo.GetNextTextElement(title).ToUpperInvariant();
        }
    }

    public Brush AvatarBrush
    {
        get
        {
            // 起動ごとに変わらないよう FNV-1a で色を決める
            uint hash = 2166136261;
            foreach (char c in DisplayTitle.ToLowerInvariant()) hash = (hash ^ c) * 16777619;
            return AvatarPalette[hash % (uint)AvatarPalette.Length];
        }
    }

    public bool IsFavorite => Info.IsFavorite;

    public string FavoriteMenuText => Info.IsFavorite ? "お気に入りから外す" : "お気に入りに追加";

    /// <summary>既定以外のパラメータ（SHA-256・8桁など）のみ表示する。</summary>
    public string ParametersText => Info.HasDefaultParameters
        ? ""
        : $"{Info.Algorithm.ToDisplayName()} · {Info.Digits}桁 · {Info.Period}秒";

    public string? CurrentCode => _code;

    public string FormattedCode => _code is null ? new string('–', Info.Digits) : Totp.FormatCode(_code);

    public string NextCodeText => _nextCode is null ? "" : "次: " + Totp.FormatCode(_nextCode);

    public bool ShowNextCode => _isExpiring && _nextCode is not null;

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public int SecondsRemaining
    {
        get => _secondsRemaining;
        private set => SetProperty(ref _secondsRemaining, value);
    }

    public bool IsExpiring
    {
        get => _isExpiring;
        private set
        {
            if (SetProperty(ref _isExpiring, value)) OnPropertyChanged(nameof(ShowNextCode));
        }
    }

    public bool IsCopied
    {
        get => _isCopied;
        private set => SetProperty(ref _isCopied, value);
    }

    public void SetInfo(AccountInfo info)
    {
        Info = info;
        _counter = long.MinValue; // パラメータ変更に備えて再生成
        _nextCounter = long.MinValue;
        OnPropertyChanged(string.Empty);
    }

    public void MarkCopied()
    {
        IsCopied = true;
        _copiedTimer.Stop();
        _copiedTimer.Start();
    }

    public void Update(long unixTimeMilliseconds)
    {
        long nowSeconds = unixTimeMilliseconds / 1000;
        int period = Info.Period;
        long counter = Totp.GetCounter(nowSeconds, period);

        if (counter != _counter)
        {
            _counter = counter;
            _code = Generate(nowSeconds);
            OnPropertyChanged(nameof(FormattedCode));
            OnPropertyChanged(nameof(CurrentCode));
        }

        long remainingMs = Totp.GetMillisecondsRemaining(unixTimeMilliseconds, period);
        Progress = remainingMs / (period * 1000.0);
        SecondsRemaining = (int)Math.Ceiling(remainingMs / 1000.0);

        bool expiring = SecondsRemaining <= 5;
        if (expiring && _nextCounter != counter + 1)
        {
            _nextCounter = counter + 1;
            _nextCode = Generate((counter + 1) * period);
            OnPropertyChanged(nameof(NextCodeText));
        }
        else if (!expiring && _nextCode is not null)
        {
            _nextCode = null;
            OnPropertyChanged(nameof(NextCodeText));
        }
        IsExpiring = expiring;
        OnPropertyChanged(nameof(ShowNextCode));
    }

    /// <summary>ロック時に表示中のコードを破棄する。</summary>
    public void Clear()
    {
        _code = null;
        _nextCode = null;
        _copiedTimer.Stop();
    }

    private string? Generate(long unixTimeSeconds)
    {
        try
        {
            return _store.GenerateCode(Info, unixTimeSeconds);
        }
        catch (VaultLockedException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"Code generation failed for {Info.Id:N}.", ex);
            return null;
        }
    }
}
