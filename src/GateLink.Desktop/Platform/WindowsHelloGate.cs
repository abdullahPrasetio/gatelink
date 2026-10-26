#if WINDOWS
using GateLink.Core.Vault;
using Windows.Security.Credentials.UI;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Windows Hello / PIN gate via UserConsentVerifier. If the device has no
/// Windows Hello configured, the vault still uses DPAPI at rest; the gate
/// permits the action so the app remains usable on such machines.
/// </summary>
public sealed class WindowsHelloGate : IBiometricGate
{
    public bool IsAvailable => OperatingSystem.IsWindows();

    public async Task<bool> VerifyAsync(string reason, CancellationToken ct = default)
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            if (availability != UserConsentVerifierAvailability.Available)
                return true;   // DPAPI still protects the vault at rest

            var result = await UserConsentVerifier.RequestVerificationAsync(reason);
            return result == UserConsentVerificationResult.Verified;
        }
        catch
        {
            return false;
        }
    }
}
#endif
