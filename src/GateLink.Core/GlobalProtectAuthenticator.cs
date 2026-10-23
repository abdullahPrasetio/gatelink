namespace GateLink.Core;

/// <summary>
/// Drives the GlobalProtect authentication phase and produces a
/// <see cref="GpCookie"/>. Separated from the tunnel runner so the auth flow
/// can be tested and reasoned about independently.
/// </summary>
public sealed class GlobalProtectAuthenticator
{
    private readonly OpenConnectRunner _runner;

    public GlobalProtectAuthenticator(OpenConnectRunner runner) => _runner = runner;

    /// <summary>
    /// Authenticates with password + OTP and returns the parsed session cookie.
    /// Throws if credentials are rejected or the cookie cannot be obtained.
    /// </summary>
    public async Task<GpCookie> AuthenticateAsync(
        string username,
        string password,
        string otp,
        CancellationToken ct = default)
    {
        var output = await _runner.AuthenticateAsync(username, password, otp, ct);
        return GpCookie.Parse(output);
    }
}
