using System.Security.Cryptography;
using System.Text.Json.Serialization;
using TotpVault.Core.Otp;

namespace TotpVault.Core.Storage;

/// <summary>アカウントの表示用情報（シークレットキーは含まない）。</summary>
public sealed record AccountInfo
{
    public Guid Id { get; init; }
    public string Issuer { get; init; } = "";
    public string AccountName { get; init; } = "";
    public OtpAlgorithm Algorithm { get; init; } = OtpAlgorithm.Sha1;
    public int Digits { get; init; } = Totp.DefaultDigits;
    public int Period { get; init; } = Totp.DefaultPeriod;
    public bool IsFavorite { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public int SortOrder { get; init; }

    public bool HasDefaultParameters
        => Algorithm == OtpAlgorithm.Sha1 && Digits == Totp.DefaultDigits && Period == Totp.DefaultPeriod;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) && string.IsNullOrWhiteSpace(AccountName))
            throw new ArgumentException("サービス名またはアカウント名を入力してください。");
        if (Issuer.Length > 200 || AccountName.Length > 200)
            throw new ArgumentException("サービス名・アカウント名は200文字以内で入力してください。");
        if (!Enum.IsDefined(Algorithm)) throw new ArgumentException("未対応のアルゴリズムです。");
        if (!Totp.IsValidDigits(Digits)) throw new ArgumentException("桁数は6〜8桁で指定してください。");
        if (!Totp.IsValidPeriod(Period)) throw new ArgumentException($"更新間隔は{Totp.MinPeriod}〜{Totp.MaxPeriod}秒で指定してください。");
    }
}

/// <summary>バックアップ・移行用。シークレットキーを含むため、使用後は <see cref="ClearSecret"/> を呼ぶこと。</summary>
public sealed class PortableAccount
{
    public string Issuer { get; set; } = "";
    public string AccountName { get; set; } = "";
    public byte[] Secret { get; set; } = [];
    [JsonConverter(typeof(JsonStringEnumConverter<OtpAlgorithm>))]
    public OtpAlgorithm Algorithm { get; set; } = OtpAlgorithm.Sha1;
    public int Digits { get; set; } = Totp.DefaultDigits;
    public int Period { get; set; } = Totp.DefaultPeriod;
    public bool IsFavorite { get; set; }

    public void ClearSecret() => CryptographicOperations.ZeroMemory(Secret);
}

public sealed record ImportResult(int Added, int SkippedDuplicates);

public sealed record VaultLoadResult(IReadOnlyList<AccountInfo> Accounts, int CorruptedCount);
