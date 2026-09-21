namespace GZCTF.Modules.TeamLab.Contracts;

/// <summary>Internal business-module lookup of an immutable published release; caller authorizes configuration writes.</summary>
public interface ITeamLabReleaseCatalog
{
    Task<bool> IsAvailableAsync(Guid topologyId, Guid releaseId, CancellationToken cancellationToken);
}
