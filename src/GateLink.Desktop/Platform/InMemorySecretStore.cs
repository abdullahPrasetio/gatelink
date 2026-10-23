using GateLink.Core.Vault;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Non-persistent store for development. Real builds use KeychainSecretStore
/// (macOS) or DpapiSecretStore (Windows) so secrets are encrypted at rest.
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, VaultEntry> _entries = new();

    public Task<IReadOnlyList<VaultEntry>> LoadAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<VaultEntry>>(_entries.Values.ToList());

    public Task SaveAsync(VaultEntry entry, CancellationToken ct = default)
    {
        _entries[entry.Id] = entry;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        _entries.Remove(id);
        return Task.CompletedTask;
    }
}
