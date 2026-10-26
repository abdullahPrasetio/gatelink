using System.Text.RegularExpressions;

namespace GateLink.Core;

/// <summary>
/// Validation for values that cross the unprivileged → root boundary.
/// The root-owned helper validates these again; this is defence in depth and
/// gives the UI a clear error before it asks for elevation.
/// </summary>
public static class TunnelArgs
{
    private static readonly Regex HostPattern = new(
        "^[A-Za-z0-9](?:[A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FingerprintPattern = new(
        // openconnect prints pin-sha256:<base64>, whose alphabet includes + / =.
        "^[A-Za-z0-9:._+/=-]{1,200}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A DNS name or IPv4 literal, never an option or a path.</summary>
    public static bool IsValidHost(string? host)
        => !string.IsNullOrEmpty(host)
           && host.Length <= 253
           && !host.StartsWith('-')
           && HostPattern.IsMatch(host);

    /// <summary>openconnect accepts sha256:… and pin-sha256:… fingerprints.</summary>
    public static bool IsValidFingerprint(string? fingerprint)
        => !string.IsNullOrEmpty(fingerprint)
           && fingerprint.Length <= 200
           && !fingerprint.StartsWith('-')
           && FingerprintPattern.IsMatch(fingerprint);

    /// <summary>A PID that is safe to signal; never 0/1 or a non-number.</summary>
    public static bool TryParseTunnelPid(string? text, out int pid)
        => int.TryParse(text?.Trim(), out pid) && pid > 1;
}
