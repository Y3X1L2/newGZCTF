using System.Net;
using System.Net.Sockets;
using GZCTF.Models.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabFabricLinkAllocator(
    AppDbContext context,
    IOptions<TeamLabNetworkConfig> options)
{
    private const int LinkPrefixLength = 30;
    private readonly TeamLabNetworkConfig _config = options.Value;

    public async Task<IReadOnlyList<TeamLabFabricLinkLease>> AllocateAsync(
        TeamLabRuntime runtime,
        IReadOnlyList<TeamLabRuntimeShard> shards,
        CancellationToken cancellationToken)
    {
        var existing = await context.TeamLabFabricLinkLeases
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation &&
                           item.ReleasedAt == null)
            .ToDictionaryAsync(item => item.ShardId, cancellationToken);
        var used = await context.TeamLabFabricLinkLeases.AsNoTracking()
            .Where(item => item.ReleasedAt == null)
            .Select(item => item.AllocatedCidr)
            .ToArrayAsync(cancellationToken);
        var allocated = new List<IPNetwork>();
        var result = new List<TeamLabFabricLinkLease>(shards.Count);
        foreach (var shard in shards.OrderBy(item => item.WorkerNodeId).ThenBy(item => item.Id))
        {
            if (existing.TryGetValue(shard.Id, out var lease))
            {
                if (lease.WorkerNodeId != shard.WorkerNodeId)
                    throw new TeamLabRuntimeExecutionException(
                        $"Fabric 链路租约 {shard.Id} 指向了其他 WorkerNode");
                result.Add(lease);
                continue;
            }

            var cidr = FirstFree(_config.FabricLinkPool, used.Concat(allocated))
                ?? throw new TeamLabApiContractException(
                    "fabric_link_pool_exhausted",
                    "TeamLab Fabric 链路池没有可用的 /30 网络",
                    409);
            allocated.Add(cidr);
            lease = new TeamLabFabricLinkLease
            {
                RuntimeId = runtime.Id,
                Runtime = runtime,
                Generation = runtime.Generation,
                ShardId = shard.Id,
                Shard = shard,
                WorkerNodeId = shard.WorkerNodeId,
                AllocatedCidr = cidr,
                HubAddress = HostAt(cidr, 1),
                NodeAddress = HostAt(cidr, 2)
            };
            context.TeamLabFabricLinkLeases.Add(lease);
            result.Add(lease);
        }
        return result;
    }

    internal static IPNetwork? FirstFree(string poolCidr, IEnumerable<IPNetwork> unavailable)
    {
        var pool = ParseNetwork(poolCidr);
        if (pool.PrefixLength > LinkPrefixLength)
            throw new TeamLabApiContractException(
                "fabric_link_pool_invalid",
                "TeamLab Fabric 链路池至少需要包含一个 /30 网络",
                500);
        var used = unavailable.Select(ToRange).ToArray();
        var poolRange = ToRange(pool);
        const uint size = 1u << (32 - LinkPrefixLength);
        for (var start = poolRange.Start; start <= poolRange.End - size + 1; start += size)
        {
            var end = start + size - 1;
            if (used.All(item => end < item.Start || item.End < start))
                return new IPNetwork(FromUInt32(start), LinkPrefixLength);
            if (uint.MaxValue - start < size) break;
        }
        return null;
    }

    private static IPNetwork ParseNetwork(string cidr)
    {
        var parts = cidr.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork || !int.TryParse(parts[1], out var prefix) ||
            prefix is < 1 or > LinkPrefixLength)
            throw new TeamLabApiContractException(
                "fabric_link_pool_invalid",
                $"TeamLab Fabric 链路池 '{cidr}' 不是有效的 IPv4 CIDR",
                500);
        var raw = ToUInt32(address);
        var mask = uint.MaxValue << (32 - prefix);
        if (raw != (raw & mask))
            throw new TeamLabApiContractException(
                "fabric_link_pool_invalid",
                $"TeamLab Fabric 链路池 '{cidr}' 未按网络边界对齐",
                500);
        return new IPNetwork(address, prefix);
    }

    private static string HostAt(IPNetwork network, uint offset)
    {
        var range = ToRange(network);
        return FromUInt32(range.Start + offset).ToString();
    }

    private static (uint Start, uint End) ToRange(IPNetwork network)
    {
        var start = ToUInt32(network.BaseAddress);
        var size = 1u << (32 - network.PrefixLength);
        return (start, start + size - 1);
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value) => new([
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
    ]);
}
