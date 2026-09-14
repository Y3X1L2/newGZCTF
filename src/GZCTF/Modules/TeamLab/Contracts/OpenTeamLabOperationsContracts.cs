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

public static class OpenTeamLabOperationsMapping
{
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
