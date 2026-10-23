using System.IO;
using System.Runtime.InteropServices;

namespace GateLink.Core;

/// <summary>
/// Resolves external tooling (openconnect) and runtime locations (logs, pid,
/// runtime dir). Cross-platform: on Windows it searches PATH plus the usual
/// install folders and uses %ProgramData%; on macOS/Linux it searches PATH plus
/// the common Homebrew/system prefixes, matching where openconnect normally
/// lives (/opt/homebrew/bin, /usr/local/bin).
/// </summary>
public sealed class VpnEnvironment
{
    private static readonly bool IsWindows =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string[] CandidateOpenConnectNames => IsWindows
        ? new[] { "openconnect.exe", "openconnect" }
        : new[] { "openconnect" };

    // Well-known install locations per platform, searched after PATH.
    // OpenConnect-GUI installs openconnect.exe under a bin\ subfolder, so we
    // list both the base folders and their bin\ children.
    private static string[] CandidateDirectories => IsWindows
        ? new[]
        {
            @"C:\Program Files\OpenConnect",
            @"C:\Program Files\OpenConnect\bin",
            @"C:\Program Files (x86)\OpenConnect",
            @"C:\Program Files (x86)\OpenConnect\bin",
            @"C:\Program Files\OpenConnect-GUI",
            @"C:\Program Files\OpenConnect-GUI\bin",
            @"C:\Program Files (x86)\OpenConnect-GUI",
            @"C:\Program Files (x86)\OpenConnect-GUI\bin",
            @"C:\Program Files\OpenVPN\bin",
        }
        : new[]
        {
            "/opt/homebrew/bin",   // Apple Silicon Homebrew
            "/usr/local/bin",      // Intel Homebrew / manual installs
            "/usr/bin",
        };

    public string RuntimeDirectory { get; }
    public string LogPath { get; }
    public string PidPath { get; }

    /// <summary>
    /// Path to the HIP report script passed to openconnect via --csd-wrapper,
    /// or null if not available. GlobalProtect gateways that enforce Host
    /// Integrity Protection reject/limit the tunnel without it.
    /// </summary>
    public string? HipReportScriptPath { get; }

    public VpnEnvironment()
    {
        RuntimeDirectory = Path.Combine(ResolveRuntimeBase(), VpnConfig.ProductName);
        Directory.CreateDirectory(RuntimeDirectory);

        LogPath = Path.Combine(RuntimeDirectory, "openconnect.log");
        PidPath = Path.Combine(RuntimeDirectory, "openconnect.pid");
        HipReportScriptPath = ResolveHipReportScript();
    }

    /// <summary>
    /// Finds the bundled hipreport.sh and copies it into the runtime directory
    /// with executable permissions, so openconnect can run it. Looks next to the
    /// executable (app bundle Assets) and in common locations.
    /// </summary>
    private string? ResolveHipReportScript()
    {
        if (IsWindows) return null;   // HIP wrapper is a shell script (macOS/Linux)

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Assets", "hipreport.sh"),
                Path.Combine(baseDir, "hipreport.sh"),
            };

            foreach (var src in candidates)
            {
                if (!File.Exists(src)) continue;
                var dst = Path.Combine(RuntimeDirectory, "hipreport.sh");
                File.Copy(src, dst, overwrite: true);
                MakeExecutable(dst);
                return dst;
            }
        }
        catch
        {
            // fall through: no HIP wrapper available
        }
        return null;
    }

    private static void MakeExecutable(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/chmod")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("+x");
            psi.ArgumentList.Add(path);
            System.Diagnostics.Process.Start(psi)?.WaitForExit();
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Base directory for logs/pid. On Windows this is %ProgramData%; on
    /// macOS/Linux CommonApplicationData maps to a non-writable location for a
    /// normal user, so fall back to the per-user local app data directory.
    /// </summary>
    private static string ResolveRuntimeBase()
    {
        if (IsWindows)
            return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(local)
            ? Path.Combine(Path.GetTempPath(), VpnConfig.ProductName + "-runtime")
            : local;
    }

    /// <summary>Whether a usable openconnect binary was found.</summary>
    public bool IsReady => OpenConnectPath is not null;

    /// <summary>
    /// Locates the openconnect executable by searching PATH and well-known
    /// install directories. Returns null if not found.
    /// </summary>
    public string? OpenConnectPath => _openConnectPath ??= ResolveOpenConnect();

    private string? _openConnectPath;

    private static string? ResolveOpenConnect()
    {
        // 1. PATH
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathDirs = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in pathDirs.Concat(CandidateDirectories))
        {
            foreach (var name in CandidateOpenConnectNames)
            {
                try
                {
                    var full = Path.Combine(dir, name);
                    if (File.Exists(full)) return full;
                }
                catch (ArgumentException)
                {
                    // Skip malformed PATH entries.
                }
            }
        }

        // Last resort on Windows: recursively search the Program Files trees for
        // openconnect.exe, since installers use version-specific subfolders.
        if (IsWindows)
        {
            foreach (var root in new[]
                     {
                         Environment.GetEnvironmentVariable("ProgramFiles"),
                         Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                     })
            {
                var found = SearchRecursively(root);
                if (found is not null) return found;
            }
        }

        return null;
    }

    private static string? SearchRecursively(string? root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;
        try
        {
            // Only scan folders whose name hints at OpenConnect, to stay fast.
            foreach (var dir in Directory.EnumerateDirectories(root, "*openconnect*",
                         SearchOption.TopDirectoryOnly))
            {
                var hits = Directory.EnumerateFiles(dir, "openconnect.exe",
                    SearchOption.AllDirectories);
                foreach (var hit in hits) return hit;
            }
        }
        catch
        {
            // ignore permission / IO errors during the scan
        }
        return null;
    }
}
