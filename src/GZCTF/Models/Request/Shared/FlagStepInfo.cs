using GZCTF.Models.Data;

namespace GZCTF.Models.Request.Shared;

/// <summary>
/// Multi-flag step metadata exposed to players without the answer value.
/// </summary>
public class FlagStepInfo
{
    public int Id { get; set; }
    public int OrderIndex { get; set; }
    public string? Description { get; set; }
}

internal static class FlagStepProjection
{
    internal static List<FlagStepInfo>? FromConfiguredFlags(
        ChallengeType challengeType,
        IReadOnlyCollection<FlagContext>? flags)
    {
        if (challengeType.IsDynamic() || flags is not { Count: > 1 })
            return null;

        return flags
            .OrderBy(flag => flag.OrderIndex)
            .Select(flag => new FlagStepInfo
            {
                Id = flag.Id,
                OrderIndex = flag.OrderIndex,
                Description = flag.Description
            })
            .ToList();
    }
}
