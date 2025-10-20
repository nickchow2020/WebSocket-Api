namespace WebSocketApi.Configuration;

public class CorsSettings
{
    public const string SectionName = "CorsSettings";

    /// <summary>
    /// Allowed origins for CORS
    /// </summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}
