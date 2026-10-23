using System.Web;

namespace GateLink.Core.Otp;

/// <summary>
/// Parses an <c>otpauth://totp/...</c> URI — the payload encoded in a 2FA QR
/// code and also what a user can paste manually. Both the "scan QR" and
/// "enter manually" setup paths converge here.
///
/// Format (RFC / Key Uri Format):
///   otpauth://totp/LABEL?secret=BASE32&amp;issuer=NAME&amp;digits=6&amp;period=30&amp;algorithm=SHA1
/// </summary>
public sealed record OtpAuthUri
{
    public required string Secret { get; init; }
    public string? Issuer { get; init; }
    public string? Account { get; init; }
    public int Digits { get; init; } = 6;
    public int Period { get; init; } = 30;
    public OtpAlgorithm Algorithm { get; init; } = OtpAlgorithm.Sha1;

    public static OtpAuthUri Parse(string uriText)
    {
        if (string.IsNullOrWhiteSpace(uriText))
            throw new FormatException("Empty otpauth URI.");

        if (!Uri.TryCreate(uriText.Trim(), UriKind.Absolute, out var uri))
            throw new FormatException("Not a valid URI.");

        if (!uri.Scheme.Equals("otpauth", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("URI scheme must be 'otpauth'.");

        if (!uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Only TOTP (otpauth://totp/...) is supported.");

        var query = HttpUtility.ParseQueryString(uri.Query);

        var secret = query["secret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new FormatException("otpauth URI is missing the 'secret' parameter.");

        // Validate the secret is decodable now, so errors surface at setup time.
        _ = Base32.Decode(secret);

        // Label is "Issuer:Account" or just "Account"; it is URL-encoded.
        var label = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        string? labelIssuer = null, account = label;
        var colon = label.IndexOf(':');
        if (colon > 0)
        {
            labelIssuer = label[..colon].Trim();
            account = label[(colon + 1)..].Trim();
        }

        return new OtpAuthUri
        {
            Secret = secret.Replace(" ", "").ToUpperInvariant(),
            Issuer = query["issuer"] ?? labelIssuer,
            Account = string.IsNullOrEmpty(account) ? null : account,
            Digits = ParseInt(query["digits"], 6),
            Period = ParseInt(query["period"], 30),
            Algorithm = ParseAlgorithm(query["algorithm"]),
        };
    }

    /// <summary>Builds a <see cref="TotpGenerator"/> from this descriptor.</summary>
    public TotpGenerator CreateGenerator()
        => TotpGenerator.FromBase32(Secret, Digits, Period, Algorithm);

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var n) && n > 0 ? n : fallback;

    private static OtpAlgorithm ParseAlgorithm(string? value) => value?.ToUpperInvariant() switch
    {
        "SHA256" => OtpAlgorithm.Sha256,
        "SHA512" => OtpAlgorithm.Sha512,
        _ => OtpAlgorithm.Sha1,
    };
}
