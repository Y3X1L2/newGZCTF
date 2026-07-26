using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using GZCTF.Storage.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[Index(nameof(GameInstanceId))]
public class Container
{
    /// <summary>
    /// Container GUID
    /// </summary>
    /// <remarks>
    /// Used as the unique identifier for the container instance,
    /// corresponding to the database primary key
    /// </remarks>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// The Image used to create the container
    /// </summary>
    [Required]
    public string Image { get; set; } = string.Empty;

    /// <summary>
    /// Container ID
    /// </summary>
    /// <remarks>
    /// The container ID returned by the container management system (e.g., Docker),
    /// used for managing the container instance
    /// </remarks>
    [Required]
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Monotonic runtime generation used to reject stale create/control operations.
    /// </summary>
    public int RuntimeGeneration { get; set; } = 1;

    /// <summary>
    /// Container status
    /// </summary>
    [Required]
    public ContainerStatus Status { get; set; } = ContainerStatus.Pending;

    /// <summary>
    /// Container creation time
    /// </summary>
    [Required]
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Expected container stop time
    /// </summary>
    /// <remarks>
    /// Set to 2 hours to avoid immediate destruction after creation,
    /// actual destruction time is determined by the container manager
    /// </remarks>
    [Required]
    public DateTimeOffset ExpectStopAt { get; set; } = DateTimeOffset.UtcNow + TimeSpan.FromHours(2);

    /// <summary>
    /// Whether the container has a reverse proxy
    /// </summary>
    [Required]
    public bool IsProxy { get; set; }

    /// <summary>
    /// Local IP
    /// </summary>
    [Required]
    public string IP { get; set; } = string.Empty;

    /// <summary>
    /// Local port
    /// </summary>
    [Required]
    public int Port { get; set; }

    /// <summary>
    /// Public IP
    /// </summary>
    public string? PublicIP { get; set; }

    /// <summary>
    /// Public port
    /// </summary>
    public int? PublicPort { get; set; }

    /// <summary>
    /// Stable owner identity used for compare-owner public port lease operations.
    /// </summary>
    public Guid? PublicPortLeaseId { get; set; }

    /// <summary>
    /// Publication state of the player-facing entry.
    /// Direct entries are ready immediately; gateway-backed entries require an acknowledgement.
    /// </summary>
    public ContainerEntryStatus EntryStatus { get; set; } = ContainerEntryStatus.Ready;

    /// <summary>
    /// Time when the current player-facing route was confirmed by the gateway.
    /// </summary>
    public DateTimeOffset? EntryReadyAt { get; set; }

    /// <summary>
    /// Sanitized route publication failure exposed to the player.
    /// </summary>
    [MaxLength(512)]
    public string? EntryError { get; set; }

    /// <summary>
    /// Container instance access method
    /// </summary>
    [NotMapped]
    public string Entry => IsProxy ? Id.ToString() : $"{PublicIP ?? IP}:{PublicPort ?? Port}";

    /// <summary>
    /// Player-facing entry. Pending and failed routes are intentionally withheld.
    /// </summary>
    [NotMapped]
    public string? ReadyEntry => EntryStatus == ContainerEntryStatus.Ready ? Entry : null;

    public bool IsActiveAt(DateTimeOffset timestamp) =>
        Status is ContainerStatus.Pending or ContainerStatus.Running && ExpectStopAt > timestamp;

    /// <summary>
    /// Whether traffic capture is enabled
    /// </summary>
    [NotMapped]
    public bool EnableTrafficCapture => GameInstance?.Challenge.EnableTrafficCapture ?? false;

    /// <summary>
    /// Shortened container GUID for logging purposes
    /// </summary>
    [NotMapped]
    internal string ShortId => Id.ToString("N")[..12];

    /// <summary>
    /// The container ID for logging purposes
    /// </summary>
    [NotMapped]
    internal string LogId => $"<{ShortId}> {ContainerId}";

    /// <summary>
    /// Container instance traffic capture storage path
    /// </summary>
    public string TrafficPath(string conn)
    {
        if (GameInstance is null)
            return string.Empty;

        var shortId = Id.ToString("N")[..12];

        return StoragePath.Combine(PathHelper.Capture,
            GameInstance.ChallengeId.ToString(),
            GameInstance.ParticipationId.ToString(),
            $"{shortId}-{conn}.pcap");
    }

    /// <summary>
    /// Generate metadata for the container
    /// </summary>
    /// <returns></returns>
    public byte[]? GenerateMetadata(JsonSerializerOptions options)
    {
        if (GameInstance is not null)
            return JsonSerializer.SerializeToUtf8Bytes(
                new GameMetadata(
                    GameInstance.Challenge.Title,
                    GameInstance.ChallengeId,
                    GameInstance.Participation.Team.Name,
                    GameInstance.Participation.TeamId,
                    ContainerId,
                    GameInstance.FlagContext?.Flag
                ), options);

        return null;
    }

    #region Db Relationship

    public Guid? NodeId { get; set; }

    [ForeignKey(nameof(NodeId))]
    public WorkerNode? Node { get; set; }

    /// <summary>
    /// Game challenge instance object
    /// </summary>
    public GameInstance? GameInstance { get; set; }

    /// <summary>
    /// Game challenge instance object ID
    /// </summary>
    public int? GameInstanceId { get; set; }

    #endregion Db Relationship
}

internal record GameMetadata(
    string Challenge,
    int ChallengeId,
    string Team,
    int TeamId,
    string ContainerId,
    string? Flag);
