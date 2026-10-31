using System.Runtime.InteropServices;
using GateLink.Core;
using Xunit;

namespace GateLink.Core.Tests;

public class UpdateCheckerTests
{
    private static (string Name, string Url) Asset(string name) =>
        (name, "https://example.test/" + name);

    private static readonly (string Name, string Url)[] DualArchAssets =
    [
        Asset("GateLink-x64.dmg"),
        Asset("GateLink-x64.dmg.sig"),
        Asset("GateLink-arm64.dmg"),
        Asset("GateLink-arm64.dmg.sig"),
        Asset("GateLink.dmg"),
        Asset("GateLink.dmg.sig"),
    ];

    [Fact]
    public void Arm64_SelectsArm64DmgAndSignature()
    {
        var (url, signature) = UpdateChecker.SelectMacAssets(DualArchAssets, Architecture.Arm64);

        Assert.Equal("https://example.test/GateLink-arm64.dmg", url);
        Assert.Equal("https://example.test/GateLink-arm64.dmg.sig", signature);
    }

    [Fact]
    public void X64_SelectsX64DmgAndSignature()
    {
        var (url, signature) = UpdateChecker.SelectMacAssets(DualArchAssets, Architecture.X64);

        Assert.Equal("https://example.test/GateLink-x64.dmg", url);
        Assert.Equal("https://example.test/GateLink-x64.dmg.sig", signature);
    }

    [Fact]
    public void X64_DoesNotFallBackToLegacyArm64Dmg()
    {
        var legacy = new[]
        {
            Asset("GateLink.dmg"),
            Asset("GateLink.dmg.sig"),
            Asset("GateLink-macos.zip"),
        };

        var (url, signature) = UpdateChecker.SelectMacAssets(legacy, Architecture.X64);

        Assert.Null(url);
        Assert.Null(signature);
    }

    [Fact]
    public void Arm64_FallsBackToLegacySingleArchRelease()
    {
        var legacy = new[]
        {
            Asset("GateLink.dmg"),
            Asset("GateLink.dmg.sig"),
        };

        var (url, signature) = UpdateChecker.SelectMacAssets(legacy, Architecture.Arm64);

        Assert.Equal("https://example.test/GateLink.dmg", url);
        Assert.Equal("https://example.test/GateLink.dmg.sig", signature);
    }

    [Fact]
    public void Arm64_PrefersArchAssetOverLegacyCopy()
    {
        var (url, _) = UpdateChecker.SelectMacAssets(DualArchAssets, Architecture.Arm64);

        Assert.Equal("https://example.test/GateLink-arm64.dmg", url);
    }

    [Fact]
    public void MissingSignature_ReturnsAssetWithoutSignature()
    {
        var assets = new[] { Asset("GateLink-x64.dmg") };

        var (url, signature) = UpdateChecker.SelectMacAssets(assets, Architecture.X64);

        Assert.Equal("https://example.test/GateLink-x64.dmg", url);
        Assert.Null(signature);
    }
}
