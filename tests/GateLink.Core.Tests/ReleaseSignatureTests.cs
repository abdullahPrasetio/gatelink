using System.Security.Cryptography;
using System.Text;
using GateLink.Core;
using Xunit;

namespace GateLink.Core.Tests;

public class ReleaseSignatureTests
{
    private static (string Pem, byte[] Hash, byte[] Signature) Signed()
    {
        using var rsa = RSA.Create(2048);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("GateLink.dmg"));
        var signature = rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return (rsa.ExportSubjectPublicKeyInfoPem(), hash, signature);
    }

    [Fact]
    public void ValidSignature_IsAccepted()
    {
        var (pem, hash, signature) = Signed();
        Assert.True(ReleaseSignature.Verify(hash, signature, pem));
    }

    [Fact]
    public void TamperedHash_IsRejected()
    {
        var (pem, hash, signature) = Signed();
        hash[0] ^= 0x01;
        Assert.False(ReleaseSignature.Verify(hash, signature, pem));
    }

    [Fact]
    public void TamperedSignature_IsRejected()
    {
        var (pem, hash, signature) = Signed();
        signature[0] ^= 0x01;
        Assert.False(ReleaseSignature.Verify(hash, signature, pem));
    }

    [Fact]
    public void EmptySignature_IsRejected()
    {
        var (pem, hash, _) = Signed();
        Assert.False(ReleaseSignature.Verify(hash, ReadOnlySpan<byte>.Empty, pem));
    }

    [Fact]
    public void PinnedKey_DoesNotAcceptArbitrarySignature()
    {
        var (_, hash, signature) = Signed();
        Assert.False(ReleaseSignature.Verify(hash, signature));
    }

    [Fact]
    public void PinnedKey_VerifiesKnownReleaseSignature()
    {
        // Signed with .secrets/gatelink-release.key for the string below. The
        // signature is public; it proves the pinned key matches the CI key.
        const string signatureBase64 =
            "EtxQYMCDFVSRthPzMnWZq6I5FYelnsjyTH7qV65Baylkza/VHejyAsBPt14iajM3hURTPcnVITZkrxJoRRbVb8ET6ZE85J0InvmwaMRpLIFXwECdmFFxPYsu+PAvRT1hO9+X8ytm2fQVg3jlsmD4zQfhcA/e4TyjhzsL6/sFXWqNRBC71HZbDkIHhZJy3WaijNIjYxWnOB7beHPbPRlS7iucW2dxOMFGAyVFfXjjetkB548TOzN9jWVp/KEph85NjUiRgdPF/iSshYnNRvns3PQENlRiAfNd6M/D+5Y/KW3khdi6nPlQakeYWqmy6+wgIRbjs5ukDIJL+ls+ZZ70OUtGSQ/fz6Y6SSLP3BnuQvkDFpCC1CTonJtIB53Q1fiU8rkczZ8OVflYH7ETTQWtb397g78ZWJBLi2crcUYoyANOrpyskU/AgXGPrc+er+7uSIjMSmgP8CnCB/RxPCLJdiwYuVG8S5dfFh4DymXS+mkA2QiE8AqStIi4sMfwCUgp";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("gatelink-release-test"));
        var signature = Convert.FromBase64String(signatureBase64);

        Assert.True(ReleaseSignature.Verify(hash, signature));
    }
}
