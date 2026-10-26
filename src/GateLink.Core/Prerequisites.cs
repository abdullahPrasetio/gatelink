using System.Runtime.InteropServices;

namespace GateLink.Core;

/// <summary>One checked prerequisite and how to satisfy it.</summary>
public sealed record PrerequisiteItem(string Name, bool Ok, string Detail, string? FixHint);

/// <summary>Result of a prerequisite scan.</summary>
public sealed record PrerequisiteReport(IReadOnlyList<PrerequisiteItem> Items)
{
    public bool AllOk => Items.All(i => i.Ok);
    public bool CanConnect => Items.Where(i => i.Name is "OpenConnect").All(i => i.Ok);
}

/// <summary>
/// Detects whether the tools GateLink depends on are present, so the UI can show
/// a clear checklist instead of failing cryptically at connect time.
/// </summary>
public static class Prerequisites
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static PrerequisiteReport Check(VpnEnvironment env)
    {
        var items = new List<PrerequisiteItem>();

        // 1. openconnect — required on every platform.
        var ocPath = env.OpenConnectPath;
        items.Add(new PrerequisiteItem(
            "OpenConnect",
            ocPath is not null,
            ocPath ?? "not found",
            ocPath is not null ? null
                : IsWindows
                    ? "Install OpenConnect for Windows (openconnect-gui) and ensure openconnect.exe is on PATH."
                    : "Install with: brew install openconnect"));

        if (!IsWindows)
        {
            // 2. expect — needed for the PTY login flow on macOS/Linux.
            var expect = Which("expect", "/usr/bin/expect", "/opt/homebrew/bin/expect", "/usr/local/bin/expect");
            items.Add(new PrerequisiteItem(
                "expect (login helper)",
                expect is not null,
                expect ?? "not found",
                expect is not null ? null : "expect ships with macOS; if missing: brew install expect"));

            // 3. Root tunnel helper — needed so the tunnel can run without a
            //    per-connect prompt. It is a fixed-purpose wrapper, not a
            //    NOPASSWD grant to openconnect or kill.
            var helper = File.Exists("/usr/local/libexec/gatelink/gatelink-tunnel");
            items.Add(new PrerequisiteItem(
                "Root tunnel helper",
                helper,
                helper ? "installed" : "not installed",
                helper ? null : "Run: ./scripts/install-tunnel-helper.sh --user \"$USER\""));

            // 4. HIP report script — optional but needed by gateways enforcing HIP.
            var hip = env.HipReportScriptPath is not null;
            items.Add(new PrerequisiteItem(
                "HIP report script",
                hip,
                hip ? "bundled" : "missing",
                hip ? null : "Shipped with the app; reinstall if missing."));
        }
        else
        {
            // Windows: elevation + driver are the extra needs.
            items.Add(new PrerequisiteItem(
                "Administrator",
                IsElevatedWindows(),
                IsElevatedWindows() ? "running elevated" : "not elevated",
                IsElevatedWindows() ? null : "Run GateLink as Administrator (right-click → Run as administrator)."));
        }

        return new PrerequisiteReport(items);
    }

    private static string? Which(string name, params string[] fallbacks)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var p = Path.Combine(dir, name); if (File.Exists(p)) return p; }
            catch (ArgumentException) { }
        }
        foreach (var f in fallbacks) if (File.Exists(f)) return f;
        return null;
    }

    private static bool IsElevatedWindows()
    {
        if (!IsWindows) return false;
        try
        {
            // A simple heuristic: try to open a file in a system-protected dir.
            // Avoids a Windows-only API dependency in the shared Core.
            var test = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "gatelink_elev_test.tmp");
            File.WriteAllText(test, "x");
            File.Delete(test);
            return true;
        }
        catch { return false; }
    }
}
