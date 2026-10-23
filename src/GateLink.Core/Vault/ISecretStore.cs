namespace GateLink.Core.Vault;

/// <summary>
/// Platform abstraction for encrypted persistence of vault entries.
/// Windows: DPAPI (ProtectedData, per-user). macOS: Keychain with an access
/// control that requires biometric/user presence. The store is responsible for
/// encryption at rest; callers must only reach it after the gate unlocks.
/// </summary>
public interface ISecretStore
{
    Task<IReadOnlyList<VaultEntry>> LoadAllAsync(CancellationToken ct = default);
    Task SaveAsync(VaultEntry entry, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}
