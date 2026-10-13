using System.Globalization;
using System.Text;

namespace TotpVault.Core.Logging;

public interface ILogSink
{
    void Write(string level, string message);
}

/// <summary>
/// アプリケーションログ。
/// 方針: シークレットキー・認証コード・パスワード・サービス名/アカウント名はログに出力しない。
/// 記録するのはイベント種別とアカウントの内部ID程度に限定する。
/// </summary>
public static class Log
{
    private static ILogSink? _sink;

    public static void Configure(ILogSink? sink) => _sink = sink;

    public static void Info(string message) => _sink?.Write("INFO", message);

    public static void Warn(string message) => _sink?.Write("WARN", message);

    /// <summary>例外は型名とメッセージのみ記録する（本アプリの例外メッセージには機密情報を含めない）。</summary>
    public static void Error(string message, Exception? ex = null)
        => _sink?.Write("ERROR", ex is null ? message : $"{message} [{ex.GetType().Name}] {ex.Message}");
}

/// <summary>サイズ上限付きのファイルログ（上限超過時は 1 世代だけ残してローテーション）。</summary>
public sealed class FileLogSink(string directory, long maxBytes = 512 * 1024) : ILogSink
{
    private readonly object _gate = new();
    private readonly string _path = Path.Combine(directory, "app.log");

    public void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > maxBytes)
                    File.Move(_path, Path.Combine(directory, "app.old.log"), overwrite: true);

                var line = string.Create(CultureInfo.InvariantCulture,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}");
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // ログ出力の失敗でアプリを停止させない
        }
    }
}

/// <summary>テスト用のメモリ内ログ。</summary>
public sealed class MemoryLogSink : ILogSink
{
    private readonly List<string> _lines = [];
    public IReadOnlyList<string> Lines { get { lock (_lines) return _lines.ToList(); } }
    public void Write(string level, string message) { lock (_lines) _lines.Add($"[{level}] {message}"); }
}
