using System.Diagnostics;
using GateLink.Core.Vault;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Biometric gate for macOS. Genuine Touch ID verification requires the
/// LocalAuthentication framework (LAContext.evaluatePolicy). There is no
/// first-class CLI for it, so this gate shells out to a tiny Swift snippet via
/// <c>swift</c> when the toolchain is present. If Swift is unavailable, it falls
/// back to the system "administrator" authorization prompt (which itself offers
/// Touch ID on Macs that support it, otherwise a password) so the app stays
/// usable on any machine.
/// </summary>
public sealed class TouchIdGate : IBiometricGate
{
    public bool IsAvailable => OperatingSystem.IsMacOS();

    public async Task<bool> VerifyAsync(string reason, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsMacOS()) return true;

        // Preferred path: LocalAuthentication via a short Swift program.
        if (await TrySwiftLocalAuthAsync(reason, ct) is { } swiftResult)
            return swiftResult;

        // Fallback: security authorization prompt (Touch ID or password).
        return await TrySecurityAuthorizeAsync(ct);
    }

    private static async Task<bool?> TrySwiftLocalAuthAsync(string reason, CancellationToken ct)
    {
        var swift = FindSwift();
        if (swift is null) return null;

        // Evaluate deviceOwnerAuthentication: Touch ID with password fallback.
        var program = $$"""
            import LocalAuthentication
            import Foundation
            let ctx = LAContext()
            var err: NSError?
            let reason = "{{reason.Replace("\"", "'")}}"
            if ctx.canEvaluatePolicy(.deviceOwnerAuthentication, error: &err) {
                let sem = DispatchSemaphore(value: 0)
                var ok = false
                ctx.evaluatePolicy(.deviceOwnerAuthentication, localizedReason: reason) { success, _ in
                    ok = success; sem.signal()
                }
                sem.wait()
                exit(ok ? 0 : 1)
            } else {
                exit(2) // cannot evaluate (no biometrics/passcode configured)
            }
            """;

        try
        {
            var psi = new ProcessStartInfo(swift)
            {
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-");  // read program from stdin

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            await proc.StandardInput.WriteAsync(program);
            proc.StandardInput.Close();
            await proc.WaitForExitAsync(ct);

            // 0 = verified, 1 = user cancelled/failed, 2 = cannot evaluate.
            return proc.ExitCode switch
            {
                0 => true,
                1 => false,
                _ => null, // cannot evaluate -> let caller fall back
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> TrySecurityAuthorizeAsync(CancellationToken ct)
    {
        // security authorize -u <right> prompts for authorization (Touch ID or
        // password). We use a standard right that triggers the prompt.
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/security")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("authorize");
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add("system.privilege.admin");

            using var proc = Process.Start(psi);
            if (proc is null) return false;
            await proc.WaitForExitAsync(ct);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindSwift()
    {
        foreach (var p in new[] { "/usr/bin/swift", "/usr/local/bin/swift" })
            if (File.Exists(p)) return p;
        return null;
    }
}
