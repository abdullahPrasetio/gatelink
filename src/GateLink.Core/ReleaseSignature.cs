using System.Security.Cryptography;

namespace GateLink.Core;

/// <summary>
/// Pinned RSA public key for macOS release artifacts. The DMG signature is
/// produced offline/CI with the private key (never in this repo); the app
/// verifies it before mounting or replacing itself. Unsigned releases are
/// downloaded manually instead of auto-installed.
/// </summary>
public static class ReleaseSignature
{
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAzs9vmJrp/8t941P6Qd10
        ucg6CVg0fLm/KAQXKDicpARdhRRqsIH4kuONZVf7rvl+xBn9hmHzwjEvNyyI1E96
        rjQv72G1UxzTLn0PnTqtgl7vvO59ktnRcqUwvFBQiPdtpgR/t+L/wBFWzbde33yw
        xnXfxK5jvDbJBRX/wZtbmw42Jgbfzk/aBFX8vUFBWSo2eIvkdvCBRIJljGDekSyI
        +TNcgp2lpFO2tg/o3gSnUhuCGWNtGnhzgUJHxqD6OAQyBvQa8Oo/b8U0baDR/Nvf
        SQXWKoEdV/dvWlH9A2FAHVpMrW6Q8rUE6yyj2jtcB/RJKTHcNyFedt1rr9z7NBI7
        IrhMswSLyRaNRmyguterHt8talWeNy0MgqhQf3pUOwp3Vfi7m9fT/8yE09HjunIK
        xy4uNXuivkUGXOOKzMbiVztCE/TLAdjEcnTH0eRN23/GW09pKnK6MvapLn0r1rjN
        DVUkMsZSjIwGFj1nARovfO5vEEADjtt3ybGB9VOKabzzAgMBAAE=
        -----END PUBLIC KEY-----
        """;

    /// <summary>Verifies a PKCS#1 v1.5 RSA-SHA256 signature over a SHA-256 hash.</summary>
    public static bool Verify(ReadOnlySpan<byte> sha256Hash, ReadOnlySpan<byte> signature)
        => Verify(sha256Hash, signature, PublicKeyPem);

    public static bool Verify(ReadOnlySpan<byte> sha256Hash, ReadOnlySpan<byte> signature, string publicKeyPem)
    {
        if (sha256Hash.Length != 32 || signature.Length == 0) return false;

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyHash(
                sha256Hash,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }
}
