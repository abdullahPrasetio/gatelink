using System.Diagnostics;
using System.Net.Http;
using GateLink.Core;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Downloads a new release and replaces the running app bundle. macOS-specific:
/// downloads the DMG, mounts it, copies the new .app over the running one, then
/// unmounts and relaunches. On failure, the old app remains untouched.
/// </summary>
public static class AutoUpdater
{
    public static async Task<string> UpdateAsync(
        ReleaseInfo release,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        if (OperatingSystem.IsMacOS())
            return await UpdateMacAsync(release, onProgress, ct);

        // Windows: for now, open the browser to the release page. Full
        // self-update on Windows needs replacing a running .exe (harder).
        if (!string.IsNullOrEmpty(release.HtmlUrl))
            OpenBrowser(release.HtmlUrl);
        return "Opened download page. Install manually.";
    }

    private static async Task<string> UpdateMacAsync(
        ReleaseInfo release,
        Action<string>? progress,
        CancellationToken ct)
    {
        var url = release.MacAssetUrl;
        if (string.IsNullOrEmpty(url))
        {
            // Fallback: open browser to the release page.
            if (!string.IsNullOrEmpty(release.HtmlUrl)) OpenBrowser(release.HtmlUrl);
            return "No DMG found in release. Opened download page.";
        }

        // 1. Download DMG to a temp location.
        progress?.Invoke("Downloading update...");
        var tmp = Path.Combine(Path.GetTempPath(), "GateLink-update.dmg");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GateLink-Updater/1.0");
            await using var stream = await http.GetStreamAsync(url, ct);
            await using var file = File.Create(tmp);
            await stream.CopyToAsync(file, ct);
        }

        var currentApp = ResolveCurrentAppBundle();
        if (currentApp is null)
        {
            // Not running from a bundle (e.g. dotnet run) — just open the DMG.
            await RunAsync("/usr/bin/open", $"\"{tmp}\"", ct);
            return "Opened the update DMG. Drag GateLink into Applications.";
        }

        // Replacing a running .app from inside itself is unreliable (the bundle
        // is in use). Instead we hand off to a detached shell script that waits
        // for this process to quit, swaps the bundle, and relaunches. Then we
        // quit immediately.
        progress?.Invoke("Installing update...");

        var pid = Environment.ProcessId;
        var script = Path.Combine(Path.GetTempPath(), "gatelink-update.sh");
        var scriptBody = $$"""
            #!/bin/bash
            set -e
            DMG="{{tmp}}"
            APP="{{currentApp}}"
            PID={{pid}}

            # 1. Wait for GateLink to quit (max ~15s).
            for i in $(seq 1 60); do
              kill -0 "$PID" 2>/dev/null || break
              sleep 0.25
            done

            # 2. Mount, copy the new app over the old, unmount.
            MP=$(hdiutil attach "$DMG" -nobrowse -readonly | grep -o '/Volumes/.*' | tail -1)
            NEW=$(ls -d "$MP"/*.app 2>/dev/null | head -1)
            if [ -n "$NEW" ]; then
              rm -rf "$APP.bak"
              mv "$APP" "$APP.bak" 2>/dev/null || true
              cp -R "$NEW" "$APP"
              rm -rf "$APP.bak"
            fi
            hdiutil detach "$MP" >/dev/null 2>&1 || true
            rm -f "$DMG"

            # 3. Relaunch.
            open "$APP"
            rm -f "{{script}}"
            """;
        await File.WriteAllTextAsync(script, scriptBody, ct);
        await RunAsync("/bin/chmod", $"+x \"{script}\"", ct);

        // Launch the updater detached so it survives this process exiting.
        Process.Start(new ProcessStartInfo("/bin/bash")
        {
            ArgumentList = { script },
            UseShellExecute = false,
        });

        progress?.Invoke("Restarting...");
        await Task.Delay(500, ct);
        Environment.Exit(0);
        return "Updating...";
    }

    private static string? ResolveCurrentAppBundle()
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exe)) return null;
        // .../GateLink.app/Contents/MacOS/GateLink  →  .../GateLink.app
        var idx = exe.IndexOf(".app/", StringComparison.Ordinal);
        return idx < 0 ? null : exe[..(idx + 4)];
    }

    private static async Task<string> RunAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return output;
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("/usr/bin/open", url) { UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }
}
