namespace TotpVault.Core.Otp;

/// <summary>TOTP の HMAC ハッシュアルゴリズム。</summary>
public enum OtpAlgorithm
{
    Sha1 = 1,
    Sha256 = 2,
    Sha512 = 3,
}

public static class OtpAlgorithmExtensions
{
    public static string ToDisplayName(this OtpAlgorithm algorithm) => algorithm switch
    {
        OtpAlgorithm.Sha1 => "SHA-1",
        OtpAlgorithm.Sha256 => "SHA-256",
        OtpAlgorithm.Sha512 => "SHA-512",
        _ => algorithm.ToString(),
    };

    /// <summary>"SHA1" / "sha-256" / "SHA512" などの表記を解釈する。</summary>
    public static bool TryParse(string? text, out OtpAlgorithm algorithm)
    {
        algorithm = OtpAlgorithm.Sha1;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var normalized = text.Trim().Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
        switch (normalized)
        {
            case "SHA1": algorithm = OtpAlgorithm.Sha1; return true;
            case "SHA256": algorithm = OtpAlgorithm.Sha256; return true;
            case "SHA512": algorithm = OtpAlgorithm.Sha512; return true;
            default: return false;
        }
    }
}
