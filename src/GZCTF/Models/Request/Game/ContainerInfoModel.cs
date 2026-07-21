namespace GZCTF.Models.Request.Game;

public class ContainerInfoModel
{
    /// <summary>
    /// Container status
    /// </summary>
    public ContainerStatus Status { get; set; } = ContainerStatus.Pending;

    /// <summary>
    /// Container creation time
    /// </summary>
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Expected container stop time
    /// </summary>
    public DateTimeOffset ExpectStopAt { get; set; } = DateTimeOffset.UtcNow + TimeSpan.FromHours(2);

    /// <summary>
    /// Challenge entry point
    /// </summary>
    public string? Entry { get; set; }

    /// <summary>
    /// Publication status of the challenge entry point.
    /// </summary>
    public ContainerEntryStatus EntryStatus { get; set; } = ContainerEntryStatus.Pending;

    /// <summary>
    /// Time when the challenge entry point became available.
    /// </summary>
    public DateTimeOffset? EntryReadyAt { get; set; }

    /// <summary>
    /// Player-safe route publication failure.
    /// </summary>
    public string? EntryError { get; set; }

    internal static ContainerInfoModel FromContainer(Container container) =>
        new()
        {
            Status = container.Status,
            StartedAt = container.StartedAt,
            ExpectStopAt = container.ExpectStopAt,
            Entry = container.ReadyEntry,
            EntryStatus = container.EntryStatus,
            EntryReadyAt = container.EntryReadyAt,
            EntryError = container.EntryError
        };
}
