using System.Text.Json;
using GateLink.Core;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Small user-writable settings file. The portal is not a secret and is not
/// compiled in; it is remembered here after a successful form submission.
/// The vault password is never stored here.
/// </summary>
public static class AppSettings
{
    private sealed record Data
    {
        public string? Gateway { get; init; }
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GateLink",
        "settings.json");

    public static string? LoadGateway()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(SettingsPath));
            return data?.Gateway is { } gateway && TunnelArgs.IsValidHost(gateway)
                ? gateway
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveGateway(string gateway)
    {
        try
        {
            if (!TunnelArgs.IsValidHost(gateway)) return;

            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(new Data { Gateway = gateway });
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(tmp, SettingsPath, overwrite: true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(SettingsPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Settings are best-effort; never block a connection.
        }
    }
}
