using GZCTF.Models.Internal;

namespace GZCTF.Models.Request.Admin;

/// <summary>
/// Global configuration update
/// </summary>
public class ConfigEditModel
{
    /// <summary>
    /// User policy
    /// </summary>
    public AccountPolicy? AccountPolicy { get; set; }

    /// <summary>
    /// Global configuration
    /// </summary>
    public GlobalConfig? GlobalConfig { get; set; }

    /// <summary>
    /// Game policy
    /// </summary>
    public ContainerPolicy? ContainerPolicy { get; set; }

    /// <summary>
    /// Training container quota, independent of public exercise policy
    /// </summary>
    public TrainingContainerPolicy? TrainingContainerPolicy { get; set; }
}
