namespace GateLink.Core;

/// <summary>
/// Static connection configuration. No portal is hard-coded or persisted: the
/// user enters it in the UI form, and the root-owned helper validates it at
/// connect time.
/// </summary>
public static class VpnConfig
{
    /// <summary>No compiled-in portal; the UI supplies it.</summary>
    public const string DefaultGateway = "";

    /// <summary>
    /// Active gateway host. Empty by default; set from the UI (Portal field).
    /// openconnect uses this value for both the authenticate and tunnel phases.
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
