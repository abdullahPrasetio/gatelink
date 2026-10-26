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

    // The sudoers file is root-only (0440) and not readable by the app, so we
    // keep a user-readable marker recording which openconnect path was last
    // authorized. This lets us detect a stale rule (e.g. after switching from
    // Homebrew to the bundled binary) without needing to read the sudoers file.
    private static string MarkerPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GateLink", "sudoers-authorized-path.txt");

    /// <summary>
    /// True only if the rule was installed for exactly this openconnect path.
    /// </summary>
    public static bool IsInstalledFor(string openconnectPath)
    {
        try
        {
            if (!File.Exists(SudoersPath)) return false;
            if (!File.Exists(MarkerPath)) return false;
            var authorized = File.ReadAllText(MarkerPath).Trim();
            return string.Equals(authorized, openconnectPath, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensures a sudoers rule exists that authorizes the CURRENT openconnect
    /// path. Reinstalls if missing or pointing at a different binary. Returns
    /// true if the rule is in place, false if the user cancelled or it failed.
    /// </summary>
    public static async Task<bool> EnsureInstalledAsync(VpnEnvironment env)
    {
        if (!OperatingSystem.IsMacOS()) return true;   // Windows uses elevation instead

        var openconnect = env.OpenConnectPath;
        if (string.IsNullOrEmpty(openconnect)) return false;   // can't build a rule without it

        // Already authorized for this exact binary → nothing to do.
        if (IsInstalledFor(openconnect)) return true;

        // The marker can be stale or absent when the rule was installed by the
        // script or for another copy of the app. Probe first so an already
        // authorized binary never triggers a needless admin prompt.
        if (IsInstalled && await CanRunWithoutPasswordAsync(openconnect))
        {
            await WriteMarkerAsync(openconnect);
            return true;
        }

        var user = Environment.UserName;
        var killBin = File.Exists("/bin/kill") ? "/bin/kill" : "/usr/bin/kill";

        // The rule content (restricted to the specific openconnect + kill paths).
        // Actual newlines + printf %s avoid printf interpreting '%' or '\' that
        // can appear in a path.
        static string ShQuote(string value) =>
            "'" + value.Replace("'", "'\\''") + "'";

        var ruleText =
            "# Installed by GateLink\n" +
            $"{user} ALL=(root) NOPASSWD: {openconnect}\n" +
            $"{user} ALL=(root) NOPASSWD: {killBin}\n";

        // A shell command that writes the rule to a temp file, validates it with
        // visudo, then installs it with correct ownership/permissions. Absolute
        // paths because `do shell script` runs with a minimal PATH. `set -e`
        // plus the EXIT trap makes osascript report a non-zero status when
        // visudo/install fails; previously the trailing `rm` masked it.
        var shell =
            "set -e; tmp=$(/usr/bin/mktemp); " +
            "trap '/bin/rm -f \"$tmp\"' EXIT; " +
            $"printf %s {ShQuote(ruleText)} > \"$tmp\"; " +
            $"/usr/sbin/visudo -cf \"$tmp\"; " +
            $"/usr/bin/install -m 0440 -o root -g wheel \"$tmp\" {SudoersPath}";

        // Run it elevated via osascript — this shows the native admin dialog.
        // Newlines must be escaped for AppleScript, which turns \n back into a
        // real newline inside the shell's single-quoted argument.
        var appleScript =
            "do shell script \"" +
            shell.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") +
            "\" with administrator privileges";

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

            if (proc.ExitCode == 0 && IsInstalled && await CanRunWithoutPasswordAsync(openconnect))
            {
                // Record which openconnect path we authorized, so later launches
                // know the rule is current. Written only after a real probe, so
                // a stale/unrelated rule can never be mistaken for success.
                await WriteMarkerAsync(openconnect);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task WriteMarkerAsync(string openconnect)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            await File.WriteAllTextAsync(MarkerPath, openconnect);
        }
        catch { /* marker is best-effort */ }
    }

    /// <summary>
    /// Probes the exact binary GateLink will launch: succeeds only when the
    /// installed sudoers rule lets this user run it without a password.
    /// </summary>
    private static async Task<bool> CanRunWithoutPasswordAsync(string openconnect)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/sudo")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add(openconnect);
            psi.ArgumentList.Add("--version");

            using var proc = Process.Start(psi);
            if (proc is null) return false;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
