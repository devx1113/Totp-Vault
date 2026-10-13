using System.IO;

namespace TotpVault.App.Services;

public static class AppPaths
{
    /// <summary>既定: %LOCALAPPDATA%\TotpVault。起動引数 --data-dir "パス" で変更可能（検証・デモ用）。</summary>
    public static string DataDirectory { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TotpVault");

    public static string VaultFile => Path.Combine(DataDirectory, "vault.db");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static void Initialize(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--data-dir", StringComparison.OrdinalIgnoreCase))
                DataDirectory = Path.GetFullPath(args[i + 1]);
        }
        Directory.CreateDirectory(DataDirectory);
    }
}
