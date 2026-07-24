namespace GZCTF.Modules.TeamLab.Domain.Runtime;

public enum TeamLabRuntimeStatus : byte
{
    Pending = 0,
    Planning = 1,
    Scheduled = 2,
    Deploying = 3,
    Probing = 4,
    Running = 5,
    Failed = 6,
    CleanupPending = 7,
    Stopped = 8,
    Destroying = 9,
    Destroyed = 10
}

public enum TeamLabResetCheckpoint : byte
{
    CleaningPreviousGeneration = 0,
    PlanningNextGeneration = 1,
    ReservingNextGeneration = 2,
    DeployingNextGeneration = 3
}

public static class TeamLabResetCheckpointFacts
{
    private const string ObjectType = "reset-checkpoint";

    public static TeamLabResetCheckpoint? Get(TeamLabRuntime runtime, Guid ticketId) =>
        runtime.Events
            .Where(item => item.ObjectType == ObjectType &&
                           item.ObjectId == ticketId.ToString("D"))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Select(item => Enum.TryParse<TeamLabResetCheckpoint>(item.Stage, out var checkpoint)
                ? checkpoint
                : (TeamLabResetCheckpoint?)null)
            .FirstOrDefault(item => item.HasValue);

    public static void Record(
        TeamLabRuntime runtime,
        Guid ticketId,
        int targetGeneration,
        TeamLabResetCheckpoint checkpoint)
    {
        if (Get(runtime, ticketId) == checkpoint)
            return;

        runtime.Events.Add(new TeamLabEvent
        {
            RuntimeId = runtime.Id,
            Generation = targetGeneration,
            Stage = checkpoint.ToString(),
            Level = TeamLabEventLevel.Info,
            Message = $"Reset checkpoint persisted: {checkpoint}.",
            ObjectType = ObjectType,
            ObjectId = ticketId.ToString("D"),
            CreatedAt = DateTimeOffset.UtcNow
        });
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum TeamLabResourceKind : byte
{
    Docker = 0,
    Vm = 1,
    RouterNamespace = 2,
    DhcpDnsService = 3,
    WireGuard = 4,
    PublicUdpMapping = 5
}

public enum TeamLabAssetExecutionStage : byte
{
    Pending = 0,
    GuestReady = 1,
    BootstrapCompleted = 2,
    ServiceReady = 3,
    Failed = 4
}

public enum TeamLabEventLevel : byte
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public enum TeamLabTrafficCaptureStatus : byte
{
    Pending = 0,
    Running = 1,
    Stopping = 2,
    Completed = 3,
    Failed = 4,
    Expired = 5,
    CleanupPending = 6
}

public enum TeamLabAccessGrantType : byte
{
    WireGuard = 0
}
