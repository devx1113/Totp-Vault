using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using TotpVault.Core.Logging;
using TotpVault.Core.Security;

namespace TotpVault.App.Services;

/// <summary>
/// 認証コードのコピーと自動消去。
/// ・Windows のクリップボード履歴 (Win+V) とクラウド同期の対象外としてコピーする
/// ・一定時間後、クリップボードがまだ本アプリのコピー内容のままである場合のみ消去する
///   （他のアプリが別のデータをコピーしていた場合は消さない）
/// </summary>
public sealed class ClipboardService
{
    private readonly ClipboardClearTracker _tracker = new();

    public bool CopySensitive(string text, TimeSpan clearAfter)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        // https://learn.microsoft.com/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));

        if (!Retry(() => Clipboard.SetDataObject(data, copy: true)))
        {
            Log.Warn("Clipboard is busy; copy failed.");
            return false;
        }

        if (clearAfter > TimeSpan.Zero)
            _tracker.RecordCopy(text, NativeMethods.GetClipboardSequenceNumber(), DateTimeOffset.Now, clearAfter);
        else
            _tracker.Reset();
        return true;
    }

    /// <summary>定期的に呼び出し、消去時刻を過ぎていれば消去する。</summary>
    public void Tick() => Apply(_tracker.Evaluate(NativeMethods.GetClipboardSequenceNumber(), ReadText, DateTimeOffset.Now));

    /// <summary>ロック時・終了時に、本アプリのコピー内容が残っていれば即時消去する。</summary>
    public void ClearNowIfOurs()
        => Apply(_tracker.Evaluate(NativeMethods.GetClipboardSequenceNumber(), ReadText, DateTimeOffset.Now, force: true));

    private static void Apply(ClipboardClearAction action)
    {
        if (action == ClipboardClearAction.Clear && Retry(Clipboard.Clear))
            Log.Info("Clipboard cleared.");
        else if (action == ClipboardClearAction.Abandon)
            Log.Info("Clipboard changed by another application; not cleared.");
    }

    private static string? ReadText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    private static bool Retry(Action action)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                action();
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(40); // 他のアプリがクリップボードを開いている
            }
        }
        return false;
    }
}
