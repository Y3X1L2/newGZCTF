using System;
using GZCTF.Modules.Theory.Application;
using Xunit;

namespace GZCTF.Test.UnitTests.Theory;

public sealed class TheoryTagNormalizationTests
{
    [Fact]
    public void NormalizeMany_TrimsCollapsesAndDeduplicatesTags()
    {
        var tags = TheoryTagNormalizer.NormalizeMany(["  web   security ", "WEB SECURITY", "Linux"]);

        Assert.Collection(tags,
            tag =>
            {
                Assert.Equal("web security", tag.DisplayName);
                Assert.Equal("WEB SECURITY", tag.NormalizedName);
            },
            tag => Assert.Equal("LINUX", tag.NormalizedName));
    }

    [Fact]
    public void Normalize_RejectsEmptyAndOversizedTags()
    {
        Assert.Throws<ArgumentException>(() => TheoryTagNormalizer.Normalize("  "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TheoryTagNormalizer.Normalize(new string('x', TheoryTagNormalizer.MaxTagLength + 1)));
    }
}
