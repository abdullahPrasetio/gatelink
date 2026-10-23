namespace GateLink.Core.Vault;

/// <summary>
/// Platform abstraction for user-presence verification (Touch ID on macOS,
/// Windows Hello on Windows), with a password fallback handled by the OS.
/// Implementations return only a yes/no result; they never hand back a key.
/// The actual secret decryption is gated on that result via
/// <see cref="ISecretStore"/>.
/// </summary>
public interface IBiometricGate
{
    /// <summary>Whether biometric/credential verification is available.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Prompts the user to verify (biometric, falling back to password/PIN).
    /// Returns true only if the OS confirms the user.
    /// </summary>
    Task<bool> VerifyAsync(string reason, CancellationToken ct = default);
}
