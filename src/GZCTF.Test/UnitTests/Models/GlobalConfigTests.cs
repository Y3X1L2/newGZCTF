using GZCTF.Models.Internal;
using Xunit;

namespace GZCTF.Test.UnitTests.Models;

public class GlobalConfigTests
{
    [Theory]
    [InlineData(null, "YINYU")]
    [InlineData("", "YINYU")]
    [InlineData("  YINYU  ", "YINYU")]
    [InlineData("YINYU CTF", "YINYU CTF")]
    [InlineData("HiddenRange", "HiddenRange")]
    public void ToPlatformName_ReturnsConfiguredDisplayName(string? input, string expected)
    {
        Assert.Equal(expected, GlobalConfig.ToPlatformName(input));
    }

    [Fact]
    public void SplitSlogans_ReturnsDefaultTypewriterSlogansForLegacyDefault()
    {
        var slogans = GlobalConfig.SplitSlogans("专业赛事管理与攻防演练平台");

        Assert.Equal(GlobalConfig.DefaultSlogans, slogans);
    }

    [Fact]
    public void SplitSlogans_SupportsEditableMultilineSlogans()
    {
        var slogans = GlobalConfig.SplitSlogans("  第一条  \n\n第二条\r\n第三条 ");

        Assert.Equal(["第一条", "第二条", "第三条"], slogans);
    }

    [Fact]
    public void JoinSlogans_StoresTrimmedDistinctLines()
    {
        var stored = GlobalConfig.JoinSlogans([" 第一条 ", "", "第二条", "第一条"]);

        Assert.Equal("第一条\n第二条", stored);
    }
}
