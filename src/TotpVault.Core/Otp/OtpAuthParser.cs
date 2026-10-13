using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TotpVault.Core.Otp;

/// <summary>QRコード等から読み取った 1 アカウント分の登録情報。Secret は呼び出し側で使用後に消去する。</summary>
public sealed class OtpAuthEntry
{
    public string Issuer { get; init; } = "";
    public string AccountName { get; init; } = "";
    public required byte[] Secret { get; init; }
    public OtpAlgorithm Algorithm { get; init; } = OtpAlgorithm.Sha1;
    public int Digits { get; init; } = Totp.DefaultDigits;
    public int Period { get; init; } = Totp.DefaultPeriod;

    public void ClearSecret() => CryptographicOperations.ZeroMemory(Secret);
}

public sealed record OtpAuthParseResult(IReadOnlyList<OtpAuthEntry> Entries, int SkippedUnsupported);

public sealed class OtpAuthFormatException(string message) : Exception(message);

/// <summary>
/// otpauth://totp/... 形式と、Google Authenticator のエクスポート形式 (otpauth-migration://offline?data=...) を解析する。
/// </summary>
public static class OtpAuthParser
{
    private const string TotpPrefix = "otpauth://";
    private const string MigrationPrefix = "otpauth-migration://";

    public static bool LooksLikeOtpAuth(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.TrimStart();
        return t.StartsWith(TotpPrefix, StringComparison.OrdinalIgnoreCase)
            || t.StartsWith(MigrationPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static OtpAuthParseResult Parse(string text)
    {
        var t = text.Trim();
        if (t.StartsWith(MigrationPrefix, StringComparison.OrdinalIgnoreCase))
            return ParseMigration(t);
        return new OtpAuthParseResult([ParseTotp(t)], 0);
    }

    public static OtpAuthEntry ParseTotp(string uri)
    {
        var text = uri.Trim();
        if (!text.StartsWith(TotpPrefix, StringComparison.OrdinalIgnoreCase))
            throw new OtpAuthFormatException("2段階認証用のQRコード（otpauth://）ではありません。");

        var rest = text[TotpPrefix.Length..];
        int hash = rest.IndexOf('#');
        if (hash >= 0) rest = rest[..hash];

        int slash = rest.IndexOf('/');
        int q = rest.IndexOf('?');
        int typeEnd = slash >= 0 ? slash : (q >= 0 ? q : rest.Length);
        var type = rest[..typeEnd];

        if (type.Equals("hotp", StringComparison.OrdinalIgnoreCase))
            throw new OtpAuthFormatException("カウンター方式（HOTP）には対応していません。時刻ベース（TOTP）のみ登録できます。");
        if (!type.Equals("totp", StringComparison.OrdinalIgnoreCase))
            throw new OtpAuthFormatException($"未対応の形式です（{type}）。");

        string labelRaw = "", query = "";
        if (q >= 0)
        {
            query = rest[(q + 1)..];
            if (slash >= 0 && slash < q) labelRaw = rest[(slash + 1)..q];
        }
        else if (slash >= 0)
        {
            labelRaw = rest[(slash + 1)..];
        }

        var parameters = ParseQuery(query);
        var label = Unescape(labelRaw);
        string? labelIssuer = null;
        string account = label.Trim();
        int colon = label.IndexOf(':');
        if (colon >= 0)
        {
            labelIssuer = label[..colon].Trim();
            account = label[(colon + 1)..].Trim();
        }

        parameters.TryGetValue("issuer", out var issuerParam);
        var issuer = !string.IsNullOrWhiteSpace(issuerParam) ? issuerParam.Trim() : labelIssuer ?? "";

        if (!parameters.TryGetValue("secret", out var secretText) || string.IsNullOrWhiteSpace(secretText))
            throw new OtpAuthFormatException("シークレットキーが含まれていません。");
        if (!Base32.TryDecode(secretText, out var secret))
            throw new OtpAuthFormatException("シークレットキーの形式が正しくありません（Base32 ではありません）。");
        if (secret.Length < Totp.MinSecretBytes)
        {
            CryptographicOperations.ZeroMemory(secret);
            throw new OtpAuthFormatException("シークレットキーが短すぎます。");
        }

        try
        {
            var algorithm = OtpAlgorithm.Sha1;
            if (parameters.TryGetValue("algorithm", out var algText) && !OtpAlgorithmExtensions.TryParse(algText, out algorithm))
                throw new OtpAuthFormatException($"未対応のアルゴリズムです（{algText}）。");

            int digits = Totp.DefaultDigits;
            if (parameters.TryGetValue("digits", out var digitsText)
                && (!int.TryParse(digitsText, NumberStyles.None, CultureInfo.InvariantCulture, out digits) || !Totp.IsValidDigits(digits)))
                throw new OtpAuthFormatException($"未対応の桁数です（{digitsText}）。6〜8桁に対応しています。");

            int period = Totp.DefaultPeriod;
            if (parameters.TryGetValue("period", out var periodText)
                && (!int.TryParse(periodText, NumberStyles.None, CultureInfo.InvariantCulture, out period) || !Totp.IsValidPeriod(period)))
                throw new OtpAuthFormatException($"未対応の更新間隔です（{periodText}）。");

            return new OtpAuthEntry
            {
                Issuer = issuer,
                AccountName = account,
                Secret = secret,
                Algorithm = algorithm,
                Digits = digits,
                Period = period,
            };
        }
        catch
        {
            CryptographicOperations.ZeroMemory(secret);
            throw;
        }
    }

    /// <summary>Google Authenticator「アカウントを転送」で表示される QR コード。</summary>
    public static OtpAuthParseResult ParseMigration(string uri)
    {
        var text = uri.Trim();
        int q = text.IndexOf('?');
        var parameters = ParseQuery(q >= 0 ? text[(q + 1)..] : "");
        if (!parameters.TryGetValue("data", out var data) || string.IsNullOrWhiteSpace(data))
            throw new OtpAuthFormatException("移行データが含まれていません。");

        byte[] payload;
        try
        {
            var b64 = data.Trim().Replace(' ', '+').Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            payload = Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            throw new OtpAuthFormatException("移行データの形式が正しくありません。");
        }

        var entries = new List<OtpAuthEntry>();
        int skipped = 0;
        try
        {
            var reader = new ProtoReader(payload, 0, payload.Length);
            while (reader.TryReadTag(out int field, out int wireType))
            {
                if (field == 1 && wireType == 2)
                {
                    var entry = ParseMigrationParameters(reader.ReadBytes());
                    if (entry is null) skipped++;
                    else entries.Add(entry);
                }
                else
                {
                    reader.Skip(wireType);
                }
            }
        }
        catch (Exception ex) when (ex is not OtpAuthFormatException)
        {
            foreach (var e in entries) e.ClearSecret();
            throw new OtpAuthFormatException("移行データの解析に失敗しました。");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        if (entries.Count == 0 && skipped == 0)
            throw new OtpAuthFormatException("移行データにアカウントが含まれていません。");
        return new OtpAuthParseResult(entries, skipped);
    }

    private static OtpAuthEntry? ParseMigrationParameters(ArraySegment<byte> message)
    {
        byte[]? secret = null;
        string name = "", issuer = "";
        long algorithm = 0, digits = 0, type = 0;

        var reader = new ProtoReader(message.Array!, message.Offset, message.Offset + message.Count);
        while (reader.TryReadTag(out int field, out int wireType))
        {
            switch (field)
            {
                case 1 when wireType == 2:
                    if (secret != null) CryptographicOperations.ZeroMemory(secret);
                    secret = reader.ReadBytes().ToArray();
                    break;
                case 2 when wireType == 2: name = Encoding.UTF8.GetString(reader.ReadBytes()); break;
                case 3 when wireType == 2: issuer = Encoding.UTF8.GetString(reader.ReadBytes()); break;
                case 4 when wireType == 0: algorithm = reader.ReadVarint(); break;
                case 5 when wireType == 0: digits = reader.ReadVarint(); break;
                case 6 when wireType == 0: type = reader.ReadVarint(); break;
                default: reader.Skip(wireType); break;
            }
        }

        OtpAlgorithm? alg = algorithm switch
        {
            0 or 1 => OtpAlgorithm.Sha1,
            2 => OtpAlgorithm.Sha256,
            3 => OtpAlgorithm.Sha512,
            _ => null, // MD5 など
        };
        int? digitCount = digits switch { 0 or 1 => 6, 2 => 8, _ => null };
        bool isTotp = type is 0 or 2;

        if (secret is null || secret.Length == 0 || alg is null || digitCount is null || !isTotp)
        {
            if (secret != null) CryptographicOperations.ZeroMemory(secret);
            return null;
        }

        // name は "Issuer:account" 形式の場合がある
        var account = name;
        int colon = name.IndexOf(':');
        if (colon >= 0)
        {
            var prefix = name[..colon].Trim();
            if (issuer.Length == 0 || prefix.Equals(issuer, StringComparison.OrdinalIgnoreCase))
            {
                if (issuer.Length == 0) issuer = prefix;
                account = name[(colon + 1)..];
            }
        }

        return new OtpAuthEntry
        {
            Issuer = issuer.Trim(),
            AccountName = account.Trim(),
            Secret = secret,
            Algorithm = alg.Value,
            Digits = digitCount.Value,
            Period = Totp.DefaultPeriod,
        };
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            var key = Unescape(eq >= 0 ? part[..eq] : part).Trim();
            var value = eq >= 0 ? Unescape(part[(eq + 1)..]) : "";
            result.TryAdd(key, value); // 重複時は最初の値を採用
        }
        return result;
    }

    private static string Unescape(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

    /// <summary>移行データ解析用の最小限の Protocol Buffers リーダー。</summary>
    private sealed class ProtoReader(byte[] buffer, int start, int end)
    {
        private int _pos = start;

        public bool TryReadTag(out int field, out int wireType)
        {
            field = wireType = 0;
            if (_pos >= end) return false;
            long tag = ReadVarint();
            field = (int)(tag >> 3);
            wireType = (int)(tag & 0x7);
            if (field <= 0) throw new FormatException("Invalid field number.");
            return true;
        }

        public long ReadVarint()
        {
            long result = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                if (_pos >= end) throw new FormatException("Truncated varint.");
                byte b = buffer[_pos++];
                result |= (long)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
            }
            throw new FormatException("Varint too long.");
        }

        public ArraySegment<byte> ReadBytes()
        {
            long length = ReadVarint();
            if (length < 0 || length > end - _pos) throw new FormatException("Invalid length.");
            var segment = new ArraySegment<byte>(buffer, _pos, (int)length);
            _pos += (int)length;
            return segment;
        }

        public void Skip(int wireType)
        {
            switch (wireType)
            {
                case 0: ReadVarint(); break;
                case 1: Advance(8); break;
                case 2: ReadBytes(); break;
                case 5: Advance(4); break;
                default: throw new FormatException("Unsupported wire type.");
            }
        }

        private void Advance(int count)
        {
            if (count > end - _pos) throw new FormatException("Truncated.");
            _pos += count;
        }
    }
}
