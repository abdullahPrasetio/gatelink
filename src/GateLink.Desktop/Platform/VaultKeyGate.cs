using GateLink.Core.Vault;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Biometric gate backed by the vault key item's Keychain access control.
/// Reading the key triggers the macOS userPresence prompt (Touch ID or login
/// password); the key is then cached by the store for the unlock window.
/// </summary>
public sealed class VaultKeyGate : IBiometricGate
{
    private readonly EncryptedFileSecretStore _store;

    public VaultKeyGate(EncryptedFileSecretStore store) => _store = store;

    public bool IsAvailable => OperatingSystem.IsMacOS();

    public Task<bool> VerifyAsync(string reason, CancellationToken ct = default)
        => _store.UnlockAsync(ct);
}
