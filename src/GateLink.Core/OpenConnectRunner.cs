using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GateLink.Core;

/// <summary>
/// Launches and manages the openconnect process. This is the Windows analogue
/// of the macOS OpenConnectHelper.run() routine, which spawned openconnect via
/// an elevated NSTask. On Windows, elevation is expected to be provided by a
/// Windows Service or by running the client as administrator (TODO), since
/// creating the virtual adapter (Wintun/TAP) requires privilege.
/// </summary>
public sealed class OpenConnectRunner : IAsyncDisposable
{
    private readonly VpnEnvironment _env;
    private Process? _process;
    private string? _lastTunnelFailure;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<int>? Exited;

    public OpenConnectRunner(VpnEnvironment env) => _env = env;

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>
    /// First diagnostic line emitted by the tunnel process when it fails to come
    /// up (for example "sudo: a password is required"). Null while healthy.
    /// </summary>
    public string? LastTunnelFailure => _lastTunnelFailure;

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Builds the start info for the privileged tunnel process. On Windows the
    /// executable is launched directly (elevation comes from running the app as
    /// administrator / a service). On macOS it invokes the root-owned
    /// fixed-purpose helper; the helper builds the openconnect argv itself, so
    /// the app can never inject script paths or arbitrary options.
    /// </summary>
    private ProcessStartInfo BuildTunnelStartInfo(GpCookie cookie)
    {
        ProcessStartInfo psi;
        if (IsWindows)
        {
            var exe = _env.OpenConnectPath
                      ?? throw new InvalidOperationException("openconnect not found.");

            psi = new ProcessStartInfo(exe);
            psi.ArgumentList.Add($"--protocol={VpnConfig.Protocol}");
            psi.ArgumentList.Add("--usergroup=gateway");
            psi.ArgumentList.Add("--cookie-on-stdin");
            psi.ArgumentList.Add($"--servercert={cookie.Fingerprint}");
            psi.ArgumentList.Add("--reconnect-timeout=300");
            psi.ArgumentList.Add("--force-dpd=30");
            psi.ArgumentList.Add("--no-dtls");
            psi.ArgumentList.Add($"--pid-file={_env.PidPath}");
            psi.ArgumentList.Add(cookie.Host);
        }
        else
        {
            psi = new ProcessStartInfo("/usr/bin/sudo");
            psi.ArgumentList.Add("-n");               // never prompt for a password
            psi.ArgumentList.Add(_env.TunnelHelperPath);
            psi.ArgumentList.Add("connect");
            psi.ArgumentList.Add("--host");
            psi.ArgumentList.Add(cookie.Host);
            psi.ArgumentList.Add("--fingerprint");
            psi.ArgumentList.Add(cookie.Fingerprint!);
        }

        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        return psi;
    }

    /// <summary>
    /// Runs <c>openconnect --protocol=gp --authenticate &lt;gateway&gt;</c> with the
    /// supplied credentials piped to stdin, and returns the raw stdout so the
    /// caller can parse a <see cref="GpCookie"/>.
    /// </summary>
    public async Task<string> AuthenticateAsync(
        string username,
        string password,
        string otp,
        CancellationToken ct = default)
    {
        var exe = _env.OpenConnectPath
                  ?? throw new InvalidOperationException(
                      "openconnect not found. Install OpenConnect for Windows and ensure it is on PATH.");

        // GlobalProtect's openconnect reads the password and OTP from a TTY, not
        // a plain pipe. On macOS/Linux a pipe produces
        // "Inappropriate ioctl for device" and the credentials never register,
        // so we run the authenticate phase inside a real PTY (via expect),
        // matching the reference client's PTYProcess approach.
        if (!IsWindows)
        {
            return await PtyAuthenticator.AuthenticateAsync(
                exe, VpnConfig.Gateway, VpnConfig.Protocol, username, password, otp, ct);
        }

        // Windows: openconnect reads a stdin pipe fine (no PTY needed). Use the
        // same proven flags as macOS — force the gateway endpoint and send the
        // password with the OTP appended on a single line (this GlobalProtect
        // portal concatenates password + OTP into one credential prompt).
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add($"--protocol={VpnConfig.Protocol}");
        psi.ArgumentList.Add("--usergroup=gateway");
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(username);
        psi.ArgumentList.Add("--passwd-on-stdin");
        psi.ArgumentList.Add("--authenticate");
        psi.ArgumentList.Add(VpnConfig.Gateway);

        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // Single combined credential line: password immediately followed by OTP.
        await proc.StandardInput.WriteLineAsync(password + otp);
        await proc.StandardInput.FlushAsync(ct);
        proc.StandardInput.Close();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await proc.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            throw new InvalidOperationException(
                "Login timed out after 90 seconds. Check the gateway and try again.");
        }

        if (proc.ExitCode != 0)
        {
            // Surface a short diagnostic from openconnect's stderr so the real
            // cause (wrong credentials, portal unreachable, TLS error) is visible
            // rather than a generic message.
            var detail = FirstMeaningfulLine(stderr.ToString());
            var message = string.IsNullOrEmpty(detail)
                ? "Login failed. Wrong password or OTP."
                : $"Login failed: {detail}";
            throw new InvalidOperationException(message);
        }

        return stdout.ToString();
    }

    private static string FirstMeaningfulLine(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            // Skip pure progress/banner noise.
            if (line.StartsWith("POST ") || line.StartsWith("Connected to")) continue;
            return line.Length > 80 ? line[..80] : line;
        }
        return string.Empty;
    }

    /// <summary>
    /// Starts the tunnel:
    /// <c>openconnect --protocol=gp --cookie-on-stdin --servercert &lt;fp&gt; &lt;host&gt;</c>.
    /// The process stays alive until <see cref="StopAsync"/> is called.
    /// </summary>
    public void StartTunnel(GpCookie cookie)
    {
        if (IsRunning)
            throw new InvalidOperationException("A tunnel is already running.");

        // Values from the authenticated gateway cross the root boundary. Never
        // let them become options or paths: validate here and again in the
        // root-owned helper.
        if (!TunnelArgs.IsValidHost(cookie.Host))
            throw new InvalidOperationException(
                "The gateway returned an invalid tunnel host; refusing to start.");
        if (!TunnelArgs.IsValidFingerprint(cookie.Fingerprint))
            throw new InvalidOperationException(
                "The gateway did not return a valid certificate fingerprint; refusing to start.");

        // The tunnel phase must create a virtual interface (utun/tun), which
        // requires root. On macOS we invoke the root-owned helper through
        // `sudo -n`; it is the only command authorized by sudoers. The helper
        // adds --script, --csd-wrapper, --pid-file and all other options from
        // root-owned files, so nothing user-writable is executed as root.
        var psi = BuildTunnelStartInfo(cookie);

        var tunnelLog = new StringBuilder();
        var logLock = new object();
        _lastTunnelFailure = null;
        void Capture(string line)
        {
            // stdout and stderr fire on separate threads — guard the shared
            // StringBuilder to avoid a race (previously crashed in ToString()).
            string snapshot;
            lock (logLock)
            {
                tunnelLog.AppendLine(line);
                snapshot = tunnelLog.ToString();
                NoteFailure(line);
            }
            try { OutputReceived?.Invoke(this, line); }
            catch (Exception ex) { Debug.WriteLine($"[vpn] output handler: {ex.Message}"); }
            TryWriteTunnelLog(snapshot);
        }

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) Capture(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Capture(e.Data); };
        _process.Exited += (_, _) =>
        {
            try { Exited?.Invoke(this, _process?.ExitCode ?? -1); }
            catch (Exception ex)
            {
                // Process.Exited runs on a ThreadPool thread; an escaping
                // exception would terminate the whole app.
                Debug.WriteLine($"[vpn] exit event: {ex.Message}");
            }
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        // Feed the session cookie on stdin.
        _process.StandardInput.WriteLine(cookie.Cookie);
        _process.StandardInput.Flush();
    }

    private void NoteFailure(string line)
    {
        if (_lastTunnelFailure is not null) return;

        var text = line.Trim();
        if (text.Length == 0) return;

        if (text.Contains("sudo:", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("operation not permitted", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("cannot", StringComparison.OrdinalIgnoreCase))
        {
            _lastTunnelFailure = text.Length > 160 ? text[..160] : text;
        }
    }

    private static void TryWriteTunnelLog(string text)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var path = Path.Combine(home, "gatelink-tunnel.log");
            File.WriteAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{text}\n");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { /* best-effort diagnostics */ }
    }

    public async Task StopAsync()
    {
        // On Unix the tunnel runs as root, so the child openconnect can't be
        // killed by this unprivileged process directly. The root-owned helper
        // verifies the pid belongs to its own openconnect before signalling it.
        if (!IsWindows)
            await StopViaHelperAsync();

        if (_process is null || _process.HasExited) return;
        try
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
        catch (Exception)
        {
            // The helper already stopped the root child; sudo may have exited.
        }
    }

    private async Task StopViaHelperAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/sudo")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add(_env.TunnelHelperPath);
            psi.ArgumentList.Add("stop");

            using var stop = Process.Start(psi);
            if (stop is not null) await stop.WaitForExitAsync();
        }
        catch
        {
            // Best effort: fall through to killing the local sudo wrapper.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _process?.Dispose();
        _process = null;
    }
}
