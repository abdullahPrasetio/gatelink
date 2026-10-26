using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using GateLink.Core;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Downloads a signed release and replaces the running app bundle.
///
/// macOS: the DMG must have a matching GateLink.dmg.sig asset signed by the
/// pinned key in <see cref="ReleaseSignature"/>. The signature is verified over
/// the downloaded bytes before the DMG is mounted. A compromised release or
/// tampered update is refused; the user can still download it manually.
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

        // Windows: open the release page. Full self-update needs replacing a
        // running .exe and an Authenticode-signed installer.
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
        var signatureUrl = release.MacSignatureUrl;
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(signatureUrl))
        {
            if (!string.IsNullOrEmpty(release.HtmlUrl)) OpenBrowser(release.HtmlUrl);
            return "This release is not signed for auto-update. Opened the download page.";
        }

        progress?.Invoke("Downloading update...");
        var tempDir = Directory.CreateTempSubdirectory("gatelink-update-");
        var dmgPath = Path.Combine(tempDir.FullName, "GateLink.dmg");

        byte[] hash;
        try
        {
            hash = await DownloadAndHashAsync(url, dmgPath, ct);

            progress?.Invoke("Verifying update...");
            var signature = await DownloadBytesAsync(signatureUrl, ct);
            if (!ReleaseSignature.Verify(hash, signature))
                throw new InvalidOperationException(
                    "Update signature is missing or invalid; refusing to install. Download it manually from the release page.");
        }
        catch
        {
            TryDeleteDirectory(tempDir.FullName);
            throw;
        }

        var currentApp = ResolveCurrentAppBundle();
        if (currentApp is null)
        {
            // Not running from a bundle (e.g. dotnet run) — just open the DMG.
            await RunAsync("/usr/bin/open", $"\"{dmgPath}\"", ct);
            return "Opened the update DMG. Drag GateLink into Applications.";
        }

        progress?.Invoke("Installing update...");

        var pid = Environment.ProcessId;
        var script = Path.Combine(tempDir.FullName, "update.sh");
        var scriptBody = $$"""
            #!/bin/bash
            set -e
            DMG={{ShQuote(dmgPath)}}
            APP={{ShQuote(currentApp)}}
            PID={{pid}}
            SCRIPT={{ShQuote(script)}}
            TMP={{ShQuote(tempDir.FullName)}}
            MP=""

            cleanup() {
              if [ -n "$MP" ]; then hdiutil detach "$MP" >/dev/null 2>&1 || true; fi
            }
            trap cleanup EXIT

            # 1. Wait for GateLink to quit (max ~15s).
            for i in $(seq 1 60); do
              kill -0 "$PID" 2>/dev/null || break
              sleep 0.25
            done

            # 2. Mount the verified DMG and find the new app.
            MP=$(hdiutil attach "$DMG" -nobrowse -readonly | grep -o '/Volumes/.*' | tail -1)
            if [ -z "$MP" ]; then exit 1; fi
            NEW=$(ls -d "$MP"/*.app 2>/dev/null | head -1)
            if [ -z "$NEW" ]; then exit 1; fi

            BUNDLE_ID=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$NEW/Contents/Info.plist" 2>/dev/null || true)
            if [ "$BUNDLE_ID" != "com.gatelink.app" ]; then exit 1; fi
            if ! codesign --verify --deep "$NEW" >/dev/null 2>&1; then exit 1; fi

            # 3. Swap with rollback.
            rm -rf "$APP.bak"
            mv "$APP" "$APP.bak"
            if ! cp -R "$NEW" "$APP"; then
              rm -rf "$APP"
              mv "$APP.bak" "$APP"
              exit 1
            fi
            rm -rf "$APP.bak"

            # 4. Relaunch and clean up.
            hdiutil detach "$MP" >/dev/null 2>&1 || true
            MP=""
            rm -f "$DMG"
            open "$APP"
            rm -f "$SCRIPT"
            rmdir "$TMP" >/dev/null 2>&1 || true
            """;

        await File.WriteAllTextAsync(script, scriptBody, ct);
        await RunAsync("/bin/chmod", $"+x \"{script}\"", ct);

        // Launch detached so it survives this process exiting.
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

    private static async Task<byte[]> DownloadAndHashAsync(
        string url,
        string destination,
        CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GateLink-Updater/2.0");

        await using var stream = await http.GetStreamAsync(url, ct);
        await using var file = File.Create(destination);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            hash.AppendData(buffer, 0, read);
        }
        return hash.GetHashAndReset();
    }

    private static async Task<byte[]> DownloadBytesAsync(string url, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(1) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GateLink-Updater/2.0");
        return await http.GetByteArrayAsync(url, ct);
    }

    private static string? ResolveCurrentAppBundle()
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exe)) return null;

        // .../GateLink.app/Contents/MacOS/GateLink  ->  .../GateLink.app
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

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }

    private static string ShQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
