namespace GZCTF.Services.Fleet;

public interface INginxProxySyncService
{
    Task TrySyncNowAsync(string reason, CancellationToken token = default);
}
