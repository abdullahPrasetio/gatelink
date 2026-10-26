using GateLink.Core.Otp;

namespace GateLink.Core.Vault;

/// <summary>
/// Coordinates unlock state and secret access, reproducing MantraVPN's
/// BiometricGate behaviour: a single biometric/password verification unlocks
/// the vault for a sliding window, during which reveal, copy and connect all
/// proceed without re-prompting. After <see cref="RelockAfter"/> of inactivity
/// the vault auto-relocks and cached key material is dropped.
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
    /// Ensures the vault is unlocked, prompting only if it is currently locked.
    /// Each successful access refreshes the relock timer.
    /// </summary>
    public async Task<bool> EnsureUnlockedAsync(string reason, CancellationToken ct = default)
    {
        if (IsUnlocked)
        {
            ScheduleRelock();   // refresh the window on activity
            return true;
        }

        // Drop any key material left over from an expired window before asking
        // the gate to verify the user again.
        _store.Lock();

        var ok = await _gate.VerifyAsync(reason, ct);
        if (ok)
        {
            ScheduleRelock();
            LockStateChanged?.Invoke(this, EventArgs.Empty);
        }
        return ok;
    }

    /// <summary>Immediately locks the vault and clears cached key material.</summary>
    public void Lock()
    {
        _unlockedUntil = DateTimeOffset.MinValue;
        _store.Lock();
        LockStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleRelock() => _unlockedUntil = _time.GetUtcNow() + RelockAfter;

    /// <summary>
    /// Loads the protected part of an entry after unlocking. Returns null when
    /// the user cancels or the store cannot decrypt.
    /// </summary>
    public async Task<VaultSecret?> LoadSecretAsync(VaultEntry entry, CancellationToken ct = default)
    {
        if (!await EnsureUnlockedAsync($"Unlock {entry.Issuer}", ct))
            return null;

        // Tests and simple callers may supply the secret inline.
        if (!string.IsNullOrEmpty(entry.Secret))
            return new VaultSecret(entry.Secret, entry.VpnPassword);

        var secret = await _store.LoadSecretAsync(entry.Id, ct);
        if (secret is null)
            Lock();
        return secret;
    }

    /// <summary>Returns the current OTP, unlocking and loading the seed if needed.</summary>
    public async Task<string?> GetOtpAsync(VaultEntry entry, CancellationToken ct = default)
    {
        var secret = await LoadSecretAsync(entry, ct);
        if (secret is null) return null;
        return CreateGenerator(entry, secret).Generate();
    }

    /// <summary>
    /// Returns a generator for the live UI countdown, unlocking and loading the
    /// seed if needed. Null when the user cancels.
    /// </summary>
    public async Task<TotpGenerator?> GetGeneratorAsync(VaultEntry entry, CancellationToken ct = default)
    {
        var secret = await LoadSecretAsync(entry, ct);
        return secret is null ? null : CreateGenerator(entry, secret);
    }

    private static TotpGenerator CreateGenerator(VaultEntry entry, VaultSecret secret)
        => TotpGenerator.FromBase32(secret.Secret, entry.Digits, entry.Period, entry.Algorithm);

    public Task<IReadOnlyList<VaultEntry>> LoadAllAsync(CancellationToken ct = default)
        => _store.LoadAllAsync(ct);

    public Task SaveAsync(VaultEntry entry, CancellationToken ct = default)
        => _store.SaveAsync(entry, ct);

    public Task DeleteAsync(string id, CancellationToken ct = default)
        => _store.DeleteAsync(id, ct);
}
