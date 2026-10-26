#if WINDOWS
using System.Security.Cryptography;
using System.Text.Json;
using GateLink.Core.Vault;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Windows vault: the full entry list (seeds + VPN passwords) is encrypted with
/// DPAPI (CurrentUser), so it is unreadable at rest by other users. Metadata is
/// stored separately in plaintext so the account list loads without a prompt.
/// The Windows Hello gate is enforced in-process by <see cref="WindowsHelloGate"/>.
///
/// DPAPI protects at rest, not against code already running as the same user;
/// Windows Hello supplies the user-presence check for the app's unlock window.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private static string VaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GateLink");

    private string MetadataPath => Path.Combine(VaultDirectory, "vault.meta.json");
    private string DataPath => Path.Combine(VaultDirectory, "vault.dat");

    public Task<IReadOnlyList<VaultEntry>> LoadAllAsync(CancellationToken ct = default)
        => Task.FromResult(ReadMetadata());

    public Task SaveAsync(VaultEntry entry, CancellationToken ct = default)
    {
        var entries = ReadAll();
        var index = entries.FindIndex(e => e.Id == entry.Id);
        var merged = VaultMerge.Merge(index >= 0 ? entries[index] : null, entry);
        if (index >= 0) entries[index] = merged;
        else entries.Add(merged);

        WriteAll(entries);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var entries = ReadAll();
        if (entries.RemoveAll(e => e.Id == id) > 0)
            WriteAll(entries);
        return Task.CompletedTask;
    }

    public Task<VaultSecret?> LoadSecretAsync(string id, CancellationToken ct = default)
    {
        var entry = ReadAll().FirstOrDefault(e => e.Id == id);
        return Task.FromResult(entry is null ? null : new VaultSecret(entry.Secret, entry.VpnPassword));
    }

    public void Lock()
    {
        // No cached key material: DPAPI decrypts on demand.
    }

    private IReadOnlyList<VaultEntry> ReadMetadata()
    {
        try
        {
            if (!File.Exists(MetadataPath)) return Array.Empty<VaultEntry>();
            var entries = JsonSerializer.Deserialize<List<VaultEntry>>(File.ReadAllBytes(MetadataPath), Json)
                          ?? new List<VaultEntry>();

            return entries.Select(e => e with { Secret = "", VpnPassword = null }).ToList();
        }
        catch
        {
            return Array.Empty<VaultEntry>();
        }
    }

    private List<VaultEntry> ReadAll()
    {
        try
        {
            if (!File.Exists(DataPath)) return new List<VaultEntry>();

            var protectedBytes = File.ReadAllBytes(DataPath);
            var plaintext = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<List<VaultEntry>>(plaintext, Json) ?? new List<VaultEntry>();
        }
        catch
        {
            return new List<VaultEntry>();
        }
    }

    private void WriteAll(List<VaultEntry> entries)
    {
        Directory.CreateDirectory(VaultDirectory);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(entries, Json);
        var protectedBytes = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);

        var tmp = DataPath + ".tmp";
        File.WriteAllBytes(tmp, protectedBytes);
        File.Move(tmp, DataPath, overwrite: true);

        var metadata = entries.Select(e => e with { Secret = "", VpnPassword = null }).ToList();
        var metaTmp = MetadataPath + ".tmp";
        File.WriteAllBytes(metaTmp, JsonSerializer.SerializeToUtf8Bytes(metadata, Json));
        File.Move(metaTmp, MetadataPath, overwrite: true);
    }
}
#endif
