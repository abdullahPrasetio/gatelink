using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using GateLink.Core;
using GateLink.Core.Otp;
using GateLink.Core.Vault;
using GateLink.Desktop.Platform;

namespace GateLink.Desktop;

/// <summary>
/// Presentation state for the main window: the live OTP, countdown, connection
/// phase and the add-account form. Talks only to GateLink.Core, so the same
/// logic runs on macOS and Windows.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly OtpVault _vault;
    private readonly VpnSession _session;
    private readonly DispatcherTimer _timer;

    private readonly List<VaultEntry> _entries = new();
    private VaultEntry? _entry;      // currently displayed account
    private TotpGenerator? _generator;
    private bool _connected;

    public MainViewModel()
    {
        // Pick the real platform store when available; fall back to in-memory
        // elsewhere so the app still runs on unsupported platforms.
        ISecretStore store;
        IBiometricGate gate;
        if (OperatingSystem.IsMacOS())
        {
            var macStore = new EncryptedFileSecretStore();
            store = macStore;
            gate = new VaultKeyGate(macStore);
        }
#if WINDOWS
        else
        {
            store = new DpapiSecretStore();
            gate = new WindowsHelloGate();
        }
#else
        else
        {
            store = new InMemorySecretStore();
            gate = new StubBiometricGate();
        }
#endif

        _vault = new OtpVault(gate, store)
        {
            RelockAfter = TimeSpan.FromMinutes(1),
        };

        _session = new VpnSession();
        _session.PhaseChanged += (_, phase) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (phase == VpnPhase.Connected) _connectedSince = DateTime.Now;
                Phase = phase;
                ButtonText = PhaseLabel(phase);
                _connected = phase is VpnPhase.Connected;
                // Clear transient error text when a new phase starts cleanly.
                if (phase is VpnPhase.Authenticating or VpnPhase.Connecting or VpnPhase.Connected)
                    StatusText = "";
            });
        _session.Log += (_, line) => System.Diagnostics.Debug.WriteLine($"[vpn] {line}");

        Gateway = Platform.AppSettings.LoadGateway() ?? VpnConfig.Gateway;
        _openAtLogin = Platform.LoginItem.IsEnabled;  // reflect real state
        ButtonText = "Connect";
        VersionText = "v" + UpdateChecker.CurrentVersion.ToString(3);
        if (!_session.IsEnvironmentReady)
            StatusText = "OpenConnect not found. Install it (brew install openconnect).";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshOtp();
        _timer.Start();

        // If a tunnel is still up from a previous run (app was quit while
        // connected), reflect that instead of showing "Not connected".
        _session.AdoptExistingTunnel();

        _ = LoadSavedAccountAsync();
        _ = CheckForUpdateAsync();
    }

    // ---- auto-update ----

    private ReleaseInfo? _pendingUpdate;

    private bool _updateAvailable;
    public bool UpdateAvailable { get => _updateAvailable; private set => Set(ref _updateAvailable, value); }

    private string _updateText = "";
    public string UpdateText { get => _updateText; private set => Set(ref _updateText, value); }

    private async Task CheckForUpdateAsync()
    {
        var release = await UpdateChecker.CheckAsync();
        if (release is { IsNewer: true })
        {
            _pendingUpdate = release;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateAvailable = true;
                UpdateText = $"New update ({release.TagName})";
            });
        }
    }

    /// <summary>Invoked when the user clicks the "New update" label.</summary>
    public async void InstallUpdate()
    {
        if (_pendingUpdate is null) return;
        try
        {
            UpdateText = "Updating...";
            await Platform.AutoUpdater.UpdateAsync(
                _pendingUpdate,
                msg => Dispatcher.UIThread.Post(() => UpdateText = msg));
        }
        catch (Exception ex)
        {
            UpdateText = $"Update failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Loads the first saved account from the vault (Keychain) on startup. If
    /// none exists, the account card stays empty and the user is prompted to add
    /// one with the + button. We no longer seed a fake demo account, because its
    /// OTP would be rejected by the real gateway.
    /// </summary>
    private async Task LoadSavedAccountAsync()
    {
        try
        {
            var entries = (await _vault.LoadAllAsync()).Where(e => e.Id != "demo").ToList();
            if (entries.Count > 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _entries.Clear();
                    _entries.AddRange(entries);
                    SetEntry(entries[0]);
                });
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    AddMessage = "No account yet. Tap + to add your 2FA account.");
            }
        }
        catch
        {
            // Ignore load errors; user can still add an account.
        }
    }

    // ---- bindable properties ----

    private string _gateway = "";
    public string Gateway { get => _gateway; set => Set(ref _gateway, value); }

    private string _versionText = "";
    public string VersionText { get => _versionText; private set => Set(ref _versionText, value); }

    private string _otpDisplay = "— — —";
    public string OtpDisplay { get => _otpDisplay; private set => Set(ref _otpDisplay, value); }

    private int _secondsRemaining;
    public int SecondsRemaining { get => _secondsRemaining; private set => Set(ref _secondsRemaining, value); }

    private string _issuer = "";
    public string Issuer { get => _issuer; private set => Set(ref _issuer, value); }

    private string _account = "";
    public string Account { get => _account; private set => Set(ref _account, value); }

    // Short action word on the button (Connect / Disconnect / Connecting…).
    private string _buttonText = "Connect";
    public string ButtonText { get => _buttonText; private set => Set(ref _buttonText, value); }

    // Free-form message / error shown below the button (wraps, never truncated).
    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set
        {
            Set(ref _statusText, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasStatusMessage)));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusText);

    // Current phase drives the status colours (like MantraVPN: green when
    // connected, neutral/grey when idle, blue while working).
    private VpnPhase _phase = VpnPhase.Idle;
    public VpnPhase Phase
    {
        get => _phase;
        private set
        {
            Set(ref _phase, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusColor)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLine)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowForm)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConnectButtonColor)));
        }
    }

    public bool IsConnected => _phase is VpnPhase.Connected;

    /// <summary>Show the credential form only when not connected.</summary>
    public bool ShowForm => _phase is not VpnPhase.Connected;

    /// <summary>Connect button colour: green when connected, blue otherwise.</summary>
    public string ConnectButtonColor => _phase switch
    {
        VpnPhase.Connected => "#248a3d",      // darker green (Disconnect)
        VpnPhase.Reconnecting or VpnPhase.Disconnecting => "#8e8e93",
        _ => "#0a84ff",                         // blue (Connect)
    };

    /// <summary>Colour string for the status line and tray accent.</summary>
    public string StatusColor => _phase switch
    {
        VpnPhase.Connected => "#30d158",     // green
        VpnPhase.Failed => "#ff453a",        // red
        VpnPhase.Idle => "#8e8e93",          // neutral grey
        _ => "#0a84ff",                        // blue while authenticating/connecting
    };

    private DateTime _connectedSince;

    /// <summary>Human status line, e.g. "Connected to vpn.example.com since 16:18".</summary>
    public string StatusLine => _phase switch
    {
        VpnPhase.Connected => $"Connected to {Gateway} since {_connectedSince:HH:mm}",
        VpnPhase.Authenticating => "Authenticating…",
        VpnPhase.Connecting => "Connecting…",
        VpnPhase.Reconnecting => "Reconnecting…",
        VpnPhase.Disconnecting => "Disconnecting…",
        VpnPhase.Failed => "Not connected",
        _ => "Not connected",
    };

    private bool _hasAccount;
    public bool HasAccount { get => _hasAccount; private set => Set(ref _hasAccount, value); }

    // "Open at Login" — reflects and controls the real macOS LaunchAgent.
    private bool _openAtLogin;
    public bool OpenAtLogin
    {
        get => _openAtLogin;
        set
        {
            if (!Set(ref _openAtLogin, value)) return;
            Platform.LoginItem.Set(value);
        }
    }

    private string _username = "";
    public string Username { get => _username; set => Set(ref _username, value); }

    private string _password = "";
    public string Password { get => _password; set => Set(ref _password, value); }

    private bool _passwordRevealed;
    public bool PasswordRevealed
    {
        get => _passwordRevealed;
        private set
        {
            Set(ref _passwordRevealed, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PasswordChar)));
        }
    }

    /// <summary>Bullet char when masked; null reveals the text (with RevealPassword).</summary>
    public char PasswordChar => PasswordRevealed ? '\0' : '\u25CF';

    public void TogglePassword() => PasswordRevealed = !PasswordRevealed;

    private bool _otpRevealed;
    public bool OtpRevealed
    {
        get => _otpRevealed;
        private set { Set(ref _otpRevealed, value); RefreshOtp(); }
    }

    /// <summary>OTP shown either as bullets (masked) or the real code.</summary>
    public string OtpMasked => OtpRevealed ? OtpDisplay : "● ● ● ● ● ●";

    /// <summary>
    /// Reveals the OTP only after the vault is unlocked (Touch ID / password).
    /// Hiding needs no auth. While the vault stays unlocked (sliding window),
    /// subsequent reveals don't re-prompt — matching the reference app.
    /// </summary>
    public async void ToggleReveal()
    {
        if (OtpRevealed) { OtpRevealed = false; return; }   // hiding: no auth
        if (_entry is null) return;

        var generator = await _vault.GetGeneratorAsync(_entry);
        if (generator is null)
        {
            StatusText = "Unlock required to reveal the code";
            return;
        }

        _generator = generator;
        OtpRevealed = true;
    }

    // add-account form
    private string _manualUri = "";
    public string ManualUri { get => _manualUri; set => Set(ref _manualUri, value); }

    private string _addMessage = "";
    public string AddMessage { get => _addMessage; private set => Set(ref _addMessage, value); }

    // ---- commands (invoked from code-behind click handlers) ----

    /// <summary>Adds an entry produced by the Add account dialog and persists it.</summary>
    public async void AddEntry(VaultEntry entry)
    {
        if (!await _vault.EnsureUnlockedAsync("Save this account"))
        {
            AddMessage = "Unlock cancelled; account not saved.";
            return;
        }

        // Replace an existing account with the same id, else append.
        _entries.RemoveAll(e => e.Id == entry.Id);
        _entries.Add(entry);
        SetEntry(entry);
        AddMessage = $"Added {entry.Issuer} ({entry.Account}).";
        try { await _vault.SaveAsync(entry); }
        catch (Exception ex) { AddMessage = $"Saved in session only: {ex.Message}"; }
    }

    /// <summary>
    /// Finds the stored account whose username/account label matches the typed
    /// username, so entering username A uses account A's OTP. Falls back to the
    /// currently displayed account when there is no match (single-account case).
    /// </summary>
    private VaultEntry? MatchEntryForUsername(string username)
    {
        var u = username.Trim();
        var match = _entries.FirstOrDefault(e =>
            string.Equals(e.Account, u, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.VpnUsername, u, StringComparison.OrdinalIgnoreCase));
        return match ?? _entry;
    }

    /// <summary>Deletes the displayed account from the vault and UI.</summary>
    public async void DeleteCurrentAccount()
    {
        if (_entry is null) return;
        if (!await _vault.EnsureUnlockedAsync("Delete this account"))
        {
            AddMessage = "Unlock cancelled; account not deleted.";
            return;
        }

        var id = _entry.Id;
        _entries.RemoveAll(e => e.Id == id);
        try { await _vault.DeleteAsync(id); } catch { /* best effort */ }

        var next = _entries.FirstOrDefault();
        if (next is not null) SetEntry(next);
        else
        {
            _entry = null;
            _generator = null;
            HasAccount = false;
            AddMessage = "Account deleted. Tap + to add one.";
        }
    }

    /// <summary>Builds a human-readable prerequisite checklist for the UI.</summary>
    public string PrerequisitesText()
    {
        var report = Prerequisites.Check(new VpnEnvironment());
        var lines = report.Items.Select(i =>
        {
            var mark = i.Ok ? "✅" : "❌";
            var fix = i.Ok || string.IsNullOrEmpty(i.FixHint) ? "" : $"\n     → {i.FixHint}";
            return $"{mark}  {i.Name}: {i.Detail}{fix}";
        });
        var header = report.CanConnect
            ? "Ready to connect."
            : "Missing requirements — connect will fail until fixed.";
        return header + "\n\n" + string.Join("\n", lines);
    }

    /// <summary>Returns a details string for the current account (no secret).</summary>
    public string CurrentAccountDetails()
    {
        if (_entry is null) return "No account.";
        return $"Issuer: {_entry.Issuer}\nAccount: {_entry.Account}\n" +
               $"Digits: {_entry.Digits}\nPeriod: {_entry.Period}s\nAlgorithm: {_entry.Algorithm}";
    }

    public void AddFromManualUri()
    {
        try
        {
            var uri = OtpAuthUri.Parse(ManualUri.Trim());
            var entry = VaultEntry.FromOtpAuth(uri) with
            {
                VpnUsername = uri.Account,
            };
            SetEntry(entry);
            AddMessage = $"Added {entry.Issuer} ({entry.Account}).";
            ManualUri = "";
        }
        catch (Exception ex)
        {
            AddMessage = $"Could not add: {ex.Message}";
        }
    }

    public void AddFromQrFile(string path)
    {
        try
        {
            var uri = QrDecoder.DecodeOtpAuthFile(path);
            var entry = VaultEntry.FromOtpAuth(uri) with { VpnUsername = uri.Account };
            SetEntry(entry);
            AddMessage = $"Added {entry.Issuer} ({entry.Account}) from QR.";
        }
        catch (Exception ex)
        {
            AddMessage = $"Could not read QR: {ex.Message}";
        }
    }

    /// <summary>
    /// Connect (or disconnect) using the stored account. The OTP is generated by
    /// the app — no manual entry. Runs the real VpnSession, which invokes
    /// openconnect. The tunnel phase needs elevation; on macOS openconnect must
    /// be able to run with root (e.g. via a sudoers rule), otherwise it fails at
    /// the adapter-creation step and the error is surfaced here.
    /// </summary>
    public async void BeginConnect()
    {
        if (_connected || Phase == VpnPhase.Connected)
        {
            await _session.DisconnectAsync();
            _connected = false;
            return;
        }

        if (_entry is null) { StatusText = "Add a 2FA account first (+)"; return; }
        if (!_session.IsEnvironmentReady) { StatusText = "Install OpenConnect"; return; }
        if (string.IsNullOrWhiteSpace(Username))
        {
            StatusText = "Enter username";
            return;
        }
        if (string.IsNullOrWhiteSpace(Gateway))
        {
            StatusText = "Enter a portal/gateway";
            return;
        }

        // Apply the (possibly edited) gateway so openconnect targets it.
        VpnConfig.Gateway = Gateway.Trim();
        if (!TunnelArgs.IsValidHost(VpnConfig.Gateway))
        {
            StatusText = "Gateway must be a hostname or IPv4 address (letters, digits, dots, hyphens).";
            return;
        }

        // Remember the form value for the next launch (portal only, not secrets).
        Platform.AppSettings.SaveGateway(VpnConfig.Gateway);

        // On macOS the tunnel runs through a root-owned fixed-purpose helper.
        // EnsureInstalledAsync probes the existing helper first, so this does
        // not prompt when everything is already installed and authorized.
        if (OperatingSystem.IsMacOS())
        {
            if (!Platform.TunnelHelper.IsInstalled)
            {
                StatusText = "GateLink needs a one-time root helper to create the VPN tunnel. " +
                             "It runs only the bundled OpenConnect with fixed options.";
            }

            if (!await Platform.TunnelHelper.EnsureInstalledAsync())
            {
                StatusText = "Root helper installation failed or was cancelled. " +
                             "Run scripts/install-tunnel-helper.sh once, then retry.";
                return;
            }
        }

        // Match the account by the typed username so its OTP is used. Carry the
        // live username into the entry. A blank password means "use the stored
        // one"; VaultMerge preserves it, and ConnectWithVaultAsync loads it
        // after the unlock gate.
        var source = MatchEntryForUsername(Username) ?? _entry;
        var entry = source with
        {
            VpnUsername = Username,
            VpnPassword = string.IsNullOrWhiteSpace(Password) ? null : Password,
        };

        if (!await _vault.EnsureUnlockedAsync($"Connect to {VpnConfig.Gateway}"))
        {
            StatusText = "Unlock cancelled. VPN not connected.";
            return;
        }

        // Persist the credentials so they survive restarts (encrypted at rest).
        _entries.RemoveAll(e => e.Id == entry.Id);
        _entries.Add(entry);
        _entry = entry;
        try { await _vault.SaveAsync(entry); } catch { /* best effort */ }

        try
        {
            await _session.ConnectWithVaultAsync(_vault, entry);
            _connected = true;
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;   // shown (wrapped) below the button
        }
    }

    private static string PhaseLabel(VpnPhase phase) => phase switch
    {
        VpnPhase.Idle => "Connect",
        VpnPhase.Authenticating => "Authenticating…",
        VpnPhase.Connecting => "Connecting…",
        VpnPhase.Connected => "Disconnect",
        VpnPhase.Reconnecting => "Reconnecting…",
        VpnPhase.Disconnecting => "Disconnecting…",
        VpnPhase.Failed => "Connect",
        _ => "Connect",
    };

    public async Task CopyOtpAsync(Func<string, Task> setClipboard)
    {
        if (_entry is null) return;
        var code = await _vault.GetOtpAsync(_entry);
        if (code is null) { StatusText = "Unlock cancelled"; return; }
        await setClipboard(code);
        StatusText = "Code copied";
    }

    // ---- internals ----

    private void SetEntry(VaultEntry entry)
    {
        _entry = entry;
        // Metadata-only entries (loaded at startup) have no seed until the
        // vault is unlocked; do not construct a generator from an empty secret.
        _generator = string.IsNullOrEmpty(entry.Secret) ? null : entry.CreateGenerator();
        Issuer = entry.Issuer;
        Account = entry.Account;
        if (!string.IsNullOrEmpty(entry.VpnUsername)) Username = entry.VpnUsername;
        if (!string.IsNullOrEmpty(entry.VpnPassword)) Password = entry.VpnPassword;
        HasAccount = true;
        RefreshOtp();
    }

    private void RefreshOtp()
    {
        // Drop the in-memory generator as soon as the sliding window expires so
        // the code stops updating (and the seed is no longer held by the UI).
        if (_generator is not null && !_vault.IsUnlocked)
        {
            _generator = null;
            _vault.Lock();
            if (_otpRevealed)
            {
                _otpRevealed = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OtpMasked)));
            }
        }

        if (_generator is null)
        {
            OtpDisplay = "— — —";
            SecondsRemaining = 0;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OtpMasked)));
            return;
        }

        OtpDisplay = FormatCode(_generator.Generate());
        SecondsRemaining = _generator.SecondsRemaining();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OtpMasked)));
    }

    private static string FormatCode(string code)
        => code.Length == 6 ? $"{code[..3]} {code[3..]}" : code;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
