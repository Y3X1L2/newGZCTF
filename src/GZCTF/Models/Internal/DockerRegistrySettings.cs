using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Internal;

/// <summary>
/// Settings for the internal Docker registry used by uploaded challenge images.
/// </summary>
public class DockerRegistrySettings
{
    public const string FixedAddress = "10.24.0.28:5000";

    /// <summary>
    /// Registry host and port. Empty configuration falls back to the fixed platform registry.
    /// </summary>
    [MaxLength(256)]
    public string Address { get; set; } = FixedAddress;

    /// <summary>
    /// Optional repository prefix inside the registry.
    /// </summary>
    [MaxLength(128)]
    public string Namespace { get; set; } = "ctf";

    /// <summary>
    /// Maximum Docker archive upload size in GB.
    /// </summary>
    [Range(1, 100)]
    public int MaxUploadSizeGb { get; set; } = 10;

    public string NormalizedAddress
    {
        get
        {
            var value = string.IsNullOrWhiteSpace(Address) ? FixedAddress : Address.Trim().TrimEnd('/');
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return value["http://".Length..];
            if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return value["https://".Length..];
            return value;
        }
    }

    public string NormalizedNamespace => Namespace.Trim().Trim('/');

    public long MaxUploadSizeBytes => (long)Math.Max(1, MaxUploadSizeGb) * 1024 * 1024 * 1024;
}
