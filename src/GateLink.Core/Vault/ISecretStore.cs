namespace GateLink.Core.Vault;

/// <summary>The protected part of a vault entry: TOTP seed + optional VPN password.</summary>
public sealed record VaultSecret(string Secret, string? VpnPassword);

/// <summary>
/// Platform abstraction for encrypted persistence of vault entries.
/// Windows: DPAPI (ProtectedData, per-user). macOS: AES-GCM file encrypted with
/// a key held in the Keychain behind a user-presence access control.
///
/// Callers must only read secrets after the gate unlocks. Metadata (issuer,
/// account, digits, period) may be loaded without unlocking.
/// </summary>
public interface ISecretStore
{
    Task<IReadOnlyList<VaultEntry>> LoadAllAsync(CancellationToken ct = default);
    Task SaveAsync(VaultEntry entry, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Loads the protected part of an entry. Stores that return metadata-only
    /// entries from <see cref="LoadAllAsync"/> implement this; simple stores can
    /// rely on the default (callers then use <see cref="VaultEntry.Secret"/>).
    /// </summary>
    Task<VaultSecret?> LoadSecretAsync(string id, CancellationToken ct = default)
        => Task.FromResult<VaultSecret?>(null);

    /// <summary>Drops cached decryption material when the vault relocks.</summary>
    void Lock() { }
}
