using System.Security.Cryptography;
using System.Text;
using GateLink.Core.Vault;
using Xunit;

namespace GateLink.Core.Tests;

public class VaultCryptoTests
{
    [Fact]
    public void RoundTrip_ReturnsPlaintext()
    {
        var key = VaultCrypto.NewKey();
        var plaintext = Encoding.UTF8.GetBytes("{\"Secret\":\"JBSWY3DPEHPK3PXP\"}");

        var envelope = VaultCrypto.Encrypt(plaintext, key);
        var decrypted = VaultCrypto.Decrypt(envelope, key);

        Assert.Equal(plaintext, decrypted);
        Assert.NotEqual(plaintext, envelope[^plaintext.Length..]);
    }

    [Fact]
    public void WrongKey_Fails()
    {
        var envelope = VaultCrypto.Encrypt(Encoding.UTF8.GetBytes("x"), VaultCrypto.NewKey());
        Assert.ThrowsAny<CryptographicException>(
            () => VaultCrypto.Decrypt(envelope, VaultCrypto.NewKey()));
    }

    [Fact]
    public void TamperedCiphertext_Fails()
    {
        var key = VaultCrypto.NewKey();
        var envelope = VaultCrypto.Encrypt(Encoding.UTF8.GetBytes("secret"), key);
        envelope[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.Decrypt(envelope, key));
    }

    [Fact]
    public void WrongMagic_Fails()
    {
        var key = VaultCrypto.NewKey();
        var envelope = VaultCrypto.Encrypt(Encoding.UTF8.GetBytes("secret"), key);
        envelope[0] = (byte)'X';

        Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.Decrypt(envelope, key));
    }

    [Fact]
    public void KeyEncoding_RoundTrips()
    {
        var key = VaultCrypto.NewKey();
        Assert.Equal(key, VaultCrypto.DecodeKey(VaultCrypto.EncodeKey(key)));
    }
}

public class VaultMergeTests
{
    private static VaultEntry Entry() => new()
    {
        Id = "1",
        Issuer = "Example IAM",
        Account = "alice",
        Secret = "JBSWY3DPEHPK3PXP",
        VpnUsername = "alice",
        VpnPassword = "s3cret",
    };

    [Fact]
    public void MetadataOnlyUpdate_PreservesSeedAndPassword()
    {
        var merged = VaultMerge.Merge(Entry(), Entry() with
        {
            Secret = "",
            VpnPassword = null,
            Issuer = "Renamed",
        });

        Assert.Equal("JBSWY3DPEHPK3PXP", merged.Secret);
        Assert.Equal("s3cret", merged.VpnPassword);
        Assert.Equal("Renamed", merged.Issuer);
    }

    [Fact]
    public void TypedPassword_OverridesStoredPassword()
    {
        var merged = VaultMerge.Merge(Entry(), Entry() with
        {
            Secret = "",
            VpnPassword = "new-password",
        });

        Assert.Equal("new-password", merged.VpnPassword);
        Assert.Equal("JBSWY3DPEHPK3PXP", merged.Secret);
    }

    [Fact]
    public void NewEntryWithoutSecret_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => VaultMerge.Merge(null, Entry() with { Secret = "" }));
    }

    [Fact]
    public void NewEntryWithSecret_IsAccepted()
    {
        var entry = Entry();
        Assert.Equal(entry, VaultMerge.Merge(null, entry));
    }
}
