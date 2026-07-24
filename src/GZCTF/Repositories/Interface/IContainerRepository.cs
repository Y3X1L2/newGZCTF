using GZCTF.Models.Request.Admin;
using System.Security.Cryptography;
using System.Text;

namespace GZCTF.Repositories.Interface;

/// <summary>
/// 端口映射信息（用于 Nginx 代理同步）
/// </summary>
public record PortMappingEntry(int PublicPort, string IP, int Port, Guid LeaseId);

public sealed class PortMapAckRequest
{
    public string Revision { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public Guid[] LeaseIds { get; set; } = [];
    public string? Error { get; set; }
}

public sealed record PortMapAckResponse(string Revision, int UpdatedEntries);

public static class PortMappingRevision
{
    public static string Compute(IEnumerable<PortMappingEntry> mappings)
    {
        var canonical = string.Join('\n', mappings
            .OrderBy(mapping => mapping.PublicPort)
            .ThenBy(mapping => mapping.LeaseId)
            .Select(mapping =>
                $"{mapping.PublicPort}|{mapping.IP}|{mapping.Port}|{mapping.LeaseId:N}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static bool Matches(
        string? revision,
        IEnumerable<Guid> acknowledgedLeaseIds,
        IReadOnlyCollection<PortMappingEntry> currentMappings)
    {
        if (!string.Equals(revision, Compute(currentMappings), StringComparison.Ordinal))
            return false;

        var current = currentMappings.Select(mapping => mapping.LeaseId).Order().ToArray();
        var acknowledged = acknowledgedLeaseIds.Distinct().Order().ToArray();
        return current.SequenceEqual(acknowledged);
    }

    public static string NormalizeError(string? error)
    {
        const string fallback = "Public gateway failed to publish the instance route.";
        var normalized = string.IsNullOrWhiteSpace(error) ? fallback : error.Trim();
        return normalized[..Math.Min(normalized.Length, 512)];
    }
}

public interface IContainerRepository : IRepository
{
    /// <summary>
    /// Get container by database ID
    /// </summary>
    /// <param name="guid">container ID</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Container?> GetContainerById(Guid guid, CancellationToken token = default);

    /// <summary>
    /// Get container with instance info by database ID
    /// </summary>
    /// <param name="guid">container ID</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Container?> GetContainerWithInstanceById(Guid guid, CancellationToken token = default);

    /// <summary>
    /// Check if the container exists by database ID
    /// </summary>
    /// <param name="guid">container ID</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<bool> ValidateContainer(Guid guid, CancellationToken token = default);

    /// <summary>
    /// Get all container instances
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<ContainerInstanceModel[]> GetContainerInstances(CancellationToken token = default);

    /// <summary>
    /// Get all containers that are about to be stopped
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Container[]> GetDyingContainers(CancellationToken token = default);

    /// <summary>
    /// Get active container port mappings for Nginx proxy synchronization
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<PortMappingEntry[]> GetProxyPortMappingsAsync(CancellationToken token = default);

    /// <summary>
    /// Apply a gateway publication result to the current public port leases.
    /// </summary>
    public Task<int> SetEntryPublicationResultAsync(
        IReadOnlyCollection<Guid> leaseIds,
        ContainerEntryStatus status,
        string? error,
        CancellationToken token = default);

    /// <summary>
    /// Extend container lifetime
    /// </summary>
    /// <param name="container">container</param>
    /// <param name="time">extension period</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task ExtendLifetime(Container container, TimeSpan time, CancellationToken token = default);

    /// <summary>
    /// Destroy container and remove it from database
    /// </summary>
    /// <param name="container"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<bool> DestroyContainer(Container container, CancellationToken token = default);
}
