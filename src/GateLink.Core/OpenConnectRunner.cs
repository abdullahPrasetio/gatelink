using System.Diagnostics;
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

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<int>? Exited;

    public OpenConnectRunner(VpnEnvironment env) => _env = env;

    public bool IsRunning => _process is { HasExited: false };

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Builds the start info for the privileged tunnel process. On Windows the
    /// executable is launched directly (elevation comes from running the app as
    /// administrator / a service). On macOS/Linux it is wrapped in
    /// <c>sudo -n</c> so a NOPASSWD sudoers rule can grant root without a prompt.
    /// </summary>
    private static ProcessStartInfo BuildTunnelStartInfo(string openConnectPath)
    {
        ProcessStartInfo psi;
        if (IsWindows)
        {
            psi = new ProcessStartInfo(openConnectPath);
        }
        else
        {
            psi = new ProcessStartInfo("/usr/bin/sudo");
            psi.ArgumentList.Add("-n");               // never prompt for a password
            psi.ArgumentList.Add(openConnectPath);
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

        await proc.WaitForExitAsync(ct);

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

        var exe = _env.OpenConnectPath
                  ?? throw new InvalidOperationException("openconnect not found.");

        // The tunnel phase must create a virtual interface (utun/tun), which
        // requires root. On macOS/Linux we invoke openconnect through `sudo -n`
        // (non-interactive): it succeeds only if a NOPASSWD sudoers rule for
        // openconnect is installed (see scripts/install-sudoers.sh). `-n` means
        // we never block on a password prompt; if the rule is missing, sudo
        // fails fast and the error surfaces to the UI.
        // Argument set mirrors the reference helper (from analysis):
        //   --protocol=gp --usergroup=gateway --cookie-on-stdin --servercert <fp>
        //   --reconnect-timeout=300 --force-dpd=30 --no-dtls --pid-file <pid> <host>
        // We keep it running in the foreground (no --background) so we can stream
        // output and detect exit.
        var psi = BuildTunnelStartInfo(exe);
        psi.ArgumentList.Add($"--protocol={VpnConfig.Protocol}");
        psi.ArgumentList.Add("--usergroup=gateway");
        psi.ArgumentList.Add("--cookie-on-stdin");
        if (!string.IsNullOrEmpty(cookie.Fingerprint))
            psi.ArgumentList.Add($"--servercert={cookie.Fingerprint}");
        psi.ArgumentList.Add("--reconnect-timeout=300");
        psi.ArgumentList.Add("--force-dpd=30");
        psi.ArgumentList.Add("--no-dtls");
        // Submit a HIP report if the gateway requires it (GlobalProtect Host
        // Integrity Protection). Without this the tunnel connects but the server
        // may quarantine/limit it.
        if (!string.IsNullOrEmpty(_env.HipReportScriptPath))
            psi.ArgumentList.Add($"--csd-wrapper={_env.HipReportScriptPath}");
        psi.ArgumentList.Add($"--pid-file={_env.PidPath}");
        psi.ArgumentList.Add(cookie.Host);

        var tunnelLog = new StringBuilder();
        void Capture(string line)
        {
            tunnelLog.AppendLine(line);
            OutputReceived?.Invoke(this, line);
            TryWriteTunnelLog(tunnelLog.ToString());
        }

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) Capture(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Capture(e.Data); };
        _process.Exited += (_, _) => Exited?.Invoke(this, _process?.ExitCode ?? -1);

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        // Feed the session cookie on stdin.
        _process.StandardInput.WriteLine(cookie.Cookie);
        _process.StandardInput.Flush();
    }

    private static void TryWriteTunnelLog(string text)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            File.WriteAllText(Path.Combine(home, "gatelink-tunnel.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{text}\n");
        }
        catch { /* best-effort diagnostics */ }
    }

    public async Task StopAsync()
    {
        // On Unix the tunnel runs as root (via sudo), so the child openconnect
        // can't be killed by this unprivileged process directly. Terminate it
        // via its pid file using `sudo -n kill`, which the same NOPASSWD rule
        // can permit. Then clean up the local sudo wrapper process.
        if (!IsWindows)
            await StopViaPidFileAsync();

        if (_process is null || _process.HasExited) return;
        try
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            // Process already gone.
        }
    }

    private async Task StopViaPidFileAsync()
    {
        try
        {
            if (!File.Exists(_env.PidPath)) return;
            var pidText = (await File.ReadAllTextAsync(_env.PidPath)).Trim();
            if (string.IsNullOrEmpty(pidText)) return;

            var psi = new ProcessStartInfo("/usr/bin/sudo")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add("/bin/kill");
            psi.ArgumentList.Add(pidText);

            using var kill = Process.Start(psi);
            if (kill is not null) await kill.WaitForExitAsync();
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
