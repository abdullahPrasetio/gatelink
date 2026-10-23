using System.Text;
using GateLink.Core.Otp;
using Xunit;

namespace GateLink.Core.Tests;

public class TotpTests
{
    // RFC 6238 Appendix B test vectors. The seed is the ASCII string
    // "12345678901234567890" (20 bytes) for SHA-1, using 8 digits.
    private static TotpGenerator Rfc6238Sha1()
    {
        var seed = Encoding.ASCII.GetBytes("12345678901234567890");
        return new TotpGenerator(seed, digits: 8, periodSeconds: 30, algorithm: OtpAlgorithm.Sha1);
    }

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    public void Generate_MatchesRfc6238Vectors(long unixTime, string expected)
    {
        var totp = Rfc6238Sha1();
        var at = DateTimeOffset.FromUnixTimeSeconds(unixTime);

        Assert.Equal(expected, totp.Generate(at));
    }

    [Fact]
    public void SecondsRemaining_IsWithinPeriod()
    {
        var totp = Rfc6238Sha1();
        var remaining = totp.SecondsRemaining(DateTimeOffset.FromUnixTimeSeconds(10));
        Assert.Equal(20, remaining); // 30 - (10 % 30)
    }

    [Fact]
    public void Generate_ProducesSixDigitsByDefault()
    {
        var totp = TotpGenerator.FromBase32("JBSWY3DPEHPK3PXP");
        var code = totp.Generate();
        Assert.Equal(6, code.Length);
        Assert.True(code.All(char.IsDigit));
    }
}

public class Base32Tests
{
    [Theory]
    [InlineData("JBSWY3DP", "Hello")]            // RFC 4648 example
    [InlineData("MFRGG===", "abc")]
    public void Decode_RoundTripsKnownValues(string base32, string ascii)
    {
        var decoded = Base32.Decode(base32);
        Assert.Equal(ascii, Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void Decode_IgnoresSpacesAndCaseAndPadding()
    {
        var a = Base32.Decode("jbsw y3dp");
        var b = Base32.Decode("JBSWY3DP");
        Assert.Equal(b, a);
    }

    [Fact]
    public void Decode_Throws_OnInvalidCharacter()
        => Assert.Throws<FormatException>(() => Base32.Decode("JBSW0189")); // 0,1,8,9 not in alphabet
}

public class OtpAuthUriTests
{
    [Fact]
    public void Parse_FullUri_ExtractsAllFields()
    {
        var uri = "otpauth://totp/BRI%20VPN:alice?secret=JBSWY3DPEHPK3PXP&issuer=BRI%20VPN&digits=6&period=30&algorithm=SHA1";

        var parsed = OtpAuthUri.Parse(uri);

        Assert.Equal("JBSWY3DPEHPK3PXP", parsed.Secret);
        Assert.Equal("BRI VPN", parsed.Issuer);
        Assert.Equal("alice", parsed.Account);
        Assert.Equal(6, parsed.Digits);
        Assert.Equal(30, parsed.Period);
        Assert.Equal(OtpAlgorithm.Sha1, parsed.Algorithm);
    }

    [Fact]
    public void Parse_IssuerFromLabel_WhenNoIssuerParam()
    {
        var parsed = OtpAuthUri.Parse("otpauth://totp/ACME:bob?secret=JBSWY3DPEHPK3PXP");
        Assert.Equal("ACME", parsed.Issuer);
        Assert.Equal("bob", parsed.Account);
    }

    [Fact]
    public void Parse_Throws_WhenSecretMissing()
        => Assert.Throws<FormatException>(() => OtpAuthUri.Parse("otpauth://totp/ACME:bob?issuer=ACME"));

    [Fact]
    public void Parse_Throws_OnNonOtpauthScheme()
        => Assert.Throws<FormatException>(() => OtpAuthUri.Parse("https://example.com"));

    [Fact]
    public void CreateGenerator_ProducesWorkingCode()
    {
        var parsed = OtpAuthUri.Parse("otpauth://totp/ACME:bob?secret=JBSWY3DPEHPK3PXP");
        var code = parsed.CreateGenerator().Generate();
        Assert.Equal(6, code.Length);
    }
}
