using System.Diagnostics;
using GateLink.Core;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Installs the sudoers rule that lets GateLink run openconnect as root without
/// a password prompt on every connect — all from inside the app, no Terminal.
///
/// It shows the native macOS admin prompt ("GateLink wants to make changes")
/// via osascript's "with administrator privileges", writes a validated rule to
/// /etc/sudoers.d/gatelink, and is a no-op if the rule already exists.
///
/// Security note: NOPASSWD for openconnect is the same trade-off the reference
/// app makes. The rule is restricted to the specific openconnect path.
/// </summary>
public static class SudoersSetup
{
    private const string SudoersPath = "/etc/sudoers.d/gatelink";

    public static bool IsInstalled => File.Exists(SudoersPath);

    /// <summary>
    /// Ensures the sudoers rule exists. Returns true if already present or newly
    /// installed, false if the user cancelled or it failed. macOS only.
    /// </summary>
    public static async Task<bool> EnsureInstalledAsync(VpnEnvironment env)
    {
        if (!OperatingSystem.IsMacOS()) return true;   // Windows uses elevation instead
        if (IsInstalled) return true;

        var openconnect = env.OpenConnectPath;
        if (string.IsNullOrEmpty(openconnect)) return false;   // can't build a rule without it

        var user = Environment.UserName;
        var killBin = File.Exists("/bin/kill") ? "/bin/kill" : "/usr/bin/kill";

        // The rule content (restricted to the specific openconnect + kill paths).
        var rule =
            $"# Installed by GateLink\\n" +
            $"{user} ALL=(root) NOPASSWD: {openconnect}\\n" +
            $"{user} ALL=(root) NOPASSWD: {killBin}\\n";

        // A shell command that writes the rule to a temp file, validates it with
        // visudo, then installs it with correct ownership/permissions.
        var shell =
            $"tmp=$(mktemp); printf '{rule}' > \"$tmp\"; " +
            $"visudo -cf \"$tmp\" && install -m 0440 -o root -g wheel \"$tmp\" {SudoersPath}; " +
            $"rm -f \"$tmp\"";

        // Run it elevated via osascript — this shows the native admin dialog.
        var appleScript =
            $"do shell script \"{shell.Replace("\"", "\\\"")}\" with administrator privileges";

        try
        {
            var psi = new ProcessStartInfo("/usr/bin/osascript")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(appleScript);

            using var proc = Process.Start(psi)!;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 && IsInstalled;
        }
        catch
        {
            return false;
        }
    }
}
