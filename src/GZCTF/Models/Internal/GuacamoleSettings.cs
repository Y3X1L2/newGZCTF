namespace GZCTF.Models.Internal;

/// <summary>
/// Configuration for Apache Guacamole remote desktop access.
/// Maps to the "GuacamoleSettings" section in appsettings.json.
/// </summary>
public class GuacamoleSettings
{
    /// <summary>
    /// Guacd proxy host
    /// </summary>
    public string GuacdHost { get; set; } = "localhost";

    /// <summary>
    /// Guacd proxy port
    /// </summary>
    public int GuacdPort { get; set; } = 4822;

    /// <summary>
    /// Guacamole API base URL
    /// </summary>
    public string GuacamoleApiUrl { get; set; } = "http://localhost:8081/guacamole/api";

    /// <summary>
    /// Pre-configured Guacamole authentication token
    /// </summary>
    public string GuacamoleAuthToken { get; set; } = string.Empty;

    /// <summary>
    /// Connection timeout in seconds for Guacamole API calls
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Public URL for Guacamole web client (used for user-facing RDP links).
    /// If empty, falls back to deriving from GuacamoleApiUrl.
    /// Example: "http://10.0.7.118:8081/guacamole"
    /// </summary>
    public string GuacamolePublicUrl { get; set; } = string.Empty;
}
