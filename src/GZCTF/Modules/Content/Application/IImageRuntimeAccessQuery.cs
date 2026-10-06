using GZCTF.Modules.Content.Contracts;

namespace GZCTF.Modules.Content.Application;

public interface IImageRuntimeAccessQuery
{
    Task<IReadOnlyDictionary<int, ImageRuntimeAccessSummary>> GetBatchAsync(
        IReadOnlyCollection<int> templateIds, CancellationToken cancellationToken);
}
