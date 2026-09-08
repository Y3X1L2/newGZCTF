using System;
using System.Text;
using GZCTF.Agent.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabDiagnosticOutputTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TruncatedMultibyteSuffixIsNotRenderedAsCorruption(int missingBytes)
    {
        var bytes = Encoding.UTF8.GetBytes("ok\u4e2d");
        Assert.Equal("ok", DockerService.DecodeDiagnosticOutput(bytes[..^missingBytes], true));
    }

    [Fact]
    public void CompleteCharactersAndActualInvalidBytesArePreserved()
    {
        Assert.Equal("ok\u4e2d", DockerService.DecodeDiagnosticOutput(Encoding.UTF8.GetBytes("ok\u4e2d"), true));
        Assert.Equal("\ufffd", DockerService.DecodeDiagnosticOutput(new byte[] { 0xff }, false));
        Assert.Equal(string.Empty, DockerService.DecodeDiagnosticOutput(Array.Empty<byte>(), true));
    }
}
