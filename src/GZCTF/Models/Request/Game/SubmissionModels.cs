namespace GZCTF.Models.Request.Game;

public sealed record SubmissionPageModel(IReadOnlyList<Submission> Items, string? NextCursor);
