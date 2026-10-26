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

    /// <summary>
    /// PID written by the root-owned tunnel helper. The app only reads it for
    /// "adopt existing tunnel" / health checks; it never writes it.
    /// </summary>
    public string PidPath { get; }

    /// <summary>
    /// Path to the HIP report script shipped with the app. The root-owned helper
    /// installer copies it to a protected location; the app never executes the
    /// user-writable copy as root.
    /// </summary>
    public string? HipReportScriptPath { get; }

    /// <summary>
    /// Root-owned fixed-purpose helper installed by install-tunnel-helper.sh.
    /// macOS only; Windows runs openconnect under the app's elevation.
    /// </summary>
    public string TunnelHelperPath => "/usr/local/libexec/gatelink/gatelink-tunnel";

    public VpnEnvironment()
    {
        RuntimeDirectory = Path.Combine(ResolveRuntimeBase(), VpnConfig.ProductName);
        Directory.CreateDirectory(RuntimeDirectory);

        LogPath = Path.Combine(RuntimeDirectory, "openconnect.log");
        PidPath = IsWindows
            ? Path.Combine(RuntimeDirectory, "openconnect.pid")
            : "/var/run/gatelink.pid";
        HipReportScriptPath = ResolveHipReportScript();
    }

    /// <summary>
    /// Locates the bundled hipreport.sh without copying it into a user-writable
    /// directory. The helper installer copies it to a root-owned location.
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
                if (File.Exists(src)) return src;
        }
        catch
        {
            // fall through: no HIP wrapper available
        }
        return null;
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
        // 0. Bundled openconnect (shipped inside the app) takes priority, so the
        //    user never needs to install it themselves.
        var bundled = ResolveBundledOpenConnect();
        if (bundled is not null) return bundled;

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

    /// <summary>
    /// Finds the openconnect bundled next to the app (Assets/openconnect/). On
    /// macOS this lives inside the .app; on Windows next to the exe.
    /// </summary>
    private static string? ResolveBundledOpenConnect()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var name = IsWindows ? "openconnect.exe" : "openconnect";
            foreach (var rel in new[]
                     {
                         Path.Combine(baseDir, "Assets", "openconnect", name),
                         Path.Combine(baseDir, "openconnect", name),
                     })
            {
                if (File.Exists(rel)) return rel;
            }
        }
        catch { /* fall through to PATH search */ }
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
