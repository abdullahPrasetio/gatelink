namespace GateLink.Core;

/// <summary>
/// GlobalProtect session cookie obtained from the authentication phase.
/// Mirrors the fields OpenConnect emits on stdout when run with
/// <c>--protocol=gp --authenticate</c>:
///   COOKIE, HOST, CONNECT_URL, FINGERPRINT, RESOLVE.
/// These values are then fed back into the connect phase.
/// </summary>
public sealed record GpCookie
{
    /// <summary>The WebVPN session cookie (passed to openconnect via stdin).</summary>
    public required string Cookie { get; init; }

    /// <summary>Resolved gateway host for the tunnel.</summary>
    public required string Host { get; init; }

    /// <summary>Full connect URL reported by the portal.</summary>
    public string? ConnectUrl { get; init; }

    /// <summary>Server certificate fingerprint, pinned on the connect call.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>Optional host:ip resolve hint.</summary>
    public string? Resolve { get; init; }

    /// <summary>
    /// Parses the key=value block OpenConnect prints after a successful
    /// <c>--authenticate</c> run. Unknown keys are ignored.
    /// </summary>
    public static GpCookie Parse(string authenticateOutput)
    {
        string? cookie = null, host = null, connectUrl = null, fingerprint = null, resolve = null;

        foreach (var raw in authenticateOutput.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim().ToUpperInvariant();
            var value = Unquote(line[(eq + 1)..].Trim());

            switch (key)
            {
                case "COOKIE": cookie = value; break;
                case "HOST": host = value; break;
                case "CONNECT_URL": connectUrl = value; break;
                case "FINGERPRINT": fingerprint = value; break;
                case "RESOLVE": resolve = value; break;
            }
        }

        if (string.IsNullOrEmpty(cookie))
            throw new InvalidOperationException(
                "Failed to obtain WebVPN cookie. Wrong password or OTP?");

        static string? Unquote(string? v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            // openconnect prints shell-style values, e.g. HOST='103.63.96.190'.
            // Strip a single pair of surrounding single or double quotes.
            if (v.Length >= 2 &&
                ((v[0] == '\'' && v[^1] == '\'') || (v[0] == '"' && v[^1] == '"')))
                return v[1..^1];
            return v;
        }

        return new GpCookie
        {
            Cookie = cookie,
            Host = host ?? VpnConfig.Gateway,
            ConnectUrl = connectUrl,
            Fingerprint = fingerprint,
            Resolve = resolve,
        };
    }
}
