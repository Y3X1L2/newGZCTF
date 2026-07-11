namespace GZCTF.Modules.Identity.Application;

public interface IApiTokenResourceGrantPolicy
{
    string ResourceType { get; }
    Task<bool> CanGrantAsync(
        ActorContext actor,
        string resourceId,
        CancellationToken cancellationToken);
}
