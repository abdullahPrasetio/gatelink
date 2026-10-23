namespace GateLink.Core.Vault;

/// <summary>
/// Coordinates unlock state and secret access, reproducing MantraVPN's
/// BiometricGate behaviour: a single biometric/password verification unlocks
/// the vault for a sliding window, during which reveal, copy and connect all
/// proceed without re-prompting. After <see cref="RelockAfter"/> of inactivity
/// the vault auto-relocks (the app's scheduleRelock), and the next
/// secret-touching action prompts again.
/// </summary>
public sealed class OtpVault
{
    private readonly IBiometricGate _gate;
    private readonly ISecretStore _store;
    private readonly TimeProvider _time;

    private DateTimeOffset _unlockedUntil = DateTimeOffset.MinValue;

    public TimeSpan RelockAfter { get; set; } = TimeSpan.FromMinutes(1);

    public event EventHandler? LockStateChanged;

    public OtpVault(IBiometricGate gate, ISecretStore store, TimeProvider? time = null)
    {
        _gate = gate;
        _store = store;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>True while the vault is unlocked (within the sliding window).</summary>
    public bool IsUnlocked => _time.GetUtcNow() < _unlockedUntil;

    /// <summary>
    /// Ensures the vault is unlocked, prompting for biometrics only if it is
    /// currently locked. Each successful access refreshes the relock timer
    /// (sliding window), matching scheduleRelock.
    /// </summary>
    public async Task<bool> EnsureUnlockedAsync(string reason, CancellationToken ct = default)
    {
        if (IsUnlocked)
        {
            ScheduleRelock();   // refresh the window on activity
            return true;
        }

        var ok = await _gate.VerifyAsync(reason, ct);
        if (ok)
        {
            ScheduleRelock();
            LockStateChanged?.Invoke(this, EventArgs.Empty);
        }
        return ok;
    }

    /// <summary>Immediately locks the vault.</summary>
    public void Lock()
    {
        _unlockedUntil = DateTimeOffset.MinValue;
        LockStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleRelock() => _unlockedUntil = _time.GetUtcNow() + RelockAfter;

    /// <summary>
    /// Returns the current OTP for an entry, unlocking first if needed. Used by
    /// both "reveal/copy" in the UI and by the connect flow.
    /// </summary>
    public async Task<string?> GetOtpAsync(VaultEntry entry, CancellationToken ct = default)
    {
        if (!await EnsureUnlockedAsync($"Reveal one-time code for {entry.Issuer}", ct))
            return null;
        return entry.CreateGenerator().Generate();
    }

    public Task<IReadOnlyList<VaultEntry>> LoadAllAsync(CancellationToken ct = default)
        => _store.LoadAllAsync(ct);

    public Task SaveAsync(VaultEntry entry, CancellationToken ct = default)
        => _store.SaveAsync(entry, ct);

    public Task DeleteAsync(string id, CancellationToken ct = default)
        => _store.DeleteAsync(id, ct);
}
