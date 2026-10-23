namespace GateLink.Core.Otp;

/// <summary>
/// RFC 4648 Base32 encoding/decoding, used for TOTP shared secrets. Decoding is
/// case-insensitive and tolerates padding ('=') and spaces, which authenticator
/// setup keys often contain.
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] Decode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new FormatException("Base32 input is empty.");

        // Normalise: strip spaces/padding, upper-case.
        var cleaned = input.Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant();
        if (cleaned.Length == 0)
            throw new FormatException("Base32 input contains no data.");

        var output = new List<byte>(cleaned.Length * 5 / 8);
        int buffer = 0;
        int bitsLeft = 0;

        foreach (var c in cleaned)
        {
            var val = Alphabet.IndexOf(c);
            if (val < 0)
                throw new FormatException($"Invalid Base32 character: '{c}'.");

            buffer = (buffer << 5) | val;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return output.ToArray();
    }

    public static string Encode(byte[] data)
    {
        if (data.Length == 0) return string.Empty;

        var result = new System.Text.StringBuilder((data.Length + 4) / 5 * 8);
        int buffer = 0;
        int bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                result.Append(Alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }

        if (bitsLeft > 0)
            result.Append(Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);

        return result.ToString();
    }
}
