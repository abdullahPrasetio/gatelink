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
    private readonly TouchIdGate _touchId = new();

    public VaultKeyGate(EncryptedFileSecretStore store) => _store = store;

    public bool IsAvailable => OperatingSystem.IsMacOS();

    public async Task<bool> VerifyAsync(string reason, CancellationToken ct = default)
    {
        // Touch ID first; the Keychain read follows. After the user chooses
        // Always Allow once, only the Touch ID prompt remains.
        if (!await _touchId.VerifyAsync(reason, ct)) return false;
        return await _store.UnlockAsync(ct);
    }
}
