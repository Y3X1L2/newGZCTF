namespace GZCTF.Modules.Ctf.Domain;

public enum ChallengeMutationKind
{
    Import = 0,
    Delete = 1
}

public sealed class ChallengeMutationJob
{
    public Guid OperationId { get; set; }
    public int GameId { get; set; }
    public ChallengeMutationKind Kind { get; set; }
    public string? PayloadJson { get; set; }
    public string? ResultJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
