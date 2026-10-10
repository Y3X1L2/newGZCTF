namespace GZCTF.Modules.Runtime.Contracts;

/// <summary>Small durable receipts prevent replaying a retired creation key.</summary>
public interface ITeamLabCreationHistory
{
    Task LockAsync(Guid ownerId, string key, CancellationToken token);
    Task<string?> GetRetiredRequestHashAsync(Guid ownerId, string key, CancellationToken token);
    Task RetireAsync(Guid ownerId, string key, string requestHash, Guid publicId, CancellationToken token);
}
