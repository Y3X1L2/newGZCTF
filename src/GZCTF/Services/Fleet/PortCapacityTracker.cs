using System.Collections.Concurrent;

namespace GZCTF.Services.Fleet;

public class PortCapacityTracker
{
    private readonly ConcurrentDictionary<Guid, NodePortCapacity> _capacities = new();

    public void UpdateCapacity(Guid nodeId, int totalPorts, int usedPorts)
    {
        _capacities[nodeId] = new NodePortCapacity
        {
            TotalPorts = totalPorts, UsedPorts = usedPorts,
            AvailablePorts = totalPorts - usedPorts, LastUpdated = DateTimeOffset.UtcNow
        };
    }

    public bool HasCapacity(Guid nodeId, int requiredPorts) =>
        _capacities.TryGetValue(nodeId, out var cap) && cap.AvailablePorts >= requiredPorts;
}

public class NodePortCapacity
{
    public int TotalPorts { get; init; }
    public int UsedPorts { get; init; }
    public int AvailablePorts { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
}
