using GateLink.Core;
using Xunit;

namespace GateLink.Core.Tests;

public class TunnelArgsTests
{
    [Theory]
    [InlineData("vpn.example.com")]
    [InlineData("vpn.example.com")]
    [InlineData("10.0.0.1")]
    [InlineData("a")]
    public void ValidHosts_AreAccepted(string host)
        => Assert.True(TunnelArgs.IsValidHost(host));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-vpn.example.com")]
    [InlineData("--script=/tmp/x")]
    [InlineData("vpn example.com")]
    [InlineData("vpn.example.com/../../x")]
    [InlineData("vpn.example.com\n--csd-wrapper=/tmp/x")]
    public void InvalidHosts_AreRejected(string? host)
        => Assert.False(TunnelArgs.IsValidHost(host));

    [Theory]
    [InlineData("sha256:aa:bb:cc")]
    [InlineData("pin-sha256:mz70")]
    [InlineData("pin-sha256:Ab+Cd/Ef0123456789+/abc=")]
    [InlineData("ABCDEF")]
    public void ValidFingerprints_AreAccepted(string fingerprint)
        => Assert.True(TunnelArgs.IsValidFingerprint(fingerprint));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-sha256:aa")]
    [InlineData("sha256:aa bb")]
    [InlineData("sha256:aa;rm -rf /")]
    [InlineData("sha256:aa\n--script=/tmp/x")]
    public void InvalidFingerprints_AreRejected(string? fingerprint)
        => Assert.False(TunnelArgs.IsValidFingerprint(fingerprint));

    [Theory]
    [InlineData("2", 2)]
    [InlineData(" 4321 ", 4321)]
    public void ValidPids_AreAccepted(string text, int expected)
    {
        Assert.True(TunnelArgs.TryParseTunnelPid(text, out var pid));
        Assert.Equal(expected, pid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-5")]
    [InlineData("12x")]
    public void InvalidPids_AreRejected(string? text)
        => Assert.False(TunnelArgs.TryParseTunnelPid(text, out _));
}
