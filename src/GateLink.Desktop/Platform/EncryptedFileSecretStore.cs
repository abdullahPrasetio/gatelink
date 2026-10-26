using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GateLink.Core.Vault;

namespace GateLink.Desktop.Platform;

/// <summary>
/// macOS vault: entries are stored in an AES-256-GCM file under
/// ~/Library/Application Support/GateLink. The 32-byte key lives in the login
/// Keychain as a generic-password item with a <c>userPresence</c> access
/// control, so reading it requires Touch ID / the login password. The key is
/// cached in memory only while <see cref="OtpVault"/>'s unlock window is open.
///
/// Metadata (issuer/account/digits/period) is plaintext so the UI can list
/// accounts without prompting; seeds and VPN passwords are only in the
/// encrypted file. Legacy <c>security</c>-stored entries are migrated once.
/// </summary>
public sealed class EncryptedFileSecretStore : ISecretStore
{
    private const string LegacyService = "com.gatelink.vault";
    private const string LegacyIndexAccount = "__index__";
    private const string KeyService = "com.gatelink.vault.key";
    private const string KeyAccount = "vault-key";

    private readonly EncryptedVaultFile _file;
    private byte[]? _key;

    public EncryptedFileSecretStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GateLink"))
    {
    }

    internal EncryptedFileSecretStore(string directory)
        => _file = new EncryptedVaultFile(directory);

    private string KeyMarkerPath => Path.Combine(_file.DirectoryPath, "vault.key.created");

    public async Task<IReadOnlyList<VaultEntry>> LoadAllAsync(CancellationToken ct = default)
    {
        await MigrateLegacyAsync(ct);
        return _file.LoadMetadata();
    }

    public async Task SaveAsync(VaultEntry entry, CancellationToken ct = default)
    {
        if (!await EnsureKeyAsync(ct))
            throw new InvalidOperationException("Vault is locked. Unlock it and retry.");

        var entries = ReadEncrypted();
        var index = entries.FindIndex(e => e.Id == entry.Id);
        var merged = VaultMerge.Merge(index >= 0 ? entries[index] : null, entry);
        if (index >= 0) entries[index] = merged;
        else entries.Add(merged);

        WriteAll(entries);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        if (!await EnsureKeyAsync(ct))
            throw new InvalidOperationException("Vault is locked. Unlock it and retry.");

        var entries = ReadEncrypted();
        if (entries.RemoveAll(e => e.Id == id) > 0)
            WriteAll(entries);
    }

    public async Task<VaultSecret?> LoadSecretAsync(string id, CancellationToken ct = default)
    {
        if (!await EnsureKeyAsync(ct)) return null;

        var entry = ReadEncrypted().FirstOrDefault(e => e.Id == id);
        return entry is null ? null : new VaultSecret(entry.Secret, entry.VpnPassword);
    }

    public void Lock()
    {
        if (_key is not null)
            CryptographicOperations.ZeroMemory(_key);
        _key = null;
    }

    /// <summary>
    /// Gate implementation: creates the key on first use, otherwise reads it
    /// from the Keychain. The read triggers the userPresence prompt.
    /// </summary>
    public async Task<bool> UnlockAsync(CancellationToken ct = default)
        => await EnsureKeyAsync(ct);

    private async Task<bool> EnsureKeyAsync(CancellationToken ct)
    {
        if (_key is not null) return true;

        if (File.Exists(KeyMarkerPath))
        {
            var existing = await ReadKeyItemAsync(ct);
            if (existing is null) return false;
            _key = existing;
            return true;
        }

        // No marker: this may be a first run, or the marker was lost. Probe the
        // Keychain first so a lost marker never overwrites an existing key.
        var found = await ReadKeyItemAsync(ct);
        if (found is not null)
        {
            _key = found;
            WriteKeyMarker();
            return true;
        }

        var key = VaultCrypto.NewKey();
        if (!await CreateKeyItemAsync(key, ct))
            return false;

        _key = key;
        WriteKeyMarker();
        return true;
    }

    /// <summary>
    /// Stores the random vault key in the login Keychain, trusting only this
    /// app. Reading it later triggers the Keychain user-presence prompt; other
    /// processes cannot read it silently.
    /// </summary>
    private static async Task<bool> CreateKeyItemAsync(byte[] key, CancellationToken ct)
    {
        var args = new List<string>
        {
            "add-generic-password", "-U",
            "-s", KeyService,
            "-a", KeyAccount,
            "-w", VaultCrypto.EncodeKey(key),
        };

        var app = ResolveTrustedAppPath();
        if (app is not null)
        {
            args.Add("-T");
            args.Add(app);
        }

        return await RunSecurityAsync(args.ToArray(), ct) is not null;
    }

    private static string? ResolveTrustedAppPath()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return null;

            var idx = exe.IndexOf(".app/", StringComparison.Ordinal);
            return idx >= 0 ? exe[..(idx + 4)] : exe;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]?> ReadKeyItemAsync(CancellationToken ct)
    {
        var encoded = await RunSecurityAsync(new[]
        {
            "find-generic-password",
            "-s", KeyService,
            "-a", KeyAccount,
            "-w",
        }, ct);

        if (string.IsNullOrEmpty(encoded)) return null;
        try
        {
            return VaultCrypto.DecodeKey(encoded);
        }
        catch
        {
            return null;
        }
    }

    private List<VaultEntry> ReadEncrypted()
    {
        try
        {
            return _key is null ? new List<VaultEntry>() : _file.LoadAll(_key);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[vault] Could not decrypt vault: {ex.Message}");
            return new List<VaultEntry>();
        }
    }

    private void WriteAll(List<VaultEntry> entries)
    {
        if (_key is null) throw new InvalidOperationException("Vault is locked.");
        _file.Save(entries, _key);
    }

    private void WriteKeyMarker()
    {
        Directory.CreateDirectory(_file.DirectoryPath);
        File.WriteAllText(KeyMarkerPath, "1");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(KeyMarkerPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private async Task MigrateLegacyAsync(CancellationToken ct)
    {
        if (_file.Exists) return;

        var legacy = await ReadLegacyAsync(ct);
        if (legacy.Count == 0) return;

        // Create the key and write the encrypted vault. The legacy items are
        // then removed so nothing remains readable through /usr/bin/security.
        if (!await EnsureKeyAsync(ct)) return;

        WriteAll(legacy);
        await DeleteLegacyAsync(legacy, ct);
        Lock();   // the next reveal/connect must authenticate
    }

    private static async Task<List<VaultEntry>> ReadLegacyAsync(CancellationToken ct)
    {
        var result = new List<VaultEntry>();

        var index = await RunSecurityAsync(new[]
        {
            "find-generic-password",
            "-s", LegacyService,
            "-a", LegacyIndexAccount,
            "-w",
        }, ct);

        if (string.IsNullOrEmpty(index)) return result;

        foreach (var id in index.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var json = await RunSecurityAsync(new[]
            {
                "find-generic-password",
                "-s", LegacyService,
                "-a", id,
                "-w",
            }, ct);

            if (string.IsNullOrEmpty(json)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<VaultEntry>(json);
                if (entry is not null) result.Add(entry);
            }
            catch (JsonException)
            {
                // Skip corrupt legacy entries.
            }
        }

        return result;
    }

    private static async Task DeleteLegacyAsync(IEnumerable<VaultEntry> entries, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            await RunSecurityAsync(new[]
            {
                "delete-generic-password",
                "-s", LegacyService,
                "-a", entry.Id,
            }, ct);
        }

        await RunSecurityAsync(new[]
        {
            "delete-generic-password",
            "-s", LegacyService,
            "-a", LegacyIndexAccount,
        }, ct);
    }

    private static async Task<string?> RunSecurityAsync(string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/security")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            return proc.ExitCode == 0 ? stdout.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
