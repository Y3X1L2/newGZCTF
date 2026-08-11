using GZCTF.Agent.Models;

namespace GZCTF.Agent.Services;

public enum AgentOperationCategory
{
    DockerCreate,
    VmCreate,
    DockerImageTransfer,
    VmImageTransfer,
    TeamLabNetwork,
    TeamLabExecution,
    Control
}

public sealed class AgentOperationGate
{
    readonly IReadOnlyDictionary<AgentOperationCategory, SemaphoreSlim> _gates;

    public AgentOperationGate(Microsoft.Extensions.Options.IOptions<AgentConfig> options)
    {
        var cpu = Math.Max(1, Environment.ProcessorCount);
        var configured = options.Value.ExecutionLimits;
        _gates = new Dictionary<AgentOperationCategory, SemaphoreSlim>
        {
            [AgentOperationCategory.DockerCreate] = Gate(configured.DockerCreates ?? Math.Clamp(cpu / 2, 2, 8)),
            [AgentOperationCategory.VmCreate] = Gate(configured.VmCreates ?? (cpu >= 16 ? 2 : 1)),
            [AgentOperationCategory.DockerImageTransfer] = Gate(configured.DockerImageTransfers ?? 2),
            [AgentOperationCategory.VmImageTransfer] = Gate(configured.VmImageTransfers ?? 1),
            [AgentOperationCategory.TeamLabNetwork] = Gate(configured.TeamLabNetworkOperations ?? 4),
            [AgentOperationCategory.TeamLabExecution] = Gate(configured.TeamLabExecutionOperations ?? 1),
            [AgentOperationCategory.Control] = Gate(configured.ControlOperations ?? 2)
        };
    }

    public async ValueTask<IAsyncDisposable> EnterAsync(AgentOperationCategory category, CancellationToken token)
    {
        var gate = _gates[category];
        await gate.WaitAsync(token);
        return new Releaser(gate);
    }

    static SemaphoreSlim Gate(int limit) => new(Math.Max(1, limit), Math.Max(1, limit));

    sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
