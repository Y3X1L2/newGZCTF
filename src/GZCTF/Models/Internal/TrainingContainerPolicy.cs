namespace GZCTF.Models.Internal;

/// <summary>
/// Training runtime policy, configured independently of public exercise containers.
/// </summary>
public sealed class TrainingContainerPolicy
{
    /// <summary>
    /// Maximum running and queued training containers per user across courses.
    /// Zero means unlimited. Reaching the limit never evicts an existing lab.
    /// </summary>
    public int MaxContainerCountPerUser { get; set; } = 3;
}
