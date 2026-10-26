using GateLink.Core.Vault;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Placeholder gate that approves without prompting, so the UI is runnable
/// during development. Replace with the platform biometric gate
/// and WindowsHelloGate (Windows, UserConsentVerifier).
/// </summary>
public sealed class StubBiometricGate : IBiometricGate
{
    public bool IsAvailable => true;
    public Task<bool> VerifyAsync(string reason, CancellationToken ct = default) => Task.FromResult(true);
}
