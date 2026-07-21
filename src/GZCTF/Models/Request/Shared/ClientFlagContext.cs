namespace GZCTF.Models.Request.Shared;

public class ClientFlagContext
{
    /// <summary>
    /// Close time of the challenge instance
    /// </summary>
    public DateTimeOffset? CloseTime { get; set; }

    /// <summary>
    /// Connection method of the challenge instance
    /// </summary>
    public string? InstanceEntry { get; set; }

    /// <summary>
    /// Publication status of the challenge instance entry.
    /// </summary>
    public ContainerEntryStatus? InstanceEntryStatus { get; set; }

    /// <summary>
    /// Time when the current instance entry became available.
    /// </summary>
    public DateTimeOffset? InstanceEntryReadyAt { get; set; }

    /// <summary>
    /// Player-safe route publication failure.
    /// </summary>
    public string? InstanceEntryError { get; set; }

    /// <summary>
    /// Attachment URL
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Attachment file size
    /// </summary>
    public long? FileSize { get; set; }

    internal static ClientFlagContext FromInstance(Container? container, string? url, long? fileSize) =>
        new()
        {
            CloseTime = container?.ExpectStopAt,
            InstanceEntry = container?.ReadyEntry,
            InstanceEntryStatus = container?.EntryStatus,
            InstanceEntryReadyAt = container?.EntryReadyAt,
            InstanceEntryError = container?.EntryError,
            Url = url,
            FileSize = fileSize
        };
}
