namespace GateLink.Core;

/// <summary>
/// Static connection configuration. The gateway is intentionally hard-coded
/// for this build. Change the values here to retarget the client.
/// </summary>
public static class VpnConfig
{
    /// <summary>Default GlobalProtect portal/gateway host.</summary>
    public const string DefaultGateway = "vpn.bri.co.id";

    /// <summary>
    /// Active gateway host. Defaults to <see cref="DefaultGateway"/> but can be
    /// changed at runtime from the UI (the Portal field). openconnect uses this
    /// value for both the authenticate and tunnel phases.
    /// </summary>
    public static string Gateway { get; set; } = DefaultGateway;

    /// <summary>OpenConnect protocol selector. GlobalProtect = "gp".</summary>
    public const string Protocol = "gp";

    /// <summary>Product name used in UI, logs and runtime paths.</summary>
    public const string ProductName = "GateLink";

    /// <summary>Interval between tunnel health checks.</summary>
    public static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(15);

    /// <summary>Maximum automatic reconnect attempts before giving up.</summary>
    public const int MaxReconnectAttempts = 3;
}
