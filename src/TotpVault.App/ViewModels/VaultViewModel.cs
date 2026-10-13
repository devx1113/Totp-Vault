using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using TotpVault.App.Mvvm;
using TotpVault.App.Services;
using TotpVault.App.Views;
using TotpVault.Core.Logging;
using TotpVault.Core.Otp;
using TotpVault.Core.Storage;

namespace TotpVault.App.ViewModels;

public sealed record Option<T>(T Value, string Label);

/// <summary>ロック解除中のメイン画面（アカウント一覧）。</summary>
public sealed class VaultViewModel : ObservableObject, IDisposable
{
    private const CompareOptions SearchOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;
    private static readonly CultureInfo Japanese = CultureInfo.GetCultureInfo("ja-JP");
    private static readonly StringComparer NameComparer = StringComparer.Create(Japanese, SearchOptions);

    private readonly MainViewModel _main;
    private readonly VaultStore _store;
    private readonly AppSettings _settings;
    private readonly List<AccountItemViewModel> _all = [];
    private readonly DispatcherTimer _timer;
    private string _searchText = "";
    private string _sortMode;
    private string _statusText = "";
    private bool _disposed;

    public VaultViewModel(MainViewModel main, VaultStore store, AppSettings settings)
    {
        _main = main;
        _store = store;
        _settings = settings;
        _sortMode = SortOptions.Any(o => o.Value == settings.SortMode) ? settings.SortMode : "custom";

        AddManualCommand = new RelayCommand(() => AddWithEditor(AccountEditorViewModel.ForNew()));
        AddFromImageCommand = new AsyncRelayCommand(AddFromImageFileAsync);
        AddFromScreenCommand = new AsyncRelayCommand(AddFromScreenAsync);
        AddFromClipboardCommand = new AsyncRelayCommand(AddFromClipboardAsync);
        ImportBackupCommand = new RelayCommand(_main.ImportBackup);
        ExportBackupCommand = new RelayCommand(_main.ExportBackup, () => _all.Count > 0);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        LockCommand = new RelayCommand(() => _main.Lock("manual"));
        ClearSearchCommand = new RelayCommand(() => SearchText = "");

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => Tick();

        Reload();
        RefreshStatus();
        _timer.Start();
    }

    public ObservableCollection<AccountItemViewModel> Favorites { get; } = [];
    public ObservableCollection<AccountItemViewModel> Others { get; } = [];

    public IReadOnlyList<Option<string>> SortOptions { get; } =
    [
        new("custom", "カスタム順"),
        new("name", "名前順"),
        new("newest", "新しい順"),
    ];

    public ICommand AddManualCommand { get; }
    public ICommand AddFromImageCommand { get; }
    public ICommand AddFromScreenCommand { get; }
    public ICommand AddFromClipboardCommand { get; }
    public ICommand ImportBackupCommand { get; }
    public ICommand ExportBackupCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand LockCommand { get; }
    public ICommand ClearSearchCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? "")) ApplyFilter();
        }
    }

    public string SortMode
    {
        get => _sortMode;
        set
        {
            if (value is null || !SetProperty(ref _sortMode, value)) return;
            _settings.SortMode = value;
            ApplyFilter();
        }
    }

    public bool HasFavorites => Favorites.Count > 0;
    public bool HasOthers => Others.Count > 0;
    public bool ShowOthersHeader => HasFavorites && HasOthers;
    public bool IsEmpty => _all.Count == 0;
    public bool HasAccounts => _all.Count > 0;
    public bool HasNoResults => _all.Count > 0 && !HasFavorites && !HasOthers;
    public string NoResultsText => $"「{SearchText.Trim()}」に一致するアカウントはありません。";
    public string AccountCountText => _all.Count == 0 ? "アカウント未登録" : $"{_all.Count} 件のアカウント";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    // ───────────────────────────── 一覧 ─────────────────────────────

    public void Reload()
    {
        if (_disposed) return;
        var result = _store.LoadAccounts();
        foreach (var item in _all) item.Clear();
        _all.Clear();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var info in result.Accounts)
        {
            var item = new AccountItemViewModel(this, _store, info);
            item.Update(now);
            _all.Add(item);
        }
        ApplyFilter();

        if (result.CorruptedCount > 0)
        {
            Ui.Info("一部のデータを読み込めません",
                $"{result.CorruptedCount} 件のアカウントデータが破損または改ざんされているため表示できません。\nバックアップからの復元を検討してください。",
                DialogKind.Warning);
        }
    }

    public void RefreshStatus()
    {
        int minutes = _settings.AutoLockMinutes;
        StatusText = "暗号化済み · " + (minutes > 0 ? $"{minutes}分間操作がないと自動ロック" : "自動ロック無効");
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        IEnumerable<AccountItemViewModel> items = _all;
        if (query.Length > 0)
        {
            items = items.Where(i =>
                Japanese.CompareInfo.IndexOf(i.Info.Issuer, query, SearchOptions) >= 0 ||
                Japanese.CompareInfo.IndexOf(i.Info.AccountName, query, SearchOptions) >= 0);
        }

        items = SortMode switch
        {
            "name" => items.OrderBy(i => i.DisplayTitle, NameComparer).ThenBy(i => i.DisplaySubtitle, NameComparer),
            "newest" => items.OrderByDescending(i => i.Info.CreatedAt),
            _ => items.OrderBy(i => i.Info.SortOrder),
        };

        var list = items.ToList();
        Replace(Favorites, list.Where(i => i.IsFavorite));
        Replace(Others, list.Where(i => !i.IsFavorite));

        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasOthers));
        OnPropertyChanged(nameof(ShowOthersHeader));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(NoResultsText));
        OnPropertyChanged(nameof(AccountCountText));
    }

    private static void Replace(ObservableCollection<AccountItemViewModel> target, IEnumerable<AccountItemViewModel> items)
    {
        var list = items.ToList();
        if (target.SequenceEqual(list)) return;
        target.Clear();
        foreach (var item in list) target.Add(item);
    }

    private void Tick()
    {
        if (_disposed) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var item in _all) item.Update(now);
    }

    // ───────────────────────────── アカウント操作 ─────────────────────────────

    public void Copy(AccountItemViewModel item)
    {
        var code = item.CurrentCode;
        if (code is null) return;
        int seconds = Math.Max(0, _settings.ClipboardClearSeconds);
        if (!_main.Clipboard.CopySensitive(code, TimeSpan.FromSeconds(seconds)))
        {
            _main.ShowToast("クリップボードにアクセスできませんでした。もう一度お試しください。");
            return;
        }
        item.MarkCopied();
        _main.ShowToast(seconds > 0 ? $"コードをコピーしました（{seconds}秒後に自動消去）" : "コードをコピーしました");
    }

    public void ToggleFavorite(AccountItemViewModel item)
    {
        var updated = item.Info with { IsFavorite = !item.Info.IsFavorite };
        _store.UpdateAccount(updated);
        item.SetInfo(updated);
        ApplyFilter();
    }

    public void Edit(AccountItemViewModel item)
    {
        using var editor = AccountEditorViewModel.ForEdit(item.Info, _store.GenerateCode);
        if (Ui.ShowDialog(new AccountEditorWindow(editor)) != true || _disposed) return;
        var updated = editor.BuildInfo();
        _store.UpdateAccount(updated);
        item.SetInfo(updated);
        item.Update(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        ApplyFilter();
        _main.ShowToast("保存しました");
    }

    public void Delete(AccountItemViewModel item)
    {
        if (!Ui.Confirm($"「{item.DisplayTitle}」を削除しますか？",
                "削除すると、このアプリではこのサービスの認証コードを生成できなくなります。この操作は元に戻せません。\n\n" +
                "削除する前に、サービス側で2段階認証を無効化するか、別の認証アプリへ移行済みであることを確認してください。",
                "削除する", DialogKind.Danger))
            return;

        _store.DeleteAccount(item.Info.Id);
        item.Clear();
        _all.Remove(item);
        ApplyFilter();
        _main.ShowToast("削除しました");
    }

    public bool CanMove(AccountItemViewModel item, int delta)
    {
        if (SortMode != "custom" || SearchText.Length > 0) return false;
        var group = _all.Where(i => i.IsFavorite == item.IsFavorite).OrderBy(i => i.Info.SortOrder).ToList();
        int target = group.IndexOf(item) + delta;
        return target >= 0 && target < group.Count;
    }

    public void Move(AccountItemViewModel item, int delta)
    {
        if (!CanMove(item, delta)) return;
        var ordered = _all.OrderBy(i => i.Info.SortOrder).ToList();
        var group = ordered.Where(i => i.IsFavorite == item.IsFavorite).ToList();
        var other = group[group.IndexOf(item) + delta];

        int a = ordered.IndexOf(item), b = ordered.IndexOf(other);
        (ordered[a], ordered[b]) = (ordered[b], ordered[a]);
        _store.SetOrder(ordered.Select(i => i.Info.Id).ToList());
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Info.SortOrder != i) ordered[i].SetInfo(ordered[i].Info with { SortOrder = i });
        }
        Tick();
        ApplyFilter();
    }

    // ───────────────────────────── 追加 ─────────────────────────────

    private void AddWithEditor(AccountEditorViewModel editor)
    {
        using (editor)
        {
            if (Ui.ShowDialog(new AccountEditorWindow(editor)) != true || _disposed) return;
            var secret = editor.Secret;
            if (secret is null) return;

            if (_store.ContainsSecret(secret) &&
                !Ui.Confirm("重複の確認", "同じシークレットキーのアカウントが既に登録されています。重複して追加しますか？", "追加する", DialogKind.Warning))
                return;

            AddToList(_store.AddAccount(editor.BuildInfo(), secret));
            _main.ShowToast($"「{_all[^1].DisplayTitle}」を追加しました");
        }
    }

    private void AddToList(AccountInfo info)
    {
        var item = new AccountItemViewModel(this, _store, info);
        item.Update(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _all.Add(item);
        ApplyFilter();
    }

    private async Task AddFromImageFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "QRコード画像を選択",
            Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|すべてのファイル (*.*)|*.*",
            Multiselect = true,
        };
        var owner = Ui.ActiveOwner();
        if ((owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) != true) return;
        await ImportImageFilesAsync(dialog.FileNames);
    }

    public async Task ImportImageFilesAsync(IReadOnlyList<string> files)
    {
        var texts = new List<string>();
        int failed = 0;
        foreach (var file in files)
        {
            try
            {
                var image = QrImageReader.LoadFile(file);
                texts.AddRange(await _main.RunBusyAsync("QRコードを読み取っています…", () => QrImageReader.DecodeAsync(image)));
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException or ArgumentException)
            {
                failed++;
            }
        }
        if (_disposed) return;

        if (texts.Count == 0 && failed > 0)
        {
            Ui.Error("画像を読み込めませんでした", "画像ファイルの形式に対応していないか、ファイルが破損しています。PNG または JPEG 形式の画像をお試しください。");
            return;
        }
        HandleQrTexts(texts, "選択した画像");
    }

    private async Task AddFromScreenAsync()
    {
        var window = Application.Current.MainWindow;
        BitmapSource capture;
        window.Hide(); // 自分のウィンドウが QR コードを隠さないようにする
        try
        {
            await Task.Delay(400);
            capture = ScreenCapture.CaptureVirtualScreen();
        }
        finally
        {
            window.Show();
            window.Activate();
        }
        if (_disposed) return;

        var texts = await _main.RunBusyAsync("画面上のQRコードを探しています…", () => QrImageReader.DecodeAsync(capture, isScreenCapture: true));
        if (_disposed) return;
        HandleQrTexts(texts, "画面上");
    }

    private async Task AddFromClipboardAsync()
    {
        var image = QrImageReader.GetClipboardImage();
        if (image is not null)
        {
            var texts = await _main.RunBusyAsync("QRコードを読み取っています…", () => QrImageReader.DecodeAsync(image));
            if (!_disposed) HandleQrTexts(texts, "クリップボードの画像");
            return;
        }

        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList().Cast<string>().Where(QrImageReader.IsImageFile).ToList();
            if (files.Count > 0)
            {
                await ImportImageFilesAsync(files);
                return;
            }
        }

        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText().Trim();
            if (OtpAuthParser.LooksLikeOtpAuth(text))
            {
                HandleQrTexts([text], "クリップボード");
                return;
            }
            if (text.Length <= 128 && Base32.TryDecode(text, out var secret) && secret.Length >= Totp.MinSecretBytes)
            {
                Array.Clear(secret);
                AddWithEditor(AccountEditorViewModel.ForNew(secretText: text));
                return;
            }
        }

        Ui.Info("貼り付けできるデータがありません",
            "QRコードのスクリーンショット（Win + Shift + S）、QRコード画像、または otpauth:// で始まるURIをコピーしてから実行してください。",
            DialogKind.Warning);
    }

    /// <summary>読み取った QR コードの内容を解析し、1 件なら編集画面、複数件ならまとめて登録する。</summary>
    private void HandleQrTexts(IReadOnlyList<string> texts, string source)
    {
        if (texts.Count == 0)
        {
            Ui.Info("QRコードが見つかりません",
                $"{source}にQRコードが見つかりませんでした。\nQRコード全体がはっきり表示されていることを確認してください。",
                DialogKind.Warning);
            return;
        }

        var otpTexts = texts.Where(OtpAuthParser.LooksLikeOtpAuth).ToList();
        if (otpTexts.Count == 0)
        {
            Ui.Info("2段階認証用のQRコードではありません",
                "読み取ったQRコードは2段階認証（otpauth://）の登録用ではありません。\nサービスの2段階認証の設定画面に表示されるQRコードを使用してください。",
                DialogKind.Warning);
            return;
        }

        var entries = new List<OtpAuthEntry>();
        var errors = new List<string>();
        int unsupported = 0;
        foreach (var text in otpTexts)
        {
            try
            {
                var result = OtpAuthParser.Parse(text);
                entries.AddRange(result.Entries);
                unsupported += result.SkippedUnsupported;
            }
            catch (OtpAuthFormatException ex)
            {
                errors.Add(ex.Message);
            }
        }

        try
        {
            if (entries.Count == 0)
            {
                Ui.Error("登録できないQRコードです", string.Join("\n", errors.Distinct()));
                return;
            }

            if (entries.Count == 1 && unsupported == 0)
            {
                AddWithEditor(AccountEditorViewModel.ForNew(entries[0]));
                return;
            }

            AddMany(entries, unsupported);
        }
        finally
        {
            foreach (var e in entries) e.ClearSecret();
        }
    }

    private void AddMany(List<OtpAuthEntry> entries, int unsupported)
    {
        var names = string.Join("\n", entries.Take(8).Select(e => "・" + (string.IsNullOrEmpty(e.Issuer) ? e.AccountName : $"{e.Issuer}（{e.AccountName}）")));
        if (entries.Count > 8) names += $"\n…ほか {entries.Count - 8} 件";
        var note = unsupported > 0 ? $"\n\n※ 未対応の形式（HOTP等）の {unsupported} 件は除外されます。" : "";
        if (!Ui.Confirm($"{entries.Count} 件のアカウントが見つかりました", $"以下のアカウントを追加しますか？\n\n{names}{note}", "すべて追加"))
            return;

        int added = 0, skipped = 0;
        foreach (var e in entries)
        {
            if (_store.ContainsSecret(e.Secret))
            {
                skipped++;
                continue;
            }
            var info = new AccountInfo
            {
                Issuer = e.Issuer,
                AccountName = string.IsNullOrWhiteSpace(e.AccountName) && string.IsNullOrWhiteSpace(e.Issuer) ? "（名称未設定）" : e.AccountName,
                Algorithm = e.Algorithm,
                Digits = e.Digits,
                Period = e.Period,
            };
            AddToList(_store.AddAccount(info, e.Secret));
            added++;
        }
        _main.ShowToast(skipped > 0 ? $"{added} 件追加しました（{skipped} 件は登録済み）" : $"{added} 件のアカウントを追加しました");
    }

    private void OpenSettings()
    {
        Ui.ShowDialog(new SettingsWindow(new SettingsViewModel(_main, _settings)));
        if (!_disposed) RefreshStatus();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        foreach (var item in _all) item.Clear();
        _all.Clear();
        Favorites.Clear();
        Others.Clear();
    }
}
