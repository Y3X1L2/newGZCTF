using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using GZCTF.TeamLab.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record OpenTeamLabAssetFileEntryModel(string Name, string Kind, long Size);

public sealed record OpenTeamLabAssetFileListModel(IReadOnlyList<OpenTeamLabAssetFileEntryModel> Items);

public sealed class OpenUploadTeamLabAssetFileModel
{
    [Range(1, int.MaxValue)]
    [FromForm(Name = "generation")]
    [JsonPropertyName("generation")]
    public int Generation { get; set; }

    [Required, StringLength(1024, MinimumLength = 1)]
    [FromForm(Name = "path")]
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [Required]
    [FromForm(Name = "file")]
    [JsonPropertyName("file")]
    public IFormFile File { get; set; } = null!;

    [FromForm(Name = "overwrite")]
    [JsonPropertyName("overwrite")]
    public bool Overwrite { get; set; }

    [FromForm(Name = "confirmed")]
    [JsonPropertyName("confirmed")]
    public bool Confirmed { get; set; }
}

public sealed record OpenCreateTeamLabAssetDirectoryModel(
    [param: Range(1, int.MaxValue)] int Generation,
    [param: Required, StringLength(1024, MinimumLength = 1)] string Path);

public sealed record OpenMoveTeamLabAssetFileModel(
    [param: Range(1, int.MaxValue)] int Generation,
    [param: Required, StringLength(1024, MinimumLength = 1)] string SourcePath,
    [param: Required, StringLength(1024, MinimumLength = 1)] string DestinationPath,
    bool Overwrite = false,
    bool Confirmed = false);

public sealed record OpenTeamLabRemoteAuditEvidenceModel(
    long Id,
    long Size,
    string Sha256,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record OpenTeamLabRemoteAuditSummaryModel(
    string State,
    int RetentionDays,
    IReadOnlyList<OpenTeamLabRemoteAuditEvidenceModel> Evidence);

public sealed record OpenCreateTeamLabServiceAccessModel(
    [param: Required, RegularExpression("^(tcp|udp)$")] string Protocol,
    [param: Range(1, 65535)] int InternalPort,
    [param: Range(1, 65535)] int? PublicPort = null,
    [param: StringLength(63, MinimumLength = 1)] string? NetworkKey = null);

public sealed record OpenTeamLabServiceAccessModel(
    Guid Id,
    Guid RuntimeId,
    int Generation,
    int AssetId,
    string AssetName,
    string NetworkKey,
    string Protocol,
    int InternalPort,
    int PublicPort,
    string Endpoint,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

public sealed record OpenTeamLabAssetControlCommand(
    [param: Range(1, int.MaxValue)] int Generation,
    [param: Required, RegularExpression("^(start|stop|restart|rebuild|pause|resume)$")] string Action,
    [param: Required, StringLength(500, MinimumLength = 4)] string Reason,
    bool Confirmed = false);

public sealed record OpenTeamLabAssetControlCapabilityModel(bool Allowed, string? Reason);

public sealed record OpenTeamLabAssetControlTicketModel(Guid TicketId);

public sealed record OpenTeamLabAssetControlTaskModel(
    Guid Id,
    string Status,
    string? Stage,
    string? ErrorCode,
    bool Retryable);

public static class OpenTeamLabOperationsMapping
{
    public static CreateTeamLabServiceAccessModel ToInternal(this OpenCreateTeamLabServiceAccessModel model) =>
        new(model.Protocol, model.InternalPort, model.PublicPort, model.NetworkKey);

    public static OpenTeamLabServiceAccessModel ToOpen(this TeamLabServiceAccessModel model) =>
        new(model.Id, model.RuntimeId, model.Generation, model.AssetId, model.AssetName, model.NetworkKey,
            model.Protocol, model.InternalPort, model.PublicPort, model.Endpoint, model.Status,
            model.CreatedAt, model.RevokedAt);

    public static TeamLabAssetControlCommand ToInternal(this OpenTeamLabAssetControlCommand model) =>
        new(model.Generation, model.Action, model.Reason, model.Confirmed);

    public static OpenTeamLabAssetControlCapabilityModel ToOpen(this TeamLabAssetControlAvailability model) =>
        new(model.Allowed, model.Reason);

    public static OpenTeamLabAssetControlTaskModel ToOpen(this TeamLabAssetControlTask model) =>
        new(model.Id, model.Status, model.Stage, model.ErrorCode, model.CanRetry);

    public static OpenTeamLabAssetFileListModel ToOpenFileList(this TeamLabFileResult result) =>
        new((result.Entries ?? [])
            .Select(item => new OpenTeamLabAssetFileEntryModel(item.Name, item.Kind, item.Size))
            .ToArray());

    public static OpenTeamLabRemoteAuditSummaryModel ToOpen(this TeamLabRemoteAuditPage page) =>
        new(page.State, page.RetentionDays, page.Items
            .Select(item => new OpenTeamLabRemoteAuditEvidenceModel(
                item.Id,
                item.Size,
                item.Sha256,
                item.CreatedAt,
                item.ExpiresAt))
            .ToArray());
}
