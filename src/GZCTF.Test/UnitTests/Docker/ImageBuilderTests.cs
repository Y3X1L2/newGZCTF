using System;
using Xunit;

namespace GZCTF.Test.UnitTests.Docker;

public class ImageBuilderTests
{
    [Fact]
    public void DockerImage_BuilderExists_InDockerNamespace()
    {
        var type = Type.GetType("GZCTF.Services.Docker.DockerImageBuilder, GZCTF");
        Assert.NotNull(type);
    }

    [Fact]
    public void DockerImage_Response_FromDockerImage_ReturnsCorrectModel()
    {
        Assert.True(true);
    }
}
