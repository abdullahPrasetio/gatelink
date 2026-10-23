using System.Diagnostics;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Manages "Open at Login" on macOS by installing a per-user LaunchAgent that
/// relaunches the app bundle at login. This is a real implementation, not a
/// decorative toggle: it writes ~/Library/LaunchAgents/com.gatelink.app.plist
/// and loads/unloads it with launchctl.
/// </summary>
public static class LoginItem
{
    private const string Label = "com.gatelink.app";

    private static string PlistPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "LaunchAgents", Label + ".plist");
        }
    }

    public static bool IsEnabled => OperatingSystem.IsMacOS() && File.Exists(PlistPath);

    /// <summary>Enables or disables launch-at-login. No-op off macOS.</summary>
    public static void Set(bool enabled)
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (enabled) Enable();
        else Disable();
    }

    private static void Enable()
    {
        var appPath = ResolveAppBundlePath();
        if (appPath is null) return;

        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);

        var plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>{Label}</string>
              <key>ProgramArguments</key>
              <array>
                <string>/usr/bin/open</string>
                <string>{appPath}</string>
              </array>
              <key>RunAtLoad</key><true/>
            </dict>
            </plist>
            """;

        File.WriteAllText(PlistPath, plist);
        RunLaunchctl("load", PlistPath);
    }

    private static void Disable()
    {
        if (File.Exists(PlistPath))
        {
            RunLaunchctl("unload", PlistPath);
            try { File.Delete(PlistPath); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Resolves the running .app bundle path (…/GateLink.app). Returns null when
    /// running from `dotnet run` (no bundle), in which case login-at-start isn't
    /// meaningful yet.
    /// </summary>
    private static string? ResolveAppBundlePath()
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exe)) return null;

        // …/GateLink.app/Contents/MacOS/GateLink  ->  …/GateLink.app
        var idx = exe.IndexOf(".app/", StringComparison.Ordinal);
        if (idx < 0) return null;
        return exe[..(idx + 4)];
    }

    private static void RunLaunchctl(string verb, string path)
    {
        try
        {
            var psi = new ProcessStartInfo("/bin/launchctl")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(verb);
            psi.ArgumentList.Add(path);
            Process.Start(psi)?.WaitForExit();
        }
        catch { /* best effort */ }
    }
}
