using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace TotpVault.Core.Otp;

/// <summary>
/// RFC 4648 Base32。シークレットキー入力の揺れ（小文字・空白・ハイフン・パディング・全角英数字）を許容する。
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static bool TryDecode(ReadOnlySpan<char> input, [NotNullWhen(true)] out byte[]? result)
    {
        result = null;

        int dataChars = 0;
        bool sawPadding = false;
        foreach (char raw in input)
        {
            if (IsSeparator(raw)) continue;
            if (raw == '=' || raw == '＝') { sawPadding = true; continue; }
            if (sawPadding || Value(raw) < 0) return false; // パディング後のデータ、または不正文字
            dataChars++;
        }
        if (dataChars == 0) return false;

        var output = new byte[dataChars * 5 / 8];
        int buffer = 0, bits = 0, index = 0;
        foreach (char raw in input)
        {
            if (IsSeparator(raw) || raw == '=' || raw == '＝') continue;
            buffer = (buffer << 5) | Value(raw);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output[index++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }

        result = output;
        return true;
    }

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Alphabet[(buffer >> bits) & 0x1F]);
            }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);
        return sb.ToString();
    }

    private static bool IsSeparator(char c) => c is ' ' or '\t' or '\r' or '\n' or '-' or '　' or '－';

    private static int Value(char c)
    {
        // 全角英数字（Ａ-Ｚ, ａ-ｚ, ０-９）を半角として扱う
        if (c is >= 'Ａ' and <= 'Ｚ') c = (char)(c - 'Ａ' + 'A');
        else if (c is >= 'ａ' and <= 'ｚ') c = (char)(c - 'ａ' + 'a');
        else if (c is >= '０' and <= '９') c = (char)(c - '０' + '0');

        return c switch
        {
            >= 'A' and <= 'Z' => c - 'A',
            >= 'a' and <= 'z' => c - 'a',
            >= '2' and <= '7' => c - '2' + 26,
            _ => -1,
        };
    }
}
