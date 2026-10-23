using System.Security.Cryptography;

namespace GateLink.Core.Otp;

public enum OtpAlgorithm { Sha1, Sha256, Sha512 }

/// <summary>
/// Time-based One-Time Password generator (RFC 6238 / RFC 4226 HOTP).
/// Pure, platform-neutral math — the same code that drives Google
/// Authenticator-style 6-digit codes. This is GateLink's <c>otpProvider</c>:
/// the app generates the OTP itself instead of asking the user to type it.
/// </summary>
public sealed class TotpGenerator
{
    private readonly byte[] _secret;
    private readonly int _digits;
    private readonly int _periodSeconds;
    private readonly OtpAlgorithm _algorithm;

    public TotpGenerator(
        byte[] secret,
        int digits = 6,
        int periodSeconds = 30,
        OtpAlgorithm algorithm = OtpAlgorithm.Sha1)
    {
        if (secret.Length == 0) throw new ArgumentException("Secret is empty.", nameof(secret));
        if (digits is < 6 or > 8) throw new ArgumentOutOfRangeException(nameof(digits));
        if (periodSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(periodSeconds));

        _secret = secret;
        _digits = digits;
        _periodSeconds = periodSeconds;
        _algorithm = algorithm;
    }

    public static TotpGenerator FromBase32(
        string base32Secret,
        int digits = 6,
        int periodSeconds = 30,
        OtpAlgorithm algorithm = OtpAlgorithm.Sha1)
        => new(Base32.Decode(base32Secret), digits, periodSeconds, algorithm);

    /// <summary>Generates the code for the given UTC time (defaults to now).</summary>
    public string Generate(DateTimeOffset? at = null)
    {
        var now = at ?? DateTimeOffset.UtcNow;
        var counter = now.ToUnixTimeSeconds() / _periodSeconds;
        return GenerateForCounter(counter);
    }

    /// <summary>Seconds remaining before the current code rolls over.</summary>
    public int SecondsRemaining(DateTimeOffset? at = null)
    {
        var now = at ?? DateTimeOffset.UtcNow;
        return _periodSeconds - (int)(now.ToUnixTimeSeconds() % _periodSeconds);
    }

    private string GenerateForCounter(long counter)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        byte[] hash = _algorithm switch
        {
            OtpAlgorithm.Sha1 => HMACSHA1.HashData(_secret, counterBytes.ToArray()),
            OtpAlgorithm.Sha256 => HMACSHA256.HashData(_secret, counterBytes.ToArray()),
            OtpAlgorithm.Sha512 => HMACSHA512.HashData(_secret, counterBytes.ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(_algorithm)),
        };

        // Dynamic truncation (RFC 4226 §5.3).
        int offset = hash[^1] & 0x0F;
        int binary =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        int otp = binary % (int)Math.Pow(10, _digits);
        return otp.ToString().PadLeft(_digits, '0');
    }
}
