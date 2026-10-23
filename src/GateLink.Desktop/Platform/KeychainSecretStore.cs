using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GateLink.Core.Vault;

namespace GateLink.Desktop.Platform;

/// <summary>
/// Persists vault entries in the macOS login Keychain via the <c>security</c>
/// command-line tool. Each entry is stored as a generic password item whose
/// account is the entry id and whose secret payload is the JSON-serialised
/// <see cref="VaultEntry"/>. The Keychain encrypts the data at rest and is
/// unlocked by the user's login session.
///
/// Using the CLI avoids native interop and works on every macOS version without
/// extra dependencies.
/// </summary>
public sealed class KeychainSecretStore : ISecretStore
{
    private const string Service = "com.gatelink.vault";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public async Task<IReadOnlyList<VaultEntry>> LoadAllAsync(CancellationToken ct = default)
    {
        // `security dump-keychain` is noisy and needs confirmation; instead we
        // keep an index of ids in a single item and read each by account.
        var ids = await LoadIndexAsync(ct);
        var result = new List<VaultEntry>();
        foreach (var id in ids)
        {
            var json = await ReadItemAsync(id, ct);
            if (json is null) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<VaultEntry>(json, Json);
                if (entry is not null) result.Add(entry);
            }
            catch (JsonException)
            {
                // Skip corrupt entries rather than failing the whole load.
            }
        }
        return result;
    }

    public async Task SaveAsync(VaultEntry entry, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(entry, Json);
        await WriteItemAsync(entry.Id, json, ct);

        var ids = (await LoadIndexAsync(ct)).ToHashSet();
        if (ids.Add(entry.Id))
            await WriteItemAsync(IndexAccount, string.Join(',', ids), ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await DeleteItemAsync(id, ct);
        var ids = (await LoadIndexAsync(ct)).ToHashSet();
        if (ids.Remove(id))
            await WriteItemAsync(IndexAccount, string.Join(',', ids), ct);
    }

    // ---- index of stored ids (so we can enumerate) ----

    private const string IndexAccount = "__index__";

    private async Task<IReadOnlyList<string>> LoadIndexAsync(CancellationToken ct)
    {
        var raw = await ReadItemAsync(IndexAccount, ct);
        if (string.IsNullOrEmpty(raw)) return Array.Empty<string>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
    }

    // ---- security CLI wrappers ----

    private static async Task WriteItemAsync(string account, string secret, CancellationToken ct)
    {
        // -U updates if the item already exists. -w takes the secret value.
        await RunSecurityAsync(new[]
        {
            "add-generic-password", "-U",
            "-s", Service,
            "-a", account,
            "-w", secret,
        }, ct);
    }

    private static async Task<string?> ReadItemAsync(string account, CancellationToken ct)
    {
        var (code, stdout, _) = await RunSecurityAsync(new[]
        {
            "find-generic-password",
            "-s", Service,
            "-a", account,
            "-w",
        }, ct);

        return code == 0 ? stdout.TrimEnd('\n') : null;
    }

    private static async Task DeleteItemAsync(string account, CancellationToken ct)
    {
        await RunSecurityAsync(new[]
        {
            "delete-generic-password",
            "-s", Service,
            "-a", account,
        }, ct);
    }

    private static async Task<(int code, string stdout, string stderr)> RunSecurityAsync(
        string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("/usr/bin/security")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Failed to start /usr/bin/security.");
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode, stdout, stderr);
    }
}
