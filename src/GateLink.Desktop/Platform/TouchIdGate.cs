using System.Diagnostics;
using GateLink.Core.Vault;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Touch ID prompt via a precompiled LocalAuthentication helper. The helper
/// receives the reason as argv (no source interpolation). If it is missing or
/// the Mac has no Touch ID, the caller falls back to the Keychain ACL prompt.
/// </summary>
public sealed class TouchIdGate : IBiometricGate
{
    public bool IsAvailable => OperatingSystem.IsMacOS();

    public async Task<bool> VerifyAsync(string reason, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsMacOS()) return true;

        var helper = FindHelper();
        if (helper is null) return true;   // let the Keychain gate handle it

        try
        {
            var psi = new ProcessStartInfo(helper)
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(reason);

            using var proc = Process.Start(psi);
            if (proc is null) return true;

            await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            // 0 = verified, 1 = cancelled/failed, 2 = cannot evaluate -> Keychain.
            return proc.ExitCode switch
            {
                0 => true,
                1 => false,
                _ => true,
            };
        }
        catch
        {
            return true;
        }
    }

    private static string? FindHelper()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "Assets", "GateLinkAuth"),
                     Path.Combine(AppContext.BaseDirectory, "GateLinkAuth"),
                 })
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
