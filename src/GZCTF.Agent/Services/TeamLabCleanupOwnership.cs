namespace GZCTF.Agent.Services;

/// <summary>
/// Whether a TeamLab cleanup request owns the resources whose names are shared across generations
/// of the same runtime — bridges, the router namespace, veth pairs and dnsmasq processes.
/// </summary>
internal enum TeamLabCleanupOwnership
{
    /// <summary>Ownership cannot be established; refuse rather than guess.</summary>
    Refuse,

    /// <summary>This generation may remove the shared resources it was given.</summary>
    OwnsSharedResources,

    /// <summary>
    /// Ownership is not proven — either another generation holds the node marker, or no marker
    /// exists at all. Shared resources are left alone so a cleanup can never delete what a
    /// concurrent generation is using; per-generation state is still removed.
    /// </summary>
    SharedResourcesNotOwned
}
