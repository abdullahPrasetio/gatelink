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

        // 2. Mount the DMG.
        progress?.Invoke("Installing update...");
        var mountPoint = await RunAsync("/usr/bin/hdiutil", $"attach \"{tmp}\" -nobrowse -readonly", ct);
        // Parse mount point from hdiutil output (last column of last line).
        var volPath = ParseMountPoint(mountPoint);
        if (volPath is null)
            return "Could not mount the update DMG.";

        try
        {
            // 3. Find the .app inside the mounted volume.
            var newApp = Directory.GetDirectories(volPath, "*.app").FirstOrDefault();
            if (newApp is null) return "No .app found in the update DMG.";

            // 4. Determine where the current app lives.
            var currentApp = ResolveCurrentAppBundle();
            if (currentApp is null)
                return "Could not determine current app location. Update manually.";

            // 5. Replace: remove old, copy new.
            progress?.Invoke("Replacing app...");
            var backup = currentApp + ".old";
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            Directory.Move(currentApp, backup);

            try
            {
                CopyDirectory(newApp, currentApp);
            }
            catch
            {
                // Roll back on failure.
                if (Directory.Exists(backup))
                {
                    if (Directory.Exists(currentApp)) Directory.Delete(currentApp, true);
                    Directory.Move(backup, currentApp);
                }
                throw;
            }

            // Clean up backup.
            try { Directory.Delete(backup, true); } catch { /* best effort */ }

            // 6. Relaunch the new app.
            progress?.Invoke("Relaunching...");
            Process.Start(new ProcessStartInfo("/usr/bin/open")
            {
                ArgumentList = { "-n", currentApp },
                UseShellExecute = false,
            });

            // Give the new process a moment to start, then exit this one.
            await Task.Delay(1000, ct);
            Environment.Exit(0);

            return "Updated!"; // unreachable, but satisfies return type
        }
        finally
        {
            // 7. Unmount.
            _ = RunAsync("/usr/bin/hdiutil", $"detach \"{volPath}\"", CancellationToken.None);
            try { File.Delete(tmp); } catch { /* temp cleanup */ }
        }
    }

    private static string? ResolveCurrentAppBundle()
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exe)) return null;
        // .../GateLink.app/Contents/MacOS/GateLink  →  .../GateLink.app
        var idx = exe.IndexOf(".app/", StringComparison.Ordinal);
        return idx < 0 ? null : exe[..(idx + 4)];
    }

    private static string? ParseMountPoint(string hdiutilOutput)
    {
        // hdiutil attach output: last line, last whitespace-delimited field is the volume path.
        foreach (var raw in hdiutilOutput.Split('\n').Reverse())
        {
            var line = raw.Trim();
            if (line.Contains("/Volumes/"))
            {
                var idx = line.IndexOf("/Volumes/", StringComparison.Ordinal);
                return line[idx..].Trim();
            }
        }
        return null;
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

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
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
