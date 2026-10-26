using GateLink.Core.Otp;
using GateLink.Core.Vault;
using Xunit;

namespace GateLink.Core.Tests;

public class OtpVaultTests
{
    private sealed class FakeGate : IBiometricGate
    {
        public bool IsAvailable => true;
        public int VerifyCalls { get; private set; }
        public bool Result { get; set; } = true;

        public Task<bool> VerifyAsync(string reason, CancellationToken ct = default)
        {
            VerifyCalls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class NullStore : ISecretStore
    {
        public Task<IReadOnlyList<VaultEntry>> LoadAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<VaultEntry>>(Array.Empty<VaultEntry>());
        public Task SaveAsync(VaultEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static VaultEntry SampleEntry() => new()
    {
        Id = "1",
        Issuer = "Example VPN",
        Account = "alice",
        Secret = "JBSWY3DPEHPK3PXP",
    };

    [Fact]
    public async Task FirstAccess_PromptsBiometrics()
    {
        var gate = new FakeGate();
        var vault = new OtpVault(gate, new NullStore());

        var ok = await vault.EnsureUnlockedAsync("test");

        Assert.True(ok);
        Assert.Equal(1, gate.VerifyCalls);
        Assert.True(vault.IsUnlocked);
    }

    [Fact]
    public async Task SecondAccess_WhileUnlocked_DoesNotRePrompt()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new FakeGate();
        var vault = new OtpVault(gate, new NullStore(), time) { RelockAfter = TimeSpan.FromMinutes(1) };

        await vault.EnsureUnlockedAsync("first");
        time.Advance(TimeSpan.FromSeconds(20));
        await vault.EnsureUnlockedAsync("second"); // still within window

        Assert.Equal(1, gate.VerifyCalls); // only prompted once
    }

    [Fact]
    public async Task AfterRelockWindow_PromptsAgain()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new FakeGate();
        var vault = new OtpVault(gate, new NullStore(), time) { RelockAfter = TimeSpan.FromMinutes(1) };

        await vault.EnsureUnlockedAsync("first");
        time.Advance(TimeSpan.FromMinutes(2)); // past the window
        Assert.False(vault.IsUnlocked);

        await vault.EnsureUnlockedAsync("second");
        Assert.Equal(2, gate.VerifyCalls);
    }

    [Fact]
    public async Task RejectedBiometrics_LeavesVaultLocked()
    {
        var gate = new FakeGate { Result = false };
        var vault = new OtpVault(gate, new NullStore());

        var ok = await vault.EnsureUnlockedAsync("test");

        Assert.False(ok);
        Assert.False(vault.IsUnlocked);
    }

    [Fact]
    public async Task GetOtp_ReturnsCode_WhenUnlocked()
    {
        var vault = new OtpVault(new FakeGate(), new NullStore());
        var code = await vault.GetOtpAsync(SampleEntry());
        Assert.NotNull(code);
        Assert.Equal(6, code!.Length);
    }

    [Fact]
    public async Task GetOtp_ReturnsNull_WhenBiometricsRejected()
    {
        var vault = new OtpVault(new FakeGate { Result = false }, new NullStore());
        var code = await vault.GetOtpAsync(SampleEntry());
        Assert.Null(code);
    }
}

/// <summary>Minimal controllable TimeProvider for deterministic relock tests.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;
    public FakeTimeProvider(DateTimeOffset start) => _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
