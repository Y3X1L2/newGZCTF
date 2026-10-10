namespace GZCTF.Modules.Runtime.Contracts;

/// <summary>Read-only execution/reference facts used before deleting TeamLab history.</summary>
public interface ITeamLabRecordReferenceQuery
{
    Task<bool> HasActiveRuntimeReferencesAsync(int runtimeId, Guid publicId, CancellationToken token);
    Task<IReadOnlyList<Guid>> GetActiveOperationIdsAsync(CancellationToken token);
}
