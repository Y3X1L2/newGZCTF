namespace GZCTF.Modules.Penetration.Domain;

public sealed class PenetrationObjective
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string TopologyAssetKey { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "General";
    public int Score { get; set; } = 100;
    public bool IsDynamic { get; set; } = true;
    public string? StaticFlag { get; set; }
    public string? FlagTemplate { get; set; }
    public int MaxAttempts { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsCheckpoint { get; set; }
    public string PrerequisiteObjectiveKeysJson { get; set; } = "[]";
    public int OrderIndex { get; set; }
}
