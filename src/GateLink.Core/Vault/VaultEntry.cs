using GateLink.Core.Otp;

namespace GateLink.Core.Vault;

/// <summary>
/// A stored 2FA account: the TOTP descriptor plus an optional VPN password.
/// The secret and password are sensitive and must be persisted encrypted by an
/// <see cref="ISecretStore"/>; this record is the in-memory, unlocked view.
///
/// Mirrors the per-account model behind the macOS app's AddAccountView /
/// EditItemView / AccountRowView.
/// </summary>
public sealed record VaultEntry
{
    public required string Id { get; init; }
    public required string Issuer { get; init; }
    public required string Account { get; init; }

    /// <summary>Base32 TOTP secret.</summary>
    public required string Secret { get; init; }

    public int Digits { get; init; } = 6;
    public int Period { get; init; } = 30;
    public OtpAlgorithm Algorithm { get; init; } = OtpAlgorithm.Sha1;

    /// <summary>Optional VPN password associated with this account.</summary>
    public string? VpnUsername { get; init; }
    public string? VpnPassword { get; init; }

    public TotpGenerator CreateGenerator()
        => TotpGenerator.FromBase32(Secret, Digits, Period, Algorithm);

    /// <summary>Builds an entry from a parsed otpauth URI (QR scan or manual).</summary>
    public static VaultEntry FromOtpAuth(OtpAuthUri uri, string? id = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString("N"),
        Issuer = uri.Issuer ?? "VPN",
        Account = uri.Account ?? "account",
        Secret = uri.Secret,
        Digits = uri.Digits,
        Period = uri.Period,
        Algorithm = uri.Algorithm,
    };
}
