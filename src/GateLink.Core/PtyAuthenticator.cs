using System.Diagnostics;
using System.Text;

namespace GateLink.Core;

/// <summary>
/// Runs the openconnect authenticate phase inside a pseudo-terminal (PTY).
///
/// GlobalProtect's openconnect reads the password and OTP from a TTY, not from a
/// plain stdin pipe — feeding a pipe yields "fgets (stdin): Inappropriate ioctl
/// for device" and the credentials are never accepted. The reference client
/// handles this with a PTY process; we reproduce that on macOS/Linux using the
/// system <c>expect</c> utility, which allocates a PTY and answers each prompt
/// in sequence.
/// </summary>
public static class PtyAuthenticator
{
    /// <summary>
    /// Authenticates via a PTY and returns openconnect's stdout (the cookie
    /// block). Throws with a diagnostic if authentication fails.
    /// </summary>
    public static async Task<string> AuthenticateAsync(
        string openConnectPath,
        string gateway,
        string protocol,
        string username,
        string password,
        string otp,
        CancellationToken ct = default)
    {
        var expect = FindExpect()
            ?? throw new InvalidOperationException(
                "The 'expect' utility was not found; it is required to drive the login prompts.");

        // Build an expect script that launches openconnect in a PTY, answers the
        // first prompt (password) and the second prompt (OTP), then prints output.
        // Prompts are matched case-insensitively on common GlobalProtect labels.
        var script = BuildExpectScript(openConnectPath, gateway, protocol, username, password, otp);

        var psi = new ProcessStartInfo(expect)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-");   // read script from stdin

        using var proc = new Process { StartInfo = psi };
        var output = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.StandardInput.WriteAsync(script);
        proc.StandardInput.Close();

        // A gateway that stops answering can leave expect/openconnect waiting
        // forever, holding the password/OTP in memory. Bound the login phase.
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

        var text = output.ToString();

        // dyld errors mean the bundled openconnect was built for a newer macOS
        // than this machine. Do not present that as a credential failure.
        if (text.Contains("Symbol not found", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("dyld", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("built for macOS", StringComparison.OrdinalIgnoreCase))
        {
            var detail = ExtractError(text);
            throw new InvalidOperationException(
                string.IsNullOrEmpty(detail)
                    ? "OpenConnect is incompatible with this macOS. Rebuild the bundled engine."
                    : $"OpenConnect is incompatible with this macOS: {detail}");
        }

        if (!text.Contains("COOKIE", StringComparison.OrdinalIgnoreCase))
        {
            var detail = ExtractError(text);
            throw new InvalidOperationException(
                string.IsNullOrEmpty(detail)
                    ? "Login failed. Check username, password and OTP."
                    : $"Login failed: {detail}");
        }

        return text;
    }

    private static string BuildExpectScript(
        string exe, string gateway, string protocol,
        string username, string password, string otp)
    {
        // Escape braces/backslashes for the Tcl-based expect interpreter.
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]")
                                        .Replace("{", "\\{").Replace("}", "\\}").Replace("\"", "\\\"").Replace("$", "\\$");

        var u = Esc(username);
        var p = Esc(password);
        var o = Esc(otp);
        var gw = Esc(gateway);

        // This GlobalProtect portal uses a SINGLE password prompt and expects
        // the password and the OTP concatenated together (password immediately
        // followed by the 6-digit code, no separator) — confirmed against the
        // live gateway and matching the reference client, which builds
        // pendingPassword = password + otp. We also force gateway login with
        // --usergroup=gateway; without it openconnect hits the portal endpoint
        // (/global-protect/getconfig.esp) which returns HTTP 512 and loops.
        return $$"""
            set timeout 30
            log_user 1
            spawn {{exe}} --protocol={{protocol}} --usergroup=gateway -u {{u}} --authenticate {{gw}}
            expect {
                -re {(?i)(password|passcode|login)[^\n]*:} {
                    send "{{p}}{{o}}\r"
                }
                eof {}
                timeout { exit 2 }
            }
            expect eof
            catch wait result
            exit [lindex $result 3]
            """;
    }

    private static string ExtractError(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Unexpected", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Symbol not found", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("dyld", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("built for macOS", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("spawn id", StringComparison.OrdinalIgnoreCase))
                return line.Length > 120 ? line[..120] : line;
        }
        return string.Empty;
    }

    private static string? FindExpect()
    {
        foreach (var p in new[] { "/usr/bin/expect", "/usr/local/bin/expect", "/opt/homebrew/bin/expect" })
            if (File.Exists(p)) return p;
        return null;
    }
}
