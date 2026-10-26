using System.Diagnostics;
using System.Text;
using GateLink.Core;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Installs and probes the root-owned fixed-purpose tunnel helper. The helper
/// is the only command authorized by /etc/sudoers.d/gatelink; it never accepts
/// arbitrary options, scripts, or paths from this process.
/// </summary>
public static class TunnelHelper
{
    public const string HelperDirectory = "/usr/local/libexec/gatelink";
    public const string HelperPath = HelperDirectory + "/gatelink-tunnel";
    public const string VersionPath = HelperDirectory + "/VERSION";
    public const string SudoersPath = "/etc/sudoers.d/gatelink";
    public const string ExpectedVersion = "4";

    /// <summary>Helper files and the sudoers rule are present and current.</summary>
    public static bool IsInstalled
    {
        get
        {
            try
            {
                return File.Exists(HelperPath)
                       && File.Exists(SudoersPath)
                       && ReadVersion() == ExpectedVersion;
            }
            catch
            {
                return false;
            }
        }
    }

    public static string? ReadVersion()
    {
        try
        {
            return File.Exists(VersionPath) ? File.ReadAllText(VersionPath).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Ensures the helper exists, is current, and that the sudoers rule actually
    /// lets us invoke it. The portal is not stored here: it comes from the UI
    /// form and is validated by the wrapper at connect time.
    /// </summary>
    public static async Task<bool> EnsureInstalledAsync()
    {
        if (!OperatingSystem.IsMacOS()) return true;

        if (IsInstalled && await ProbeAsync())
            return true;

        var installer = FindInstaller();
        if (installer is null) return false;

        var args = "--user " + ShQuote(Environment.UserName);
        if (!await RunInstallerAsync(installer, args))
            return false;

        return IsInstalled && await ProbeAsync();
    }

    /// <summary>Removes the helper and its sudoers rule via the admin prompt.</summary>
    public static async Task<bool> UninstallAsync()
    {
        if (!OperatingSystem.IsMacOS()) return true;

        var installer = FindInstaller();
        if (installer is null) return false;
        return await RunInstallerAsync(installer, "--uninstall");
    }

    /// <summary>
    /// Probes the exact command sudoers authorizes. `version` exits 0 without
    /// touching the network, so this only proves the rule + helper work.
    /// </summary>
    private static async Task<bool> ProbeAsync()
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
            psi.ArgumentList.Add(HelperPath);
            psi.ArgumentList.Add("version");

            using var proc = Process.Start(psi);
            if (proc is null) return false;
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 && stdout.Trim() == ExpectedVersion;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindInstaller()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "install-tunnel-helper.sh"),
            Path.Combine(AppContext.BaseDirectory, "..", "Resources", "install-tunnel-helper.sh"),
            Path.Combine(Environment.CurrentDirectory, "scripts", "install-tunnel-helper.sh"),
        };

        // Development fallback: walk up from bin/<config>/<tfm>/.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "scripts", "install-tunnel-helper.sh"));

        foreach (var candidate in candidates)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full)) return full;
            }
            catch
            {
                // Ignore malformed paths.
            }
        }
        return null;
    }

    private static async Task<bool> RunInstallerAsync(string installer, string arguments)
    {
        try
        {
            var shell = $"/bin/bash {ShQuote(installer)} {arguments}";
            var appleScript =
                "do shell script \"" +
                shell.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") +
                "\" with administrator privileges";

            var psi = new ProcessStartInfo("/usr/bin/osascript")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(appleScript);

            using var proc = Process.Start(psi);
            if (proc is null) return false;
            await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ShQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
