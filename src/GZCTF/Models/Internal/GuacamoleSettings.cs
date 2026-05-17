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
}
