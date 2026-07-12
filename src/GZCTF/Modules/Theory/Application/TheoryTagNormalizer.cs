using System.Text.RegularExpressions;

namespace GZCTF.Modules.Theory.Application;

public static partial class TheoryTagNormalizer
{
    public const int MaxTagLength = 64;

    public static IReadOnlyList<NormalizedTheoryTag> NormalizeMany(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        return tags.Select(Normalize)
            .DistinctBy(tag => tag.NormalizedName, StringComparer.Ordinal)
            .ToArray();
    }

    public static NormalizedTheoryTag Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var displayName = WhitespaceRegex().Replace(value.Trim(), " ");
        if (displayName.Length > MaxTagLength)
            throw new ArgumentOutOfRangeException(nameof(value),
                $"Theory tag cannot exceed {MaxTagLength} characters.");

        return new NormalizedTheoryTag(displayName, displayName.ToUpperInvariant());
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

public sealed record NormalizedTheoryTag(string DisplayName, string NormalizedName);
