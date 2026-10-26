using GateLink.Core;
using GateLink.Core.Vault;
using Xunit;

namespace GateLink.Core.Tests;

public class VpnSessionVaultTests
{
    private sealed class DenyGate : IBiometricGate
    {
        public bool IsAvailable => true;
        public Task<bool> VerifyAsync(string reason, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class AllowGate : IBiometricGate
    {
        public bool IsAvailable => true;
        public Task<bool> VerifyAsync(string reason, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class NullStore : ISecretStore
    {
        public Task<IReadOnlyList<VaultEntry>> LoadAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<VaultEntry>>(Array.Empty<VaultEntry>());
        public Task SaveAsync(VaultEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static VaultEntry EntryWithCreds() => new()
    {
        Id = "1",
        Issuer = "Example VPN",
        Account = "alice",
        Secret = "JBSWY3DPEHPK3PXP",
        VpnUsername = "alice",
        VpnPassword = "s3cret",
    };

    [Fact]
    public async Task ConnectWithVault_Throws_WhenUnlockRejected()
    {
        var vault = new OtpVault(new DenyGate(), new NullStore());
        await using var session = new VpnSession();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.ConnectWithVaultAsync(vault, EntryWithCreds()));

        Assert.Contains("Unlock cancelled", ex.Message);
    }

    [Fact]
    public async Task ConnectWithVault_Throws_WhenUsernameMissing()
    {
        var vault = new OtpVault(new AllowGate(), new NullStore());
        await using var session = new VpnSession();

        var entry = EntryWithCreds() with { VpnUsername = null };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.ConnectWithVaultAsync(vault, entry));

        Assert.Contains("username", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
