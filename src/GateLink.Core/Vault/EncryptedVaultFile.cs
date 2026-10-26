using System.Text.Json;

namespace GateLink.Core.Vault;

/// <summary>
/// On-disk vault format: plaintext metadata (issuer/account/digits/period) plus
/// an AES-GCM encrypted blob containing the seeds and VPN passwords. No key
/// material lives in this class; the platform store supplies the key.
/// </summary>
public sealed class EncryptedVaultFile
{
    public const string MetadataFileName = "vault.meta.json";
    public const string EncryptedFileName = "vault.enc";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    public string DirectoryPath { get; }
    public string MetadataPath => Path.Combine(DirectoryPath, MetadataFileName);
    public string EncryptedPath => Path.Combine(DirectoryPath, EncryptedFileName);

    public bool Exists => File.Exists(MetadataPath) || File.Exists(EncryptedPath);

    public EncryptedVaultFile(string directoryPath)
        => DirectoryPath = directoryPath;

    /// <summary>Lists accounts without exposing seeds or passwords.</summary>
    public List<VaultEntry> LoadMetadata()
    {
        try
        {
            if (!File.Exists(MetadataPath)) return new List<VaultEntry>();
            var entries = JsonSerializer.Deserialize<List<VaultEntry>>(File.ReadAllBytes(MetadataPath), Json)
                          ?? new List<VaultEntry>();

            return entries.Select(e => e with { Secret = "", VpnPassword = null }).ToList();
        }
        catch
        {
            return new List<VaultEntry>();
        }
    }

    /// <summary>Decrypts and returns every entry. Throws if the key is wrong.</summary>
    public List<VaultEntry> LoadAll(ReadOnlySpan<byte> key)
    {
        if (!File.Exists(EncryptedPath)) return new List<VaultEntry>();
        var plaintext = VaultCrypto.Decrypt(File.ReadAllBytes(EncryptedPath), key);
        return JsonSerializer.Deserialize<List<VaultEntry>>(plaintext, Json) ?? new List<VaultEntry>();
    }

    /// <summary>Encrypts every entry and rewrites the metadata index atomically.</summary>
    public void Save(IReadOnlyList<VaultEntry> entries, ReadOnlySpan<byte> key)
    {
        Directory.CreateDirectory(DirectoryPath);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(entries, Json);
        var envelope = VaultCrypto.Encrypt(plaintext, key);
        WriteFileAtomic(EncryptedPath, envelope);

        var metadata = entries.Select(e => e with { Secret = "", VpnPassword = null }).ToList();
        WriteFileAtomic(MetadataPath, JsonSerializer.SerializeToUtf8Bytes(metadata, Json));
    }

    private static void WriteFileAtomic(string path, byte[] data)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, path, overwrite: true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
