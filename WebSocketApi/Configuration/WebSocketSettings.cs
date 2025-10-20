namespace WebSocketApi.Configuration;

public class WebSocketSettings
{
    public const string SectionName = "WebSocketSettings";

    /// <summary>
    /// Keep-alive interval in seconds. Default: 30
    /// </summary>
    public int KeepAliveIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Buffer size for receiving messages in bytes. Default: 16KB
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 16 * 1024; // 16KB

    /// <summary>
    /// Maximum message size in bytes. Default: 1MB
    /// </summary>
    public int MaxMessageSize { get; set; } = 1024 * 1024; // 1MB

    /// <summary>
    /// Timeout for receiving messages in seconds. Default: 120 (2 minutes)
    /// </summary>
    public int ReceiveTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Timeout for idle connections in seconds. Default: 300 (5 minutes)
    /// </summary>
    public int IdleTimeoutSeconds { get; set; } = 300;
}
