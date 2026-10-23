using GateLink.Core;
using Xunit;

namespace GateLink.Core.Tests;

public class GpCookieTests
{
    [Fact]
    public void Parse_ExtractsAllFields()
    {
        var output = """
            COOKIE=authcookie=abc123def456
            HOST=vpn.bri.co.id
            CONNECT_URL=https://vpn.bri.co.id/ssl-tunnel-connect.sslvpn
            FINGERPRINT=sha256:aa:bb:cc:dd
            RESOLVE=vpn.bri.co.id:10.0.0.1
            """;

        var cookie = GpCookie.Parse(output);

        Assert.Equal("authcookie=abc123def456", cookie.Cookie);
        Assert.Equal("vpn.bri.co.id", cookie.Host);
        Assert.Equal("https://vpn.bri.co.id/ssl-tunnel-connect.sslvpn", cookie.ConnectUrl);
        Assert.Equal("sha256:aa:bb:cc:dd", cookie.Fingerprint);
        Assert.Equal("vpn.bri.co.id:10.0.0.1", cookie.Resolve);
    }

    [Fact]
    public void Parse_IsCaseInsensitiveOnKeys_AndTrimsWhitespace()
    {
        var output = "  cookie = xyz \n  host = host.example \n";

        var cookie = GpCookie.Parse(output);

        Assert.Equal("xyz", cookie.Cookie);
        Assert.Equal("host.example", cookie.Host);
    }

    [Fact]
    public void Parse_IgnoresUnknownKeysAndBlankLines()
    {
        var output = "COOKIE=c\n\nSOMETHING=else\nHOST=h\n# a comment\n";

        var cookie = GpCookie.Parse(output);

        Assert.Equal("c", cookie.Cookie);
        Assert.Equal("h", cookie.Host);
    }

    [Fact]
    public void Parse_StripsShellStyleQuotes_FromOpenConnectOutput()
    {
        // openconnect prints values wrapped in single quotes.
        var output = """
            COOKIE='authcookie=abc123'
            HOST='103.63.96.190'
            FINGERPRINT='pin-sha256:mz70'
            RESOLVE='vpn.bri.co.id:103.63.96.190'
            """;

        var cookie = GpCookie.Parse(output);

        Assert.Equal("authcookie=abc123", cookie.Cookie);
        Assert.Equal("103.63.96.190", cookie.Host);          // no surrounding quotes
        Assert.Equal("pin-sha256:mz70", cookie.Fingerprint);
        Assert.Equal("vpn.bri.co.id:103.63.96.190", cookie.Resolve);
    }

    [Fact]
    public void Parse_DefaultsHostToConfiguredGateway_WhenMissing()
    {
        var cookie = GpCookie.Parse("COOKIE=only-cookie");

        Assert.Equal(VpnConfig.Gateway, cookie.Host);
    }

    [Fact]
    public void Parse_Throws_WhenCookieMissing()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => GpCookie.Parse("HOST=vpn.bri.co.id\nFINGERPRINT=sha256:aa"));

        Assert.Contains("cookie", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
