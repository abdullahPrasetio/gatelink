using System.Text.Json;
using GateLink.Core;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Small user-writable settings file. The portal is not a secret; the vault
/// password is never stored here. AutoUnlock only controls whether the app
/// asks the platform biometric/keychain gate at launch and fills the form.
/// </summary>
public static class AppSettings
{
    private sealed record Data
    {
        public string? Gateway { get; init; }
        public bool AutoUnlock { get; init; }
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GateLink",
        "settings.json");

    public static string? LoadGateway()
    {
        var data = Load();
        return data?.Gateway is { } gateway && TunnelArgs.IsValidHost(gateway)
            ? gateway
            : null;
    }

    public static bool LoadAutoUnlock() => Load()?.AutoUnlock == true;

    public static void SaveGateway(string gateway)
    {
        if (!TunnelArgs.IsValidHost(gateway)) return;
        var data = Load() ?? new Data();
        Save(data with { Gateway = gateway });
    }

    public static void SaveAutoUnlock(bool enabled)
    {
        var data = Load() ?? new Data();
        Save(data with { AutoUnlock = enabled });
    }

    private static Data? Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            return JsonSerializer.Deserialize<Data>(File.ReadAllText(SettingsPath));
        }
        catch
        {
            return null;
        }
    }

    private static void Save(Data data)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(data);
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
