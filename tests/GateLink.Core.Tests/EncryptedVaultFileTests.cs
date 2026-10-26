using System.Security.Cryptography;
using GateLink.Core.Vault;
using Xunit;

namespace GateLink.Core.Tests;

public class EncryptedVaultFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gatelink-vault-" + Guid.NewGuid().ToString("N"));
    private readonly EncryptedVaultFile _file;
    private readonly byte[] _key = VaultCrypto.NewKey();

    public EncryptedVaultFileTests() => _file = new EncryptedVaultFile(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static List<VaultEntry> Entries() => new()
    {
        new VaultEntry
        {
            Id = "1",
            Issuer = "Example IAM",
            Account = "alice",
            Secret = "JBSWY3DPEHPK3PXP",
            VpnUsername = "alice",
            VpnPassword = "s3cret",
        },
        new VaultEntry
        {
            Id = "2",
            Issuer = "Other",
            Account = "bob",
            Secret = "KRSXG5CTMVRXEZLU",
        },
    };

    [Fact]
    public void SaveAndLoad_RoundTripsSecrets()
    {
        var entries = Entries();
        _file.Save(entries, _key);

        var loaded = _file.LoadAll(_key);

        Assert.Equal(2, loaded.Count);
        Assert.Equal("JBSWY3DPEHPK3PXP", loaded[0].Secret);
        Assert.Equal("s3cret", loaded[0].VpnPassword);
    }

    [Fact]
    public void Metadata_HasNoSecrets()
    {
        _file.Save(Entries(), _key);

        var metadata = _file.LoadMetadata();
        var text = File.ReadAllText(_file.MetadataPath);

        Assert.Equal(2, metadata.Count);
        Assert.All(metadata, e => Assert.Empty(e.Secret));
        Assert.All(metadata, e => Assert.Null(e.VpnPassword));
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", text);
        Assert.DoesNotContain("s3cret", text);
    }

    [Fact]
    public void EncryptedFile_DoesNotContainPlaintextSecrets()
    {
        _file.Save(Entries(), _key);
        var bytes = File.ReadAllBytes(_file.EncryptedPath);
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", text);
        Assert.DoesNotContain("s3cret", text);
    }

    [Fact]
    public void WrongKey_Throws()
    {
        _file.Save(Entries(), _key);
        Assert.ThrowsAny<CryptographicException>(() => _file.LoadAll(VaultCrypto.NewKey()));
    }

    [Fact]
    public void MissingFiles_ReturnEmpty()
    {
        Assert.Empty(_file.LoadMetadata());
        Assert.Empty(_file.LoadAll(_key));
        Assert.False(_file.Exists);
    }

    [Fact]
    public void Files_AreOwnerOnly()
    {
        if (OperatingSystem.IsWindows()) return;

        _file.Save(Entries(), _key);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(_file.MetadataPath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(_file.EncryptedPath));
    }
}
